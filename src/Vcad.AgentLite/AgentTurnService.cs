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

    public async Task<AgentTurnResponse> RunStreamingAsync(AgentTurnRequest req, Func<string, Task> onDelta)
    {
        var options = ProviderRequestOptions.From(req.provider);
        var provider = string.IsNullOrWhiteSpace(options.Name) ? "echo" : options.Name.ToLowerInvariant();
        if (provider is "openai" or "deepseek" or "custom")
        {
            return await RunOpenAiCompatibleStreamingAsync(req, options, onDelta);
        }

        var response = await RunAsync(req);
        if (!string.IsNullOrWhiteSpace(response.assistant_message) && onDelta != null)
        {
            await onDelta(response.assistant_message);
        }
        return response;
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
        CompileCadIrToToolCalls(response);
        response.usage = ExtractOpenAiUsage(parsed, options, parsed?["model"]?.GetValue<string>() ?? model, req, content);
        return response;
    }

    private static async Task<AgentTurnResponse> RunOpenAiCompatibleStreamingAsync(
        AgentTurnRequest req,
        ProviderRequestOptions options,
        Func<string, Task> onDelta)
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
            stream = true,
            stream_options = new { include_usage = true },
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl(baseUrl, isDeepSeek));
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        using var resp = await Http.SendAsync(http, HttpCompletionOption.ResponseHeadersRead);
        using var stream = await resp.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);
        if (!resp.IsSuccessStatusCode)
        {
            var errorBody = await reader.ReadToEndAsync();
            throw new ProviderRequestException("OpenAI-compatible", (int)resp.StatusCode, SecretRedactor.Redact(errorBody));
        }

        var content = new StringBuilder();
        JsonNode? usageNode = null;
        var responseModel = model;
        var visibleChars = 0;
        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line.Substring(5).Trim();
            if (data == "[DONE]") break;

            JsonNode? chunk;
            try
            {
                chunk = JsonNode.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }

            responseModel = chunk?["model"]?.GetValue<string>() ?? responseModel;
            usageNode = chunk?["usage"] ?? usageNode;
            var choices = chunk?["choices"] as JsonArray;
            if (choices == null || choices.Count == 0)
            {
                continue;
            }

            var delta = choices[0]?["delta"]?["content"]?.GetValue<string>();
            if (string.IsNullOrEmpty(delta)) continue;
            content.Append(delta);
            if (onDelta != null)
            {
                var visible = ExtractPartialJsonString(content.ToString(), "assistant_message");
                if (visible.Length > visibleChars)
                {
                    await onDelta(visible.Substring(visibleChars));
                    visibleChars = visible.Length;
                }
            }
        }

        var raw = content.ToString();
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new InvalidOperationException("Agent model returned empty streamed content.");
        }

        var response = ParseAgentResponse(raw, req);
        CompileCadIrToToolCalls(response);
        if (onDelta != null && visibleChars == 0 && !string.IsNullOrWhiteSpace(response.assistant_message))
        {
            await onDelta(response.assistant_message);
        }
        response.usage = usageNode == null
            ? EstimateUsage(options, responseModel, req, raw)
            : ExtractOpenAiUsageFromNode(usageNode, options, responseModel);
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
        CompileCadIrToToolCalls(response);
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
            cad_brief = new JsonObject
            {
                ["task_type"] = "dwg_action",
                ["objective"] = text,
                ["primary_artifact"] = "active AutoCAD DWG",
                ["units"] = "millimeters unless the drawing context or user says otherwise",
            },
            task_plan = new JsonObject
            {
                ["steps"] = new JsonArray("read current DWG context", "perform the requested CAD operation", "validate the changed DWG state"),
            },
            safety = new JsonObject
            {
                ["mode"] = "non_destructive_additive",
                ["confirmation"] = "plugin policy decides whether write tools require confirmation",
            },
            validation = new JsonObject
            {
                ["planned_checks"] = new JsonArray("cad.preview_plan", "cad.read_dwg_snapshot", "cad.validate_dwg_state", "cad.before_after_diff"),
            },
        };
        response.trace.Add(new AgentTraceEvent
        {
            title = "理解意图",
            summary = "离线 echo agent 根据关键词选择一个 CAD 工具调用。",
        });

        if (ContainsAny(text, "楼梯", "stair", "stairs"))
        {
            response.tool_calls.Add(new AgentToolCall
            {
                id = "call-" + DateTime.UtcNow.ToString("HHmmssfff"),
                name = "cad.draw_stair",
                args = new JsonObject
                {
                    ["layer"] = "STAIR",
                    ["x"] = 0,
                    ["y"] = 0,
                    ["width"] = 1200,
                    ["tread_depth"] = 250,
                    ["riser_height"] = 150,
                    ["floor_height"] = 3900,
                    ["platform_depth"] = 1200,
                    ["color"] = 7,
                },
            });
            response.cad_ir = new JsonObject
            {
                ["intent"] = "draw_stair",
                ["target_layer"] = "STAIR",
                ["expected_bounds"] = new JsonObject { ["width"] = 3600, ["height"] = 4450 },
            };
        }
        else if (ContainsAny(text, "矩形", "rectangle", "房间", "room"))
        {
            response.tool_calls.Add(new AgentToolCall
            {
                id = "call-" + DateTime.UtcNow.ToString("HHmmssfff"),
                name = "cad.preview_plan",
                args = new JsonObject
                {
                    ["writes_dwg"] = true,
                    ["operations"] = new JsonArray(new JsonObject
                    {
                        ["action"] = "draw_rectangle",
                        ["target_layer"] = "A-WALL",
                        ["parameters"] = new JsonObject { ["x"] = 0, ["y"] = 0, ["width"] = 6000, ["height"] = 4000 },
                    }),
                    ["expected_effect"] = new JsonObject
                    {
                        ["layers"] = new JsonArray("A-WALL"),
                        ["object_types"] = new JsonArray("Polyline"),
                    },
                },
            });
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
            response.cad_ir = new JsonObject
            {
                ["intent"] = "draw_rectangle",
                ["target_layer"] = "A-WALL",
                ["expected_bounds"] = new JsonObject { ["width"] = 6000, ["height"] = 4000 },
            };
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
            cad_brief = node["cad_brief"] as JsonObject,
            task_plan = node["task_plan"] as JsonObject,
            cad_ir = node["cad_ir"] as JsonObject,
            safety = node["safety"] as JsonObject,
            validation = node["validation"] as JsonObject,
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

    private static void CompileCadIrToToolCalls(AgentTurnResponse response)
    {
        if (response.tool_calls.Count > 0) return;

        var operations = response.cad_ir?["operations"] as JsonArray;
        if (operations == null || operations.Count == 0) return;

        var compiled = new List<AgentToolCall>();
        var hasWrite = false;
        var hasPreview = false;
        foreach (var item in operations.OfType<JsonObject>())
        {
            var toolName = CadIrActionToToolName(
                FirstNonEmpty(
                    item["tool"]?.GetValue<string>(),
                    item["tool_name"]?.GetValue<string>(),
                    item["name"]?.GetValue<string>(),
                    item["action"]?.GetValue<string>()));
            if (string.IsNullOrWhiteSpace(toolName)) continue;

            var args = BuildToolArgsFromCadIrOperation(item, toolName);
            if (!HasMinimumArgs(toolName, args)) continue;

            hasWrite |= IsCadWriteToolName(toolName);
            hasPreview |= string.Equals(toolName, "cad.preview_plan", StringComparison.OrdinalIgnoreCase);
            compiled.Add(new AgentToolCall
            {
                id = "cad-ir-" + Guid.NewGuid().ToString("N"),
                name = toolName,
                args = args,
            });
        }

        if (compiled.Count == 0) return;

        if (hasWrite && !hasPreview)
        {
            response.tool_calls.Add(new AgentToolCall
            {
                id = "cad-ir-preview-" + Guid.NewGuid().ToString("N"),
                name = "cad.preview_plan",
                args = new JsonObject
                {
                    ["writes_dwg"] = true,
                    ["operations"] = CloneJsonArray(operations),
                    ["expected_effect"] = CloneJsonObject(response.cad_ir?["expected_effect"] as JsonObject) ?? new JsonObject(),
                },
            });
        }

        foreach (var call in compiled)
        {
            response.tool_calls.Add(call);
        }

        response.requires_user_input = false;
        response.clarification = null;
        response.done = false;
        response.trace.Add(new AgentTraceEvent
        {
            title = "CAD-IR 编译",
            summary = "AgentLite 已将模型返回的 CAD-IR 编译为可执行 cad.* tool_calls。",
        });
    }

    private static JsonObject BuildToolArgsFromCadIrOperation(JsonObject operation, string toolName)
    {
        var args = CloneJsonObject(operation["args"] as JsonObject) ??
                   CloneJsonObject(operation["parameters"] as JsonObject) ??
                   new JsonObject();

        foreach (var prop in operation)
        {
            if (string.Equals(prop.Key, "action", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "tool", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "tool_name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "args", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "parameters", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "expected_effect", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(prop.Key, "reason", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(prop.Key, "target_layer", StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value != null)
                {
                    if (string.Equals(toolName, "cad.create_layer", StringComparison.OrdinalIgnoreCase))
                    {
                        if (args["name"] == null)
                        {
                            args["name"] = CloneJsonNode(prop.Value);
                        }
                    }
                    else
                    {
                        if (args["layer"] == null)
                        {
                            args["layer"] = CloneJsonNode(prop.Value);
                        }
                    }
                }
                continue;
            }

            if (args[prop.Key] == null && prop.Value != null && prop.Value is not JsonObject)
            {
                args[prop.Key] = CloneJsonNode(prop.Value);
            }
        }

        if (string.Equals(toolName, "cad.create_layer", StringComparison.OrdinalIgnoreCase) &&
            args["name"] == null &&
            args["layer"] != null)
        {
            args["name"] = CloneJsonNode(args["layer"]!);
        }

        return args;
    }

    private static string CadIrActionToToolName(string action)
    {
        if (string.IsNullOrWhiteSpace(action)) return "";
        var normalized = action.Trim().ToLowerInvariant()
            .Replace("cad.", "")
            .Replace("-", "_")
            .Replace(" ", "_");

        return normalized switch
        {
            "preview" or "preview_plan" => "cad.preview_plan",
            "inspect" or "snapshot" or "read_snapshot" or "read_dwg_snapshot" => "cad.read_dwg_snapshot",
            "read_layers" => "cad.read_layers",
            "read_styles" => "cad.read_styles",
            "read_blocks" => "cad.read_blocks",
            "query" or "query_entities" => "cad.query_entities",
            "describe" or "describe_entity" => "cad.describe_entity",
            "describe_selection" => "cad.describe_selection",
            "find_near" => "cad.find_near",
            "find_intersections" => "cad.find_intersections",
            "find_connected_contours" => "cad.find_connected_contours",
            "find_closed_regions" => "cad.find_closed_regions",
            "measure_relation" => "cad.measure_relation",
            "semantic_scan" => "cad.semantic_scan",
            "count" or "count_entities" => "cad.count_entities",
            "measure_bounds" or "bounds" => "cad.measure_bounds",
            "measure_distance" or "distance" => "cad.measure_distance",
            "layer_diff" => "cad.layer_diff",
            "before_after_diff" => "cad.before_after_diff",
            "validate" or "validate_dwg_state" => "cad.validate_dwg_state",
            "create_layer" or "layer" => "cad.create_layer",
            "draw_line" or "line" => "cad.draw_line",
            "draw_polyline" or "polyline" => "cad.draw_polyline",
            "draw_circle" or "circle" => "cad.draw_circle",
            "draw_arc" or "arc" => "cad.draw_arc",
            "draw_rectangle" or "rectangle" or "rect" => "cad.draw_rectangle",
            "draw_room" or "room" => "cad.draw_room",
            "draw_wall" or "wall" => "cad.draw_wall",
            "draw_stair" or "stair" or "stairs" or "stair_u" or "u_stair" or "double_run_stair" => "cad.draw_stair",
            "draw_text" or "text" => "cad.draw_text",
            "draw_mtext" or "mtext" => "cad.draw_mtext",
            "draw_dimension" or "dimension" or "dim" => "cad.draw_dimension",
            "insert_block" or "block" => "cad.insert_block",
            "move" or "move_entities" => "cad.move_entities",
            "copy" or "copy_entities" => "cad.copy_entities",
            "rotate" or "rotate_entities" => "cad.rotate_entities",
            "scale" or "scale_entities" => "cad.scale_entities",
            "offset" or "offset_entities" => "cad.offset_entities",
            "delete" or "delete_entities" => "cad.delete_entities",
            "change_layer" => "cad.change_layer",
            "set_properties" or "properties" => "cad.set_properties",
            _ => "",
        };
    }

    private static bool HasMinimumArgs(string toolName, JsonObject args)
    {
        bool Has(string key) => args[key] != null;
        return toolName.ToLowerInvariant() switch
        {
            "cad.create_layer" => Has("name"),
            "cad.draw_line" => (Has("x1") && Has("y1") && Has("x2") && Has("y2")) || (Has("start") && Has("end")),
            "cad.draw_polyline" => Has("points"),
            "cad.draw_circle" => Has("radius") && (Has("x") || Has("center")),
            "cad.draw_arc" => Has("radius") && Has("start_angle") && Has("end_angle"),
            "cad.draw_rectangle" => Has("width") && Has("height"),
            "cad.draw_room" => Has("width") && Has("height"),
            "cad.draw_wall" => Has("thickness") || Has("x1") || Has("start"),
            "cad.draw_stair" => Has("width") || Has("tread_depth") || Has("floor_height"),
            "cad.draw_text" => Has("text"),
            "cad.draw_mtext" => Has("text"),
            "cad.draw_dimension" => (Has("x1") && Has("y1") && Has("x2") && Has("y2")) || (Has("start") && Has("end")),
            "cad.insert_block" => Has("name"),
            _ => true,
        };
    }

    private static bool IsCadWriteToolName(string toolName)
    {
        var name = (toolName ?? "").ToLowerInvariant();
        return name is "cad.create_layer" or "cad.draw_line" or "cad.draw_polyline" or "cad.draw_circle" or
            "cad.draw_arc" or "cad.draw_rectangle" or "cad.draw_room" or "cad.draw_wall" or
            "cad.draw_stair" or "cad.draw_text" or "cad.draw_mtext" or "cad.draw_dimension" or
            "cad.insert_block" or "cad.move_entities" or "cad.copy_entities" or "cad.rotate_entities" or
            "cad.scale_entities" or "cad.offset_entities" or "cad.delete_entities" or "cad.change_layer" or
            "cad.set_properties";
    }

    private static JsonNode? CloneJsonNode(JsonNode node) => JsonNode.Parse(node.ToJsonString());

    private static JsonObject? CloneJsonObject(JsonObject? obj) =>
        obj == null ? null : JsonNode.Parse(obj.ToJsonString())?.AsObject();

    private static JsonArray CloneJsonArray(JsonArray arr) =>
        JsonNode.Parse(arr.ToJsonString())?.AsArray() ?? new JsonArray();

    private static string AgentSystemPrompt() => """
You are VoiceCAD, an AutoCAD agent.
You run as a tool-calling CAD agent inside a docked AutoCAD panel.

Return JSON only:
{
  "assistant_message": "Natural-language reply for the panel. Never put this text into the drawing.",
  "cad_brief": {
    "task_type": "new_geometry|modify_geometry|inspect|annotation|file_context|web_context|conversation",
    "objective": "what the user wants in the active DWG",
    "primary_artifact": "active AutoCAD DWG",
    "units": "mm|drawing_units|unknown",
    "assumptions": ["explicit assumptions when proceeding without asking"],
    "validation_targets": ["dimensions, layers, counts, object types, or relationships to check"]
  },
  "task_plan": {
    "steps": ["observe DWG", "prepare CAD-IR", "safety check", "preview/confirm if required", "execute via tools", "validate result"],
    "next_step": "the immediate next action"
  },
  "cad_ir": {
    "operations": [{"action":"draw_rectangle|draw_polyline|draw_circle|draw_line|draw_text|create_layer|inspect|validate","target_layer":"...", "parameters":{}}],
    "expected_effect": {"layers":[], "object_types":[], "bounds":{}}
  },
  "safety": {
    "risk_level": "low|medium|high",
    "writes_dwg": true,
    "destructive": false,
    "requires_confirmation": true,
    "reason": "why this is safe or what needs confirmation"
  },
  "validation": {
    "planned_checks": ["cad.preview_plan", "cad.validate_dwg_state", "cad.measure_bounds", "cad.before_after_diff", "cad.read_dwg_snapshot"],
    "success_criteria": ["what must be true after tool execution"]
  },
  "trace": [{"title":"short step","summary":"brief visible reasoning, no hidden chain-of-thought"}],
  "tool_calls": [{"id":"call-1","name":"cad.read_dwg_snapshot","args":{"limit":300}}],
  "requires_user_input": false,
  "clarification": {"question":"...","options":["..."]},
  "done": false
}

Available CAD tools:
- cad.read_dwg_snapshot { limit }
- cad.read_layers {}
- cad.read_styles {}
- cad.read_blocks {}
- cad.query_entities { selector, selectors, layer, type, handle, text_contains, bounds, window, near, x, y, radius, min_length, max_length, include_exploded, include_geometry, include_properties, limit }
- cad.describe_entity { selector, layer, type, handle, near_radius, include_exploded }
- cad.describe_selection { selector, selectors, layer, type, handle, text_contains, bounds, include_exploded }
- cad.find_near { x, y, point, near_selector, radius, selector, layer, type, include_exploded, limit }
- cad.find_intersections { selector, layer, type, include_exploded, limit }
- cad.find_connected_contours { selector, layer, type, tolerance, include_exploded, limit }
- cad.find_closed_regions { selector, layer, type, tolerance, include_exploded }
- cad.measure_relation { a, b, a_selector, b_selector, include_exploded }
- cad.semantic_scan { selector, layer, type, include_exploded }
- cad.preview_plan { operations, selectors, expected_effect, writes_dwg }
- cad.count_entities { selector, selectors, layer, type, handle, include_exploded }
- cad.measure_bounds { selector, selectors, layer, type, handle, include_exploded }
- cad.measure_distance { from, to, x1, y1, x2, y2, from_selector, to_selector, include_exploded }
- cad.layer_diff { before_snapshot, selector, selectors, layer, type, handle, include_exploded }
- cad.before_after_diff { before_snapshot, after_snapshot, selector, selectors, layer, type, handle, include_exploded }
- cad.validate_dwg_state { selector, selectors, layer, type, handle, include_exploded, expected_layers, expected_min_entities, expected_types, expected_layer_entity_counts, max_warnings }
- cad.create_layer { name, color }
- cad.draw_line { layer, color, x1, y1, x2, y2 }
- cad.draw_polyline { layer, color, points:[[x,y],...], closed, constant_width }
- cad.draw_circle { layer, color, x, y, radius }
- cad.draw_arc { layer, color, x, y, radius, start_angle, end_angle }
- cad.draw_rectangle { layer, color, x, y, width, height }
- cad.draw_room { layer, color, x, y, width, height, wall_thickness }
- cad.draw_wall { layer, color, x1, y1, x2, y2, thickness }
- cad.draw_stair { layer, color, x, y, width, tread_depth, riser_height, floor_height, platform_depth, total_risers }
- cad.draw_text { layer, color, x, y, text, height }
- cad.draw_mtext { layer, color, x, y, text, height, width, rotation }
- cad.draw_dimension { layer, color, x1, y1, x2, y2, dim_line:[x,y], text }
- cad.insert_block { name, layer, color, x, y, rotation, scale }
- cad.move_entities { selector, selectors, layer, type, handle, dx, dy, dz }
- cad.copy_entities { selector, selectors, layer, type, handle, dx, dy, dz }
- cad.rotate_entities { selector, selectors, layer, type, handle, x, y, angle }
- cad.scale_entities { selector, selectors, layer, type, handle, x, y, factor }
- cad.offset_entities { selector, selectors, layer, type, handle, distance }
- cad.delete_entities { selector, selectors, layer, type, handle }
- cad.change_layer { selector, selectors, layer, type, handle, target_layer }
- cad.set_properties { selector, selectors, layer, type, handle, color, linetype, lineweight }

Available context/action tools:
- web.search { query }
- web.fetch_url { url }
- workspace.read_file { path }
- workspace.write_file { path, content }
- Uploaded attachments are already included in Attachment context. PDF text excerpts, image metadata/base64, and text file excerpts should be used directly before asking the user to repeat the content.

Rules:
- Keep the entry point and primary artifact DWG-first: you are inside AutoCAD, and the current active DWG is the source of truth. Do not switch to STEP/build123d workflows unless the user explicitly asks to export/import such files.
- Every CAD task should follow this engineering loop at the right depth: Intent -> CAD brief -> task plan -> CAD-IR -> safety -> preview/confirm policy -> AutoCAD tool adapter -> validation -> result.
- For non-trivial or modification tasks, call cad.read_dwg_snapshot before writing so you understand layers, existing objects, block references, and expanded block internals.
- For existing drawing tasks, prefer targeted observation tools over guessing: cad.semantic_scan for likely walls/rooms/stairs/annotations, cad.query_entities for candidate sets, cad.describe_entity or cad.describe_selection for selected targets, and cad.find_near/find_intersections/find_closed_regions when geometry relationships matter.
- Use stable DWG selectors for existing geometry: layer:FROG, handle:1A2F, type:Polyline, block:Door#3/entity:Line#2. Prefer selectors over vague phrases once a target has been observed.
- For modification tasks, never modify vague targets without first resolving them to selectors/handles/layers and summarizing the selected target set.
- Before writes, use cad.preview_plan when the user has not fully authorized execution or when impact is not obvious.
- If cad_ir.operations contains executable AutoCAD actions with enough parameters, include the matching tool_calls in the same response. Do not stop at a natural-language plan.
- After write tools succeed, prefer a validation read tool in the next turn: cad.before_after_diff if a previous snapshot is available, cad.layer_diff for layer changes, cad.validate_dwg_state for expected layers/counts/types, cad.measure_bounds for dimensions/bounds, cad.count_entities for target counts, or cad.read_dwg_snapshot for general inspection.
- Use tool_calls for CAD work. Do not emit AutoLISP, scripts, or command text unless a specific tool supports it.
- Use web.search/web.fetch_url when the user asks for external facts, standards, product information, or current web context.
- Use workspace.read_file/workspace.write_file when the user asks to inspect or save files under the configured workspace root. Write only when the user asked to create/update a file or the execution mode authorizes it.
- Do not ask generic questions when a safe, reversible, or previewable next step can be inferred. Make a reasonable plan, call read/context tools when useful, and ask only specific missing parameters.
- Prefer cad.draw_polyline for multi-segment contours and outlines instead of many separate cad.draw_line calls.
- Prefer cad.draw_room and cad.draw_wall for architectural room/wall requests when dimensions are available or safe defaults are acceptable.
- Prefer cad.draw_stair for common U-shaped/double-run stair requests instead of only describing a stair plan.
- Assistant replies, progress messages, status, errors, or explanations must stay in assistant_message, never cad.draw_text.
- Reply in the user's language. If the user writes Chinese, assistant_message, trace summaries, and clarification options should be Chinese.
- Use cad.draw_text only when the user explicitly asks for a drawing label, annotation, title, dimension, or note.
- For CAD tool color args, use an AutoCAD ACI integer from 1 to 255 only. Omit color or use null for ByLayer. Do not send strings like "By Layer".
- If a previous tool_result failed because of invalid args, correct the args and retry the tool instead of asking the initial intent question again.
- If information is missing, ask a clarification in the panel.
- After successful CAD execution, do not ask the generic initial intent question again. Ask a specific follow-up about likely adjustments to the current result, such as changing size, moving position, changing layer/color, adding labels, continuing with another object, or finishing.
- Prefer observe -> small action -> observe loops.
- Safety matters: avoid destructive or global edits unless the user clearly asks.
- Do not expose hidden chain-of-thought. The trace/cad_brief/task_plan fields should be short, auditable engineering summaries.
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

        var references = AgentReferences.Build(req);
        if (!string.IsNullOrWhiteSpace(references))
        {
            sb.AppendLine();
            sb.AppendLine("Progressive reference snippets:");
            sb.AppendLine(references);
        }

        return sb.ToString();
    }

    private static ProviderUsage ExtractOpenAiUsage(JsonNode? parsed, ProviderRequestOptions options, string model, AgentTurnRequest req, string content)
    {
        var usage = parsed?["usage"];
        if (usage == null) return EstimateUsage(options, model, req, content);
        return ExtractOpenAiUsageFromNode(usage, options, model);
    }

    private static ProviderUsage ExtractOpenAiUsageFromNode(JsonNode usage, ProviderRequestOptions options, string model)
    {
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

    private static string ExtractPartialJsonString(string json, string propertyName)
    {
        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(propertyName)) return "";
        var marker = "\"" + propertyName + "\"";
        var markerIndex = json.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0) return "";
        var colonIndex = json.IndexOf(':', markerIndex + marker.Length);
        if (colonIndex < 0) return "";

        var i = colonIndex + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
        if (i >= json.Length || json[i] != '"') return "";
        i++;

        var sb = new StringBuilder();
        while (i < json.Length)
        {
            var ch = json[i++];
            if (ch == '"') break;
            if (ch != '\\')
            {
                sb.Append(ch);
                continue;
            }

            if (i >= json.Length) break;
            var esc = json[i++];
            switch (esc)
            {
                case '"':
                    sb.Append('"');
                    break;
                case '\\':
                    sb.Append('\\');
                    break;
                case '/':
                    sb.Append('/');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'u':
                    if (i + 4 > json.Length) return sb.ToString();
                    var hex = json.Substring(i, 4);
                    try
                    {
                        sb.Append((char)Convert.ToInt32(hex, 16));
                    }
                    catch
                    {
                        return sb.ToString();
                    }
                    i += 4;
                    break;
                default:
                    sb.Append(esc);
                    break;
            }
        }

        return sb.ToString();
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
