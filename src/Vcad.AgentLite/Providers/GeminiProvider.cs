using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public class GeminiProvider : IProvider
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(300) };

    public async Task<ProviderParseResult> ParseAsync(ParseRequest req)
    {
        var options = ProviderRequestOptions.From(req);
        var apiKey = options.ApiKey;
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("VCAD_AGENT_API_KEY is not set.");
        }

        var baseUrl = string.IsNullOrEmpty(options.BaseUrl)
            ? "https://generativelanguage.googleapis.com"
            : options.BaseUrl;
        var model = string.IsNullOrEmpty(options.Model) ? "gemini-3.5-flash" : options.Model;

        var prompt = PromptLibrary.SystemPrompt() + "\n\nUser request:\n" + (req.text ?? "").Trim();
        var parts = AttachmentPromptBuilder.BuildGeminiParts(new ParseRequest
        {
            text = prompt,
            attachments = req.attachments,
        }, includeImages: true);
        object payload = options.StrictJson
            ? new
            {
                contents = new object[]
                {
                    new
                    {
                        role = "user",
                        parts = parts,
                    },
                },
                generationConfig = new { responseMimeType = "application/json" },
            }
            : new
            {
                contents = new object[]
                {
                    new
                    {
                        role = "user",
                        parts = parts,
                    },
                },
            };

        var url = baseUrl.TrimEnd('/') + "/v1beta/models/" +
            Uri.EscapeDataString(model) + ":generateContent?key=" +
            Uri.EscapeDataString(apiKey);
        using var http = new HttpRequestMessage(HttpMethod.Post, url);
        http.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var resp = await _client.SendAsync(http);
        var body = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("Gemini error " + (int)resp.StatusCode + ": " + SecretRedactor.Redact(body));
        }

        var parsed = JsonNode.Parse(body);
        var content = parsed?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>();
        if (string.IsNullOrEmpty(content))
        {
            throw new InvalidOperationException("Gemini returned empty content.");
        }

        var dsl = JsonNode.Parse(StripCodeFence(content));
        if (dsl == null)
        {
            throw new InvalidOperationException("Gemini returned non-JSON content.");
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
        var usage = parsed?["usageMetadata"];
        if (usage == null) return null;
        var input = usage["promptTokenCount"]?.GetValue<int>() ?? 0;
        var output = usage["candidatesTokenCount"]?.GetValue<int>() ?? 0;
        var total = usage["totalTokenCount"]?.GetValue<int>() ?? input + output;
        return new ProviderUsage
        {
            Provider = options.Name,
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = total,
            Source = "provider",
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
