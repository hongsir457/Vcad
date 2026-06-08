using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class AnthropicProvider : IProvider
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<JsonNode> ParseAsync(ParseRequest req)
    {
        var apiKey = AgentEnv.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("VCAD_AGENT_API_KEY is not set.");
        }

        var baseUrl = string.IsNullOrEmpty(AgentEnv.BaseUrl) ? "https://api.anthropic.com" : AgentEnv.BaseUrl;
        var model = string.IsNullOrEmpty(AgentEnv.Model) ? "claude-3-5-haiku-latest" : AgentEnv.Model;

        var systemPrompt = PromptLibrary.SystemPrompt();
        var userPrompt = req.text;

        var payload = new
        {
            model = model,
            max_tokens = 2048,
            system = systemPrompt,
            messages = new object[]
            {
                new { role = "user", content = userPrompt },
            },
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/messages");
        http.Headers.Add("x-api-key", apiKey);
        http.Headers.Add("anthropic-version", "2023-06-01");
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _client.SendAsync(http);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Anthropic error " + (int)resp.StatusCode + ": " + SecretRedactor.Redact(body));
        }

        var parsed = JsonNode.Parse(body);
        var contentText = parsed?["content"]?[0]?["text"]?.GetValue<string>();
        if (string.IsNullOrEmpty(contentText))
        {
            throw new InvalidOperationException("Anthropic returned empty content.");
        }

        // The model may wrap JSON in code fences; strip them.
        var stripped = contentText.Trim();
        if (stripped.StartsWith("```"))
        {
            var nl = stripped.IndexOf('\n');
            if (nl > 0) stripped = stripped.Substring(nl + 1);
            if (stripped.EndsWith("```"))
                stripped = stripped.Substring(0, stripped.Length - 3);
        }

        var dsl = JsonNode.Parse(stripped);
        if (dsl == null)
        {
            throw new InvalidOperationException("Anthropic returned non-JSON content.");
        }
        return dsl;
    }
}
