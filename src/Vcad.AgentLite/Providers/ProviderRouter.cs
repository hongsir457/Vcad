using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class ProviderRouter
{
    private readonly EchoProvider _echo = new();
    private readonly OpenAiProvider _openai = new();
    private readonly AnthropicProvider _anthropic = new();
    private readonly GeminiProvider _gemini = new();
    private readonly OllamaProvider _ollama = new();

    public Task<JsonNode> ParseAsync(ParseRequest req)
    {
        var provider = ProviderRequestOptions.From(req).Name.ToLowerInvariant();
        return provider switch
        {
            "openai" => _openai.ParseAsync(req),
            "custom" => _openai.ParseAsync(req),
            "deepseek" => _openai.ParseAsync(req),
            "anthropic" => _anthropic.ParseAsync(req),
            "gemini" => _gemini.ParseAsync(req),
            "ollama" => _ollama.ParseAsync(req),
            "echo" or _ => _echo.ParseAsync(req),
        };
    }
}
