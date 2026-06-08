using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class OpenAiProvider : IProvider
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<JsonNode> ParseAsync(ParseRequest req)
    {
        var apiKey = AgentEnv.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("VCAD_AGENT_API_KEY is not set.");
        }

        var baseUrl = string.IsNullOrEmpty(AgentEnv.BaseUrl) ? "https://api.openai.com" : AgentEnv.BaseUrl;
        var model = string.IsNullOrEmpty(AgentEnv.Model) ? "gpt-4o-mini" : AgentEnv.Model;

        var systemPrompt = PromptLibrary.SystemPrompt();
        var userPrompt = req.text;

        var payload = new
        {
            model = model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt },
            },
            response_format = new { type = "json_object" },
        };

        using var http = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/v1/chat/completions");
        http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _client.SendAsync(http);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("OpenAI error " + (int)resp.StatusCode + ": " + SecretRedactor.Redact(body));
        }

        var parsed = JsonNode.Parse(body);
        var content = parsed?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrEmpty(content))
        {
            throw new InvalidOperationException("OpenAI returned empty content.");
        }

        var dsl = JsonNode.Parse(content);
        if (dsl == null)
        {
            throw new InvalidOperationException("OpenAI returned non-JSON content.");
        }
        return dsl;
    }
}
