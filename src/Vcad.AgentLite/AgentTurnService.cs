using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcad.AgentLite;

public sealed class AgentTurnService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(300) };

    public async Task<AgentTurnResponse> RunAsync(AgentTurnRequest req)
    {
        var options = ProviderRequestOptions.From(req.provider);
        var provider = string.IsNullOrWhiteSpace(options.Name) ? "echo" : options.Name.ToLowerInvariant();
        return provider switch
        {
            "openai" or "deepseek" or "custom" => await RunOpenAiCompatibleAsync(req, options),
            "anthropic" => await RunAnthropicAsync(req, options),
            _ => EchoTurn(req, options),
        };
    }

    private static async Task<AgentTurnResponse> RunOpenAiCompatibleAsync(AgentTurnRequest req, ProviderRequestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("VCAD_AGENT_API_KEY is not set.");
        }

        var isDeepSeek = string.Equals(options.Name, "deepseek", StringComparison.OrdinalIgnoreCase);
        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl)
            ? (isDeepSeek ? "https://api.deepseek.com" : "https://api.openai.com")
            : options.BaseUrl;
        var model = string.IsNullOrWhiteSpace(options.Model)
            ? (isDeepSeek ? "deepseek-v4-flash" : "gpt-5")
            : options.Model;

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = AgentSystemPrompt() },
                new { role = "user", content = BuildAgentUserPrompt(req) },
            },
            response_format = new { type = "json_object" },
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl(baseUrl, isDeepSeek));
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(http);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new ProviderRequestException("OpenAI-compatible", (int)resp.StatusCode, SecretRedactor.Redact(body));
        }

        var parsed = JsonNode.Parse(body);
        var content = parsed?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Agent model returned empty content.");
        }

        var response = ParseAgentResponse(content, req);
        response.usage = ExtractOpenAiUsage(parsed, options, parsed?["model"]?.GetValue<string>() ?? model, req, content);
        return response;
    }

    private static async Task<AgentTurnResponse> RunAnthropicAsync(AgentTurnRequest req, ProviderRequestOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new InvalidOperationException("VCAD_AGENT_API_KEY is not set.");
        }

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "https://api.anthropic.com" : options.BaseUrl;
        var model = string.IsNullOrWhiteSpace(options.Model) ? "claude-fable-5" : options.Model;
        var payload = new
        {
            model,
            max_tokens = 4096,
            system = AgentSystemPrompt(),
            messages = new object[]
            {
                new { role = "user", content = BuildAgentUserPrompt(req) },
            },
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/messages");
        http.Headers.Add("x-api-key", options.ApiKey);
        http.Headers.Add("anthropic-version", "2023-06-01");
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(http);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new ProviderRequestException("Anthropic", (int)resp.StatusCode, SecretRedactor.Redact(body));
        }

        var parsed = JsonNode.Parse(body);
        var content = parsed?["content"]?[0]?["text"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Agent model returned empty content.");
        }

        var response = ParseAgentResponse(content, req);
        response.usage = ExtractAnthropicUsage(parsed, options, parsed?["model"]?.GetValue<string>() ?? model, req, content);
        return response;
    }

    private static AgentTurnResponse EchoTurn(AgentTurnRequest req, ProviderRequestOptions options)
    {
        var text = req.message ?? "";
        var response = new AgentTurnResponse
        {
            session_id = FirstNonEmpty(req.session_id, "agent-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")),
            assistant_message = "我会以智能体工具调用方式处理当前 CAD 请求。",
            done = false,
            usage = EstimateUsage(options, string.IsNullOrWhiteSpace(options.Model) ? "echo-agent" : options.Model, req, "echo"),
        };
        response.trace.Add(new AgentTraceEvent
        {
            title = "理解意图",
            summary = "离线 echo agent 根据关键词选择一个 CAD 工具调用。",
        });

        if (ContainsAny(text, "矩形", "rectangle", "房间", "room"))
        {
            response.tool_calls.Add(new AgentToolCall
            {
                id = "call-" + DateTime.UtcNow.ToString("HHmmssfff"),
                name = "cad.draw_rectangle",
                args = new JsonObject
                {
                    ["layer"] = "A-WALL",
                    ["x"] = 0,
                    ["y"] = 0,
                    ["width"] = 6000,
                    ["height"] = 4000,
                    ["color"] = 7,
                },
            });
        }
        else if (ContainsAny(text, "看", "读取", "图纸", "snapshot", "inspect"))
        {
            response.tool_calls.Add(new AgentToolCall
            {
                id = "call-" + DateTime.UtcNow.ToString("HHmmssfff"),
                name = "cad.read_dwg_snapshot",
                args = new JsonObject { ["limit"] = 300 },
            });
        }
        else
        {
            response.assistant_message = "我需要更具体的 CAD 目标，例如图层、位置、尺寸或要修改的对象。";
            response.requires_user_input = true;
            response.done = true;
            response.clarification = new AgentClarification
            {
                question = "你要我在当前 DWG 中执行什么 CAD 操作？",
                options = new List<string> { "读取当前图纸", "画一个 6000x4000 矩形", "取消" },
            };
        }

        return response;
    }

    private static AgentTurnResponse ParseAgentResponse(string content, AgentTurnRequest req)
    {
        var stripped = StripCodeFence(content);
        var node = JsonNode.Parse(stripped)?.AsObject() ??
            throw new InvalidOperationException("Agent model returned non-JSON content.");

        var response = new AgentTurnResponse
        {
            session_id = FirstNonEmpty(node["session_id"]?.GetValue<string>(), req.session_id, "agent-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff")),
            assistant_message = node["assistant_message"]?.GetValue<string>() ?? "",
            requires_user_input = node["requires_user_input"]?.GetValue<bool>() ?? false,
            done = node["done"]?.GetValue<bool>() ?? false,
        };

        if (node["trace"] is JsonArray trace)
        {
            foreach (var item in trace.OfType<JsonObject>())
            {
                response.trace.Add(new AgentTraceEvent
                {
                    title = item["title"]?.GetValue<string>() ?? "步骤",
                    summary = item["summary"]?.GetValue<string>() ?? "",
                });
            }
        }

        if (node["tool_calls"] is JsonArray calls)
        {
            foreach (var item in calls.OfType<JsonObject>())
            {
                var name = item["name"]?.GetValue<string>() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                response.tool_calls.Add(new AgentToolCall
                {
                    id = FirstNonEmpty(item["id"]?.GetValue<string>(), "call-" + Guid.NewGuid().ToString("N")),
                    name = name,
                    args = item["args"] as JsonObject ?? new JsonObject(),
                });
            }
        }

        if (node["clarification"] is JsonObject clarification)
        {
            response.clarification = new AgentClarification
            {
                question = clarification["question"]?.GetValue<string>() ?? "",
                options = (clarification["options"] as JsonArray)?
                    .Select(x => x?.GetValue<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Cast<string>()
                    .ToList() ?? new List<string>(),
            };
        }

        return response;
    }

    private static string AgentSystemPrompt() => """
You are VoiceCAD, an AutoCAD agent.
You run as a tool-calling CAD agent inside a docked AutoCAD panel.

Return JSON only:
{
  "assistant_message": "Natural-language reply for the panel. Never put this text into the drawing.",
  "trace": [{"title":"short step","summary":"brief visible reasoning, no hidden chain-of-thought"}],
  "tool_calls": [{"id":"call-1","name":"cad.read_dwg_snapshot","args":{"limit":300}}],
  "requires_user_input": false,
  "clarification": {"question":"...","options":["..."]},
  "done": false
}

Available CAD tools:
- cad.read_dwg_snapshot { limit }
- cad.create_layer { name, color }
- cad.draw_line { layer, color, x1, y1, x2, y2 }
- cad.draw_rectangle { layer, color, x, y, width, height }
- cad.draw_text { layer, color, x, y, text, height }

Rules:
- Use tool_calls for CAD work. Do not emit AutoLISP, scripts, or command text unless a specific tool supports it.
- Assistant replies, progress messages, status, errors, or explanations must stay in assistant_message, never cad.draw_text.
- Use cad.draw_text only when the user explicitly asks for a drawing label, annotation, title, dimension, or note.
- For CAD tool color args, use an AutoCAD ACI integer from 1 to 255 only. Omit color or use null for ByLayer. Do not send strings like "By Layer".
- If a previous tool_result failed because of invalid args, correct the args and retry the tool instead of asking the initial intent question again.
- If information is missing, ask a clarification in the panel.
- Prefer observe -> small action -> observe loops.
- Safety matters: avoid destructive or global edits unless the user clearly asks.
""";

    private static string BuildAgentUserPrompt(AgentTurnRequest req)
    {
        var sb = new StringBuilder();
        sb.AppendLine("User message:");
        sb.AppendLine((req.message ?? "").Trim());

        if (req.cad_observation != null)
        {
            sb.AppendLine();
            sb.AppendLine("Current CAD observation:");
            sb.AppendLine(Truncate(req.cad_observation.ToJsonString(), 32000));
        }

        if (req.attachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Attachment context:");
            foreach (var attachment in req.attachments)
            {
                sb.Append("- ").Append(attachment.name)
                    .Append(", kind=").Append(attachment.kind)
                    .Append(", mime=").Append(attachment.mime_type)
                    .Append(", bytes=").Append(attachment.size_bytes)
                    .AppendLine();
                if (!string.IsNullOrWhiteSpace(attachment.note)) sb.AppendLine("  note: " + attachment.note);
                if (!string.IsNullOrWhiteSpace(attachment.text_excerpt)) sb.AppendLine(Truncate(attachment.text_excerpt, 16000));
            }
        }

        if (req.tool_results is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Previous tool results:");
            foreach (var result in req.tool_results)
            {
                sb.AppendLine(JsonSerializer.Serialize(result));
            }
            if (req.tool_results.Any(x => !x.success))
            {
                sb.AppendLine();
                sb.AppendLine("Important: at least one previous tool call failed. Diagnose the tool error, correct the arguments, and continue the same user task. Do not restart with the generic initial intent clarification unless the missing information cannot be inferred from the original user message, CAD observation, and tool error.");
            }
        }

        return sb.ToString();
    }

    private static ProviderUsage ExtractOpenAiUsage(JsonNode? parsed, ProviderRequestOptions options, string model, AgentTurnRequest req, string content)
    {
        var usage = parsed?["usage"];
        if (usage == null) return EstimateUsage(options, model, req, content);
        var input = usage["prompt_tokens"]?.GetValue<int>() ?? 0;
        var output = usage["completion_tokens"]?.GetValue<int>() ?? 0;
        var total = usage["total_tokens"]?.GetValue<int>() ?? input + output;
        return new ProviderUsage { Provider = options.Name, Model = model, InputTokens = input, OutputTokens = output, TotalTokens = total, Source = "provider" };
    }

    private static ProviderUsage ExtractAnthropicUsage(JsonNode? parsed, ProviderRequestOptions options, string model, AgentTurnRequest req, string content)
    {
        var usage = parsed?["usage"];
        if (usage == null) return EstimateUsage(options, model, req, content);
        var input = usage["input_tokens"]?.GetValue<int>() ?? 0;
        var output = usage["output_tokens"]?.GetValue<int>() ?? 0;
        return new ProviderUsage { Provider = options.Name, Model = model, InputTokens = input, OutputTokens = output, TotalTokens = input + output, Source = "provider" };
    }

    private static ProviderUsage EstimateUsage(ProviderRequestOptions options, string model, AgentTurnRequest req, string content)
    {
        var input = EstimateTokens(BuildAgentUserPrompt(req).Length + AgentSystemPrompt().Length);
        var output = EstimateTokens(content?.Length ?? 0);
        return new ProviderUsage { Provider = options.Name, Model = model, InputTokens = input, OutputTokens = output, TotalTokens = input + output, Source = "estimated" };
    }

    private static string BuildChatCompletionsUrl(string baseUrl, bool isDeepSeek)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)) return trimmed;
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)) return trimmed + "/chat/completions";
        return trimmed + (isDeepSeek ? "/chat/completions" : "/v1/chat/completions");
    }

    private static string StripCodeFence(string text)
    {
        var stripped = (text ?? "").Trim();
        if (!stripped.StartsWith("```", StringComparison.Ordinal)) return stripped;
        var nl = stripped.IndexOf('\n');
        if (nl > 0) stripped = stripped.Substring(nl + 1);
        if (stripped.EndsWith("```", StringComparison.Ordinal)) stripped = stripped.Substring(0, stripped.Length - 3);
        return stripped.Trim();
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        var lower = (haystack ?? "").ToLowerInvariant();
        return needles.Any(n => lower.Contains(n.ToLowerInvariant()));
    }

    private static int EstimateTokens(int chars) => chars <= 0 ? 0 : Math.Max(1, (int)Math.Ceiling(chars / 4.0));

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static string Truncate(string text, int max) =>
        string.IsNullOrEmpty(text) || text.Length <= max ? text ?? "" : text.Substring(0, max) + "\n...[truncated]";
}

public sealed class ProviderRequestException : Exception
{
    public ProviderRequestException(string provider, int statusCode, string responseBody)
        : base(provider + " upstream error " + statusCode + ": " + responseBody)
    {
        Provider = provider;
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public string Provider { get; }
    public int StatusCode { get; }
    public string ResponseBody { get; }
}
