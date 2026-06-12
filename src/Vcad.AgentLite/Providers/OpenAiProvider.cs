using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class OpenAiProvider : IProvider
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(300) };

    public async Task<JsonNode> ParseAsync(ParseRequest req)
    {
        var options = ProviderRequestOptions.From(req);
        var apiKey = options.ApiKey;
        var isDeepSeek = string.Equals(options.Name, "deepseek", StringComparison.OrdinalIgnoreCase);
        var requiresKey = isDeepSeek || string.Equals(options.Name, "openai", StringComparison.OrdinalIgnoreCase);
        if (requiresKey && string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("VCAD_AGENT_API_KEY is not set.");
        }

        var baseUrl = string.IsNullOrEmpty(options.BaseUrl)
            ? (isDeepSeek ? "https://api.deepseek.com" : "https://api.openai.com")
            : options.BaseUrl;
        var model = string.IsNullOrEmpty(options.Model)
            ? (isDeepSeek ? "deepseek-v4-flash" : "gpt-5")
            : options.Model;

        var systemPrompt = PromptLibrary.SystemPrompt();
        var userContent = AttachmentPromptBuilder.BuildOpenAiUserContent(req, SupportsVision(options, isDeepSeek));

        object payload = options.StrictJson
            ? new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent },
                },
                response_format = new { type = "json_object" },
            }
            : new
            {
                model = model,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userContent },
                },
            };

        using var http = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionsUrl(baseUrl, isDeepSeek));
        if (!string.IsNullOrEmpty(apiKey))
        {
            http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
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

    private static string BuildChatCompletionsUrl(string baseUrl, bool isDeepSeek)
    {
        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed + "/chat/completions";
        }
        return trimmed + (isDeepSeek ? "/chat/completions" : "/v1/chat/completions");
    }

    private static bool SupportsVision(ProviderRequestOptions options, bool isDeepSeek)
    {
        if (isDeepSeek) return false;
        return string.Equals(options.Name, "openai", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(options.Name, "custom", StringComparison.OrdinalIgnoreCase);
    }
}
