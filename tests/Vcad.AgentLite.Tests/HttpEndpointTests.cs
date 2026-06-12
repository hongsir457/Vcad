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
        Assert.Contains(response["tool_calls"]!.AsArray(), t => t!["name"]!.GetValue<string>() == "cad.draw_rectangle");
        Assert.NotNull(response["usage"]);
        Assert.True(response["usage"]!["totalTokens"]!.GetValue<int>() > 0);
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
