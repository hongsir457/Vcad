using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class OllamaProvider : IProvider
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(300) };

    public async Task<ProviderParseResult> ParseAsync(ParseRequest req)
    {
        var options = ProviderRequestOptions.From(req);
        var baseUrl = string.IsNullOrEmpty(options.BaseUrl) ? "http://localhost:11434" : options.BaseUrl;
        var model = string.IsNullOrEmpty(options.Model) ? "llama3.2" : options.Model;

        object payload = options.StrictJson
            ? new
            {
                model = model,
                stream = false,
                format = "json",
                messages = Messages(req),
            }
            : new
            {
                model = model,
                stream = false,
                messages = Messages(req),
            };

        using var http = new HttpRequestMessage(HttpMethod.Post, baseUrl.TrimEnd('/') + "/api/chat");
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _client.SendAsync(http);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Ollama error " + (int)resp.StatusCode + ": " + SecretRedactor.Redact(body));
        }

        var parsed = JsonNode.Parse(body);
        var content = parsed?["message"]?["content"]?.GetValue<string>();
        if (string.IsNullOrEmpty(content))
        {
            throw new InvalidOperationException("Ollama returned empty content.");
        }

        var dsl = JsonNode.Parse(StripCodeFence(content));
        if (dsl == null)
        {
            throw new InvalidOperationException("Ollama returned non-JSON content.");
        }
        return ProviderResultFactory.FromModelJson(
            dsl,
            options,
            model,
            ExtractUsage(parsed, options, model),
            req);
    }

    private static ProviderUsage? ExtractUsage(JsonNode? parsed, ProviderRequestOptions options, string model)
    {
        var input = parsed?["prompt_eval_count"]?.GetValue<int>() ?? 0;
        var output = parsed?["eval_count"]?.GetValue<int>() ?? 0;
        if (input <= 0 && output <= 0) return null;
        return new ProviderUsage
        {
            Provider = options.Name,
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output,
            Source = "provider",
        };
    }

    private static object[] Messages(ParseRequest req)
    {
        var text = AttachmentPromptBuilder.BuildUserPrompt(req);
        var images = AttachmentPromptBuilder.ImageBase64Payloads(req);
        object userMessage = images.Length == 0
            ? new { role = "user", content = text }
            : new { role = "user", content = text, images = images };

        return new object[]
        {
            new { role = "system", content = PromptLibrary.SystemPrompt() },
            userMessage,
        };
    }

    private static string StripCodeFence(string text)
    {
        var stripped = text.Trim();
        if (!stripped.StartsWith("```", StringComparison.Ordinal)) return stripped;
        var nl = stripped.IndexOf('\n');
        if (nl > 0) stripped = stripped.Substring(nl + 1);
        if (stripped.EndsWith("```", StringComparison.Ordinal))
        {
            stripped = stripped.Substring(0, stripped.Length - 3);
        }
        return stripped.Trim();
    }
}
