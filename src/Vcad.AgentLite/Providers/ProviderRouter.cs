using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class ProviderRouter
{
    private readonly EchoProvider _echo = new();
    private readonly OpenAiProvider _openai = new();
    private readonly AnthropicProvider _anthropic = new();

    public Task<JsonNode> ParseAsync(ParseRequest req)
    {
        var provider = (AgentEnv.Provider ?? "echo").ToLowerInvariant();
        return provider switch
        {
            "openai" => _openai.ParseAsync(req),
            "anthropic" => _anthropic.ParseAsync(req),
            "echo" or _ => _echo.ParseAsync(req),
        };
    }
}
