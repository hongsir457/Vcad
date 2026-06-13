using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vcad.Plugin.Config;

namespace Vcad.Plugin.Net
{
    /// <summary>
    /// Calls the local Vcad.AgentLite service running on 127.0.0.1.
    /// Never sends data to any VCAD-owned endpoint.
    /// </summary>
    internal class AgentLiteClient
    {
        private readonly AgentSettings _settings;

        public AgentLiteClient(AgentSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<bool> HealthAsync()
        {
            var url = BuildUrl("/health");
            using (var client = NewClient())
            {
                try
                {
                    var resp = await client.GetAsync(url).ConfigureAwait(false);
                    return resp.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<AgentTurnResult> AgentTurnAsync(
            string sessionId,
            string message,
            JArray attachments,
            JObject cadObservation,
            JArray toolResults)
        {
            var payload = BuildAgentTurnPayload(sessionId, message, attachments, cadObservation, toolResults);

            using (var client = NewClient())
            using (var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json"))
            {
                var resp = await client.PostAsync(BuildUrl("/agent/turn"), content).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(FormatAgentTurnFailure((int)resp.StatusCode, body));
                }

                var envelope = JObject.Parse(body);
                var response = envelope["response"] as JObject;
                if (response == null)
                {
                    throw new InvalidOperationException("Agent response is missing 'response'.");
                }
                return AgentTurnResult.FromJson(response);
            }
        }

        public async Task<AgentTurnResult> AgentTurnStreamingAsync(
            string sessionId,
            string message,
            JArray attachments,
            JObject cadObservation,
            JArray toolResults,
            Func<string, Task> onDelta)
        {
            var payload = BuildAgentTurnPayload(sessionId, message, attachments, cadObservation, toolResults);
            using (var client = NewClient())
            using (var req = new HttpRequestMessage(HttpMethod.Post, BuildUrl("/agent/turn/stream")))
            {
                req.Content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
                using (var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    if (!resp.IsSuccessStatusCode)
                    {
                        var errorBody = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new InvalidOperationException(FormatAgentTurnFailure((int)resp.StatusCode, errorBody));
                    }

                    AgentTurnResult final = null;
                    using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        while (!reader.EndOfStream)
                        {
                            var line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(line)) continue;

                            var envelope = JObject.Parse(line);
                            var eventName = envelope.Value<string>("event");
                            var data = envelope["data"] as JObject;
                            if (string.Equals(eventName, "delta", StringComparison.OrdinalIgnoreCase))
                            {
                                var text = data?.Value<string>("text");
                                if (!string.IsNullOrEmpty(text) && onDelta != null)
                                {
                                    await onDelta(text).ConfigureAwait(false);
                                }
                            }
                            else if (string.Equals(eventName, "final", StringComparison.OrdinalIgnoreCase))
                            {
                                var response = data?["response"] as JObject;
                                if (response != null)
                                {
                                    final = AgentTurnResult.FromJson(response);
                                }
                            }
                            else if (string.Equals(eventName, "error", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new InvalidOperationException(FormatAgentTurnFailure(200, data?.ToString(Formatting.None)));
                            }
                        }
                    }

                    if (final == null)
                    {
                        throw new InvalidOperationException("Agent stream ended without a final response.");
                    }
                    return final;
                }
            }
        }

        public async Task<JObject> RunToolAsync(string name, JObject args)
        {
            var payload = new JObject
            {
                ["name"] = name,
                ["args"] = args ?? new JObject(),
            };
            using (var client = NewClient())
            using (var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json"))
            {
                var resp = await client.PostAsync(BuildUrl("/tool"), content).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Agent /tool failed: " + (int)resp.StatusCode + " " +
                        SecretRedactor.Redact(body));
                }
                return JObject.Parse(body);
            }
        }

        private string BuildUrl(string path)
        {
            var port = _settings.AgentPort == 0 ? 8765 : _settings.AgentPort;
            return "http://127.0.0.1:" + port + path;
        }

        private JObject BuildAgentTurnPayload(
            string sessionId,
            string message,
            JArray attachments,
            JObject cadObservation,
            JArray toolResults)
        {
            var payload = new JObject
            {
                ["session_id"] = string.IsNullOrWhiteSpace(sessionId) ? "session-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") : sessionId,
                ["message"] = message ?? "",
                ["provider"] = BuildProviderPayload(),
            };
            if (attachments != null && attachments.Count > 0)
            {
                payload["attachments"] = attachments;
            }
            if (cadObservation != null)
            {
                payload["cad_observation"] = cadObservation;
            }
            if (toolResults != null && toolResults.Count > 0)
            {
                payload["tool_results"] = toolResults;
            }
            return payload;
        }

        private static string FormatAgentTurnFailure(int localStatus, string body)
        {
            var redacted = SecretRedactor.Redact(body ?? "");
            try
            {
                var jo = JObject.Parse(redacted);
                var upstreamStatus = jo.Value<int?>("upstream_status");
                var provider = jo.Value<string>("provider");
                var error = FirstNonEmpty(
                    ExtractProviderErrorMessage(jo.Value<string>("upstream_body")),
                    jo.Value<string>("error"),
                    redacted);

                var message = upstreamStatus.HasValue
                    ? "模型连接失败: 上游 " + FirstNonEmpty(provider, "provider") + " " + upstreamStatus.Value + "。 " + error
                    : "模型连接失败: Agent Lite " + localStatus + "。 " + error;
                if (LooksLikeModelAccessError(error))
                {
                    message += "\r\n当前 Project 没有所选模型权限；API Key 的 All 权限只表示这个 Key 在当前 Project 内的操作权限，不等于所有模型都已开通。";
                }
                return message;
            }
            catch
            {
                return "模型连接失败: Agent Lite " + localStatus + "。 " + redacted;
            }
        }

        private static string ExtractProviderErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var jo = JObject.Parse(body);
                var error = jo["error"] as JObject;
                if (error != null)
                {
                    var message = error.Value<string>("message");
                    var code = error.Value<string>("code");
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return string.IsNullOrWhiteSpace(code) ? message : message + " (" + code + ")";
                    }
                }
            }
            catch
            {
                // Fall through to raw provider body.
            }
            return body;
        }

        private static bool LooksLikeModelAccessError(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            var s = message.ToLowerInvariant();
            return s.Contains("does not have access to model") ||
                s.Contains("model_not_found") ||
                s.Contains("model not found");
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }

        public async Task<ConnectionCheckResult> TestModelAsync()
        {
            using (var client = NewClient())
            {
                try
                {
                    var health = await client.GetAsync(BuildUrl("/health")).ConfigureAwait(false);
                    if (!health.IsSuccessStatusCode)
                    {
                        return ConnectionCheckResult.Fail("Agent Lite 已启动，但 /health 返回 " + (int)health.StatusCode + "。");
                    }
                }
                catch (Exception ex)
                {
                    return ConnectionCheckResult.Fail(
                        "Agent Lite is not reachable. The plugin will auto-start the bundled service when available. Details: " +
                        SecretRedactor.Redact(ex.Message));
                }

                try
                {
                    var payload = new JObject
                    {
                        ["session_id"] = "test-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                        ["message"] = "请只回复 ready，不要调用 CAD 工具。",
                        ["provider"] = BuildProviderPayload(),
                    };
                    using (var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json"))
                    {
                        var resp = await client.PostAsync(BuildUrl("/agent/turn"), content).ConfigureAwait(false);
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            return ConnectionCheckResult.Fail(FormatAgentTurnFailure((int)resp.StatusCode, body));
                        }
                        var jo = JObject.Parse(body);
                        if (jo["response"] == null)
                        {
                            return ConnectionCheckResult.Fail("模型返回成功，但没有 agent response 字段。");
                        }
                        return ConnectionCheckResult.Ok("模型连接成功。");
                    }
                }
                catch (Exception ex)
                {
                    return ConnectionCheckResult.Fail("模型连接失败: " + SecretRedactor.Redact(ex.Message));
                }
            }
        }

        private JObject BuildProviderPayload()
        {
            var provider = new JObject
            {
                ["name"] = string.IsNullOrWhiteSpace(_settings.Provider) ? "echo" : _settings.Provider,
                ["strict_json"] = _settings.StrictJson,
            };

            if (!string.IsNullOrWhiteSpace(_settings.ApiBaseUrl))
            {
                provider["base_url"] = _settings.ApiBaseUrl;
            }
            if (!string.IsNullOrWhiteSpace(_settings.Model))
            {
                provider["model"] = _settings.Model;
            }
            if (!string.IsNullOrWhiteSpace(_settings.ApiKeyPlain))
            {
                provider["api_key"] = _settings.ApiKeyPlain;
            }

            return provider;
        }

        private HttpClient NewClient()
        {
            var timeoutSeconds = _settings.TimeoutSeconds <= 120 ? 300 : _settings.TimeoutSeconds;
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            };
            client.DefaultRequestHeaders.Add("X-VCAD-Agent-Token", AgentTokenStore.GetOrCreate());
            return client;
        }
    }

    internal class ConnectionCheckResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }

        public static ConnectionCheckResult Ok(string message)
        {
            return new ConnectionCheckResult { Success = true, Message = message };
        }

        public static ConnectionCheckResult Fail(string message)
        {
            return new ConnectionCheckResult { Success = false, Message = message };
        }
    }

    internal class AgentTurnResult
    {
        public string SessionId { get; set; }
        public string AssistantMessage { get; set; }
        public JObject CadBrief { get; set; }
        public JObject TaskPlan { get; set; }
        public JObject CadIr { get; set; }
        public JObject Safety { get; set; }
        public JObject Validation { get; set; }
        public JArray Trace { get; set; }
        public JArray ToolCalls { get; set; }
        public JObject Clarification { get; set; }
        public bool RequiresUserInput { get; set; }
        public bool Done { get; set; }
        public AgentUsage Usage { get; set; }

        public bool NeedsClarification =>
            RequiresUserInput || Clarification != null;

        public static AgentTurnResult FromJson(JObject obj)
        {
            if (obj == null) return null;
            return new AgentTurnResult
            {
                SessionId = Value(obj, "session_id", "SessionId"),
                AssistantMessage = Value(obj, "assistant_message", "AssistantMessage"),
                CadBrief = obj["cad_brief"] as JObject,
                TaskPlan = obj["task_plan"] as JObject,
                CadIr = obj["cad_ir"] as JObject,
                Safety = obj["safety"] as JObject,
                Validation = obj["validation"] as JObject,
                Trace = obj["trace"] as JArray ?? new JArray(),
                ToolCalls = obj["tool_calls"] as JArray ?? new JArray(),
                Clarification = obj["clarification"] as JObject,
                RequiresUserInput = obj.Value<bool?>("requires_user_input") ?? false,
                Done = obj.Value<bool?>("done") ?? false,
                Usage = AgentUsage.FromJson(obj["usage"] as JObject),
            };
        }

        private static string Value(JObject obj, params string[] names)
        {
            foreach (var name in names)
            {
                var token = obj[name];
                if (token != null && token.Type != JTokenType.Null) return token.Value<string>();
            }
            return "";
        }
    }

    internal class AgentUsage
    {
        public string Provider { get; set; }
        public string Model { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens { get; set; }
        public string Source { get; set; }

        public static AgentUsage FromJson(JObject obj)
        {
            if (obj == null) return null;
            return new AgentUsage
            {
                Provider = Value(obj, "provider", "Provider"),
                Model = Value(obj, "model", "Model"),
                InputTokens = IntValue(obj, "inputTokens", "input_tokens", "InputTokens"),
                OutputTokens = IntValue(obj, "outputTokens", "output_tokens", "OutputTokens"),
                TotalTokens = IntValue(obj, "totalTokens", "total_tokens", "TotalTokens"),
                Source = Value(obj, "source", "Source"),
            };
        }

        private static string Value(JObject obj, params string[] names)
        {
            foreach (var name in names)
            {
                var token = obj[name];
                if (token != null && token.Type != JTokenType.Null) return token.Value<string>();
            }
            return "";
        }

        private static int IntValue(JObject obj, params string[] names)
        {
            foreach (var name in names)
            {
                var token = obj[name];
                if (token != null && token.Type != JTokenType.Null) return token.Value<int>();
            }
            return 0;
        }
    }
}
