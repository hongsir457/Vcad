using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Vcad.AgentLite;
using Xunit;

namespace Vcad.AgentLite.Tests;

[Collection("agent-env")]
public class HttpEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HttpEndpointTests(WebApplicationFactory<Program> factory)
    {
        Environment.SetEnvironmentVariable("VCAD_AGENT_TOKEN", null);
        Environment.SetEnvironmentVariable("VCAD_AGENT_PROVIDER", "echo");
        _factory = factory;
    }

    [Fact]
    public async Task Health_returns_ok()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("vcad-agent-lite", body);
        Assert.Contains("vcad_agent_turn_v1", body);
    }

    [Fact]
    public async Task Agent_turn_returns_tool_call_for_simple_text()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            session_id = "req-test-1",
            message = "draw a rectangle 6m x 4m",
        };
        var resp = await client.PostAsJsonAsync("/agent/turn", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        var jo = JsonNode.Parse(body)!.AsObject();
        Assert.True(jo["success"]!.GetValue<bool>());
        var response = jo["response"]!.AsObject();
        Assert.NotNull(response["tool_calls"]);
        Assert.Contains(response["tool_calls"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "cad.preview_plan");
        Assert.Contains(response["tool_calls"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "cad.draw_rectangle");
        Assert.Equal("active AutoCAD DWG", response["cad_brief"]!["primary_artifact"]!.GetValue<string>());
        Assert.NotNull(response["task_plan"]);
        Assert.NotNull(response["cad_ir"]);
        Assert.NotNull(response["safety"]);
        Assert.NotNull(response["validation"]);
        Assert.NotNull(response["usage"]);
        Assert.True(response["usage"]!["totalTokens"]!.GetValue<int>() > 0);
    }

    [Fact]
    public void Agent_response_compiles_cad_ir_to_tool_calls()
    {
        var req = new AgentTurnRequest
        {
            session_id = "cad-ir-compile-test",
            message = "draw a 6000 x 4000 rectangle on E2E-TEST",
        };
        var modelContent = """
        {
          "assistant_message": "我会在当前 DWG 中绘制矩形。",
          "cad_brief": {
            "task_type": "new_geometry",
            "objective": "draw a 6000 x 4000 rectangle",
            "primary_artifact": "active AutoCAD DWG",
            "units": "mm",
            "assumptions": [],
            "validation_targets": ["E2E-TEST layer exists", "rectangle exists"]
          },
          "task_plan": {
            "steps": ["observe DWG", "prepare CAD-IR", "execute via tools"],
            "next_step": "execute cad.draw_rectangle"
          },
          "cad_ir": {
            "operations": [
              {
                "action": "create_layer",
                "target_layer": "E2E-TEST",
                "parameters": { "color": 3 }
              },
              {
                "action": "draw_rectangle",
                "target_layer": "E2E-TEST",
                "parameters": { "x": 0, "y": 0, "width": 6000, "height": 4000, "color": 3 }
              }
            ],
            "expected_effect": {
              "layers": ["E2E-TEST"],
              "object_types": ["Polyline"]
            }
          },
          "safety": {
            "risk_level": "low",
            "writes_dwg": true,
            "destructive": false,
            "requires_confirmation": false,
            "reason": "adds new geometry only"
          },
          "validation": {
            "planned_checks": ["cad.validate_dwg_state"],
            "success_criteria": ["rectangle is created"]
          },
          "trace": [],
          "tool_calls": [],
          "requires_user_input": false,
          "clarification": null,
          "done": false
        }
        """;

        var parse = typeof(AgentTurnService).GetMethod(
            "ParseAgentResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var compile = typeof(AgentTurnService).GetMethod(
            "CompileCadIrToToolCalls",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parse);
        Assert.NotNull(compile);

        var response = Assert.IsType<AgentTurnResponse>(parse!.Invoke(null, new object[] { modelContent, req }));
        Assert.Empty(response.tool_calls);

        compile!.Invoke(null, new object[] { response });

        Assert.Contains(response.tool_calls, call => call.name == "cad.preview_plan");
        Assert.Contains(response.tool_calls, call => call.name == "cad.create_layer");
        var draw = Assert.Single(response.tool_calls.Where(call => call.name == "cad.draw_rectangle"));
        Assert.Equal("E2E-TEST", draw.args["layer"]!.GetValue<string>());
        Assert.Equal(6000, draw.args["width"]!.GetValue<int>());
        Assert.Equal(4000, draw.args["height"]!.GetValue<int>());
        Assert.False(response.requires_user_input);
        Assert.Null(response.clarification);
    }

    [Fact]
    public void Agent_response_adds_deterministic_rectangle_tool_plan_when_model_only_talks()
    {
        var req = new AgentTurnRequest
        {
            session_id = "det-rectangle-test",
            message = "draw rectangle 1000x600 at 0,0 layer E2E-TEST",
        };
        var modelContent = """
        {
          "assistant_message": "I will draw the requested rectangle.",
          "cad_brief": null,
          "task_plan": null,
          "cad_ir": null,
          "safety": null,
          "validation": null,
          "trace": [],
          "tool_calls": [],
          "requires_user_input": true,
          "clarification": {
            "question": "Please confirm dimensions.",
            "options": ["1000x600"]
          },
          "done": false
        }
        """;

        var parse = typeof(AgentTurnService).GetMethod(
            "ParseAgentResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var ensure = typeof(AgentTurnService).GetMethod(
            "EnsureDeterministicToolPlan",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parse);
        Assert.NotNull(ensure);

        var response = Assert.IsType<AgentTurnResponse>(parse!.Invoke(null, new object[] { modelContent, req }));
        Assert.Empty(response.tool_calls);

        ensure!.Invoke(null, new object[] { req, response });

        Assert.Contains(response.tool_calls, call => call.name == "cad.preview_plan");
        Assert.Contains(response.tool_calls, call => call.name == "cad.create_layer");
        var draw = Assert.Single(response.tool_calls.Where(call => call.name == "cad.draw_rectangle"));
        Assert.Equal("E2E-TEST", draw.args["layer"]!.GetValue<string>());
        Assert.Equal(1000, draw.args["width"]!.GetValue<double>());
        Assert.Equal(600, draw.args["height"]!.GetValue<double>());
        Assert.False(response.requires_user_input);
        Assert.Null(response.clarification);
    }

    [Fact]
    public void Native_tool_calls_map_to_cad_tool_calls()
    {
        var raw = JsonNode.Parse("""
        [
          {
            "id": "call_native_1",
            "type": "function",
            "function": {
              "name": "cad_draw_circle",
              "arguments": "{\"layer\":\"E2E-CIRCLE\",\"x\":2000,\"y\":0,\"radius\":100}"
            }
          }
        ]
        """)!.AsArray();
        var parse = typeof(AgentTurnService).GetMethod(
            "ParseNativeToolCalls",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(parse);

        var calls = Assert.IsAssignableFrom<IEnumerable<AgentToolCall>>(
            parse!.Invoke(null, new object?[] { raw }))!.ToList();

        var call = Assert.Single(calls);
        Assert.Equal("call_native_1", call.id);
        Assert.Equal("cad.draw_circle", call.name);
        Assert.Equal("E2E-CIRCLE", call.args["layer"]!.GetValue<string>());
        Assert.Equal(2000, call.args["x"]!.GetValue<double>());
        Assert.Equal(100, call.args["radius"]!.GetValue<double>());
    }

    [Fact]
    public void Deterministic_tool_plan_supports_basic_non_rectangle_commands_for_echo_only()
    {
        var create = typeof(AgentTurnService).GetMethod(
            "TryCreateDeterministicToolResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(create);

        var req = new AgentTurnRequest
        {
            session_id = "det-circle-test",
            message = "draw circle radius 100 at 2000,0 layer E2E-CIRCLE",
        };
        var optionsType = typeof(AgentTurnService).Assembly.GetType("Vcad.AgentLite.ProviderRequestOptions");
        Assert.NotNull(optionsType);
        var from = optionsType!.GetMethod(
            "From",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        var options = from!.Invoke(null, new object?[] { new ProviderConfig { name = "echo", model = "echo" } });
        var args = new object?[] { req, options, null };

        var ok = Assert.IsType<bool>(create!.Invoke(null, args));
        Assert.True(ok);

        var response = Assert.IsType<AgentTurnResponse>(args[2]);
        Assert.Contains(response.tool_calls, call => call.name == "cad.draw_circle");
    }

    [Fact]
    public async Task Tools_returns_registered_tool_manifest()
    {
        var client = _factory.CreateClient();
        var resp = await client.GetAsync("/tools");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        var jo = JsonNode.Parse(body)!.AsObject();
        var tools = jo["tools"]!.AsArray();
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "web.fetch_url");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "workspace.read_file");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.preview_plan");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.read_layers");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.read_styles");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.read_blocks");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.query_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.describe_entity");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.describe_selection");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.find_near");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.find_intersections");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.find_connected_contours");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.find_closed_regions");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.measure_relation");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.semantic_scan");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.count_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.measure_bounds");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.measure_distance");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.layer_diff");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.before_after_diff");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.validate_dwg_state");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.draw_wall");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.draw_room");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.draw_stair");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.draw_dimension");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.insert_block");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.move_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.copy_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.rotate_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.scale_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.offset_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.delete_entities");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.change_layer");
        Assert.Contains(tools, t => t!["name"]!.GetValue<string>() == "cad.set_properties");
    }

    [Fact]
    public async Task Benchmark_stair_uses_high_level_cad_tool()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            session_id = "bench-stair-1",
            message = "画一个U型双跑楼梯，宽度一米二，踏步两百五，踢步一百五，层高三米九",
        };
        var resp = await client.PostAsJsonAsync("/agent/turn", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        var jo = JsonNode.Parse(body)!.AsObject();
        var response = jo["response"]!.AsObject();
        Assert.Contains(response["tool_calls"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "cad.draw_stair");
    }

    [Fact]
    public async Task Benchmark_greeting_stays_in_panel_without_cad_text()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            session_id = "bench-hello-1",
            message = "你好",
        };
        var resp = await client.PostAsJsonAsync("/agent/turn", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        var jo = JsonNode.Parse(body)!.AsObject();
        var response = jo["response"]!.AsObject();
        var calls = response["tool_calls"]!.AsArray();
        Assert.DoesNotContain(calls, t => t!["name"]!.GetValue<string>() == "cad.draw_text");
        Assert.True(response["requires_user_input"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Benchmark_inspect_uses_dwg_snapshot_tool()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            session_id = "bench-inspect-1",
            message = "inspect the current drawing",
        };
        var resp = await client.PostAsJsonAsync("/agent/turn", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        var jo = JsonNode.Parse(body)!.AsObject();
        var response = jo["response"]!.AsObject();
        Assert.Contains(response["tool_calls"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "cad.read_dwg_snapshot");
    }

    [Fact]
    public async Task Agent_turn_rejects_empty_message_without_tool_results()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/agent/turn", new { message = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Agent_turn_rejects_message_over_32000_chars()
    {
        var client = _factory.CreateClient();
        var huge = new string('x', 32001);
        var resp = await client.PostAsJsonAsync("/agent/turn", new { message = huge });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Agent_turn_accepts_attachment_metadata()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            session_id = "req-attachment-1",
            message = "draw from the attached appraisal report",
            attachments = new[]
            {
                new
                {
                    id = "att-01",
                    name = "report.pdf",
                    kind = "pdf",
                    mime_type = "application/pdf",
                    size_bytes = 12_000_000,
                    sha256 = "abc123",
                    note = "metadata-only test",
                },
            },
        };
        var resp = await client.PostAsJsonAsync("/agent/turn", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Agent_turn_accepts_cad_observation_snapshot()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            session_id = "req-cad-state-1",
            message = "label the open drawing",
            cad_observation = new
            {
                schema = "cad_drawing_snapshot_v1",
                summary = new
                {
                    entity_count = 3,
                    top_level_entity_count = 2,
                    exploded_entity_count = 1,
                    block_reference_count = 1,
                    layer_count = 2,
                    truncated = false,
                },
            },
        };
        var resp = await client.PostAsJsonAsync("/agent/turn", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Agent_turn_rejects_oversize_body()
    {
        var client = _factory.CreateClient();
        var huge = new string('x', 9 * 1024 * 1024); // > 8 MB
        var content = new StringContent(@"{ ""message"": """ + huge + @""" }",
            Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/agent/turn", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
    }
}
