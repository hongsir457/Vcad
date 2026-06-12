using System;
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

        public Task<string> ParseAsync(string naturalLanguage)
        {
            return ParseAsync(naturalLanguage, null, null);
        }

        public async Task<string> ParseAsync(string naturalLanguage, JArray attachments)
        {
            return await ParseAsync(naturalLanguage, attachments, null).ConfigureAwait(false);
        }

        public async Task<string> ParseAsync(string naturalLanguage, JArray attachments, JObject cadState)
        {
            var result = await ParseFullAsync(naturalLanguage, attachments, cadState).ConfigureAwait(false);
            return result?.DslJson;
        }

        public async Task<AgentParseResult> ParseFullAsync(string naturalLanguage, JArray attachments, JObject cadState)
        {
            if (string.IsNullOrWhiteSpace(naturalLanguage)) return null;

            var payload = new JObject
            {
                ["request_id"] = "req-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                ["text"] = naturalLanguage,
                ["context"] = new JObject { ["unit"] = "mm", ["coordinate_system"] = "WCS" },
                ["options"] = new JObject { ["max_commands"] = 50 },
                ["provider"] = BuildProviderPayload(),
            };
            if (attachments != null && attachments.Count > 0)
            {
                payload["attachments"] = attachments;
            }
            if (cadState != null)
            {
                payload["cad_state"] = cadState;
            }

            using (var client = NewClient())
            using (var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json"))
            {
                var resp = await client.PostAsync(BuildUrl("/parse"), content).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Agent /parse failed: " + (int)resp.StatusCode + " " +
                        SecretRedactor.Redact(body));
                }

                var jo = JObject.Parse(body);
                var dsl = jo["dsl"];
                return new AgentParseResult
                {
                    RequestId = jo.Value<string>("request_id"),
                    DslJson = dsl == null || dsl.Type == JTokenType.Null ? null : dsl.ToString(Formatting.Indented),
                    AssistantMessage = jo.Value<string>("assistant_message"),
                    Clarification = jo["clarification"] as JObject,
                    Usage = AgentUsage.FromJson(jo["usage"] as JObject),
                };
            }
        }

        private string BuildUrl(string path)
        {
            var port = _settings.AgentPort == 0 ? 8765 : _settings.AgentPort;
            return "http://127.0.0.1:" + port + path;
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
                        ["request_id"] = "test-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                        ["text"] = "draw a small text label VCAD TEST",
                        ["context"] = new JObject { ["unit"] = "mm", ["coordinate_system"] = "WCS" },
                        ["options"] = new JObject { ["max_commands"] = 10 },
                        ["provider"] = BuildProviderPayload(),
                    };
                    using (var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json"))
                    {
                        var resp = await client.PostAsync(BuildUrl("/parse"), content).ConfigureAwait(false);
                        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode)
                        {
                            return ConnectionCheckResult.Fail("模型连接失败: " + (int)resp.StatusCode + " " + SecretRedactor.Redact(body));
                        }
                        var jo = JObject.Parse(body);
                        if (jo["dsl"] == null)
                        {
                            return ConnectionCheckResult.Fail("模型返回成功，但没有 DSL 字段。");
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

    internal class AgentParseResult
    {
        public string RequestId { get; set; }
        public string DslJson { get; set; }
        public string AssistantMessage { get; set; }
        public JObject Clarification { get; set; }
        public AgentUsage Usage { get; set; }

        public bool NeedsClarification =>
            Clarification != null && Clarification.Value<bool?>("required") == true;
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
