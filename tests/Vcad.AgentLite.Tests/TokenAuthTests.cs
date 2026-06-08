using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Vcad.AgentLite.Tests;

[Collection("agent-env")]
public class TokenAuthTests
{
    [Fact]
    public async Task When_token_is_set_requests_without_it_are_rejected()
    {
        Environment.SetEnvironmentVariable("VCAD_AGENT_TOKEN", "secret-xyz");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            var client = factory.CreateClient();

            var resp = await client.PostAsJsonAsync("/parse", new { text = "rectangle" });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VCAD_AGENT_TOKEN", null);
        }
    }

    [Fact]
    public async Task When_token_matches_requests_are_accepted()
    {
        Environment.SetEnvironmentVariable("VCAD_AGENT_TOKEN", "secret-xyz");
        try
        {
            using var factory = new WebApplicationFactory<Program>();
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-VCAD-Agent-Token", "secret-xyz");

            var resp = await client.PostAsJsonAsync("/parse", new { text = "rectangle" });
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("VCAD_AGENT_TOKEN", null);
        }
    }
}
