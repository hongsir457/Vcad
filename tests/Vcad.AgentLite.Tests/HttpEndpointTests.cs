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
        Assert.Contains("vcad_dsl_v1", body);
    }

    [Fact]
    public async Task Parse_returns_valid_dsl_for_simple_text()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            request_id = "req-test-1",
            text = "draw a rectangle 6m x 4m",
            context = new { unit = "mm", coordinate_system = "WCS" },
            options = new { max_commands = 10 },
        };
        var resp = await client.PostAsJsonAsync("/parse", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadAsStringAsync();
        var jo = JsonNode.Parse(body)!.AsObject();
        Assert.True(jo["success"]!.GetValue<bool>());
        Assert.NotNull(jo["dsl"]);
        Assert.Equal("vcad_dsl_v1", jo["dsl"]!["version"]!.GetValue<string>());
        Assert.NotNull(jo["usage"]);
        Assert.True(jo["usage"]!["totalTokens"]!.GetValue<int>() > 0);
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
    public async Task Parse_rejects_empty_text()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/parse", new { text = "" });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Parse_rejects_text_over_32000_chars()
    {
        var client = _factory.CreateClient();
        var huge = new string('x', 32001);
        var resp = await client.PostAsJsonAsync("/parse", new { text = huge });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Parse_accepts_attachment_metadata()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            request_id = "req-attachment-1",
            text = "draw from the attached appraisal report",
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
        var resp = await client.PostAsJsonAsync("/parse", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Parse_accepts_cad_state_snapshot()
    {
        var client = _factory.CreateClient();
        var req = new
        {
            request_id = "req-cad-state-1",
            text = "label the open drawing",
            cad_state = new
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
        var resp = await client.PostAsJsonAsync("/parse", req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task Parse_rejects_oversize_body()
    {
        var client = _factory.CreateClient();
        var huge = new string('x', 9 * 1024 * 1024); // > 8 MB
        var content = new StringContent(@"{ ""text"": """ + huge + @""" }",
            Encoding.UTF8, "application/json");
        var resp = await client.PostAsync("/parse", content);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resp.StatusCode);
    }
}
