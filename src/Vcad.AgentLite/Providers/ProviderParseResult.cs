using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public sealed class ProviderParseResult
{
    public JsonNode? Dsl { get; set; }
    public string? AssistantMessage { get; set; }
    public JsonNode? Clarification { get; set; }
    public ProviderUsage Usage { get; set; } = new();
}

public sealed class ProviderUsage
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public string Source { get; set; } = "provider";
}

internal static class ProviderResultFactory
{
    public static ProviderParseResult FromModelJson(
        JsonNode? modelJson,
        ProviderRequestOptions options,
        string model,
        ProviderUsage? usage,
        ParseRequest request)
    {
        var result = new ProviderParseResult
        {
            Usage = EnsureUsage(usage, options, model, request, modelJson),
        };

        if (modelJson is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("assistant_message", out var assistant))
            {
                result.AssistantMessage = assistant?.GetValue<string>();
            }
            if (obj.TryGetPropertyValue("clarification", out var clarification))
            {
                result.Clarification = clarification?.DeepClone();
            }
            if (obj.TryGetPropertyValue("dsl", out var dsl))
            {
                result.Dsl = dsl?.DeepClone();
                return result;
            }
            if (obj.TryGetPropertyValue("version", out var version) &&
                string.Equals(version?.GetValue<string>(), "vcad_dsl_v1", StringComparison.Ordinal))
            {
                result.Dsl = obj.DeepClone();
                return result;
            }
        }

        result.Dsl = modelJson?.DeepClone();
        return result;
    }

    public static ProviderUsage EnsureUsage(
        ProviderUsage? usage,
        ProviderRequestOptions options,
        string model,
        ParseRequest request,
        JsonNode? responseJson)
    {
        usage ??= EstimateUsage(options, model, request, responseJson);
        if (string.IsNullOrWhiteSpace(usage.Provider)) usage.Provider = options.Name;
        if (string.IsNullOrWhiteSpace(usage.Model)) usage.Model = model;
        if (usage.TotalTokens <= 0) usage.TotalTokens = usage.InputTokens + usage.OutputTokens;
        return usage;
    }

    private static ProviderUsage EstimateUsage(
        ProviderRequestOptions options,
        string model,
        ParseRequest request,
        JsonNode? responseJson)
    {
        var inputChars = ((request.text ?? "") + AttachmentPromptBuilder.BuildUserPrompt(request)).Length;
        var outputChars = responseJson?.ToJsonString().Length ?? 0;
        var input = EstimateTokens(inputChars);
        var output = EstimateTokens(outputChars);
        return new ProviderUsage
        {
            Provider = options.Name,
            Model = model,
            InputTokens = input,
            OutputTokens = output,
            TotalTokens = input + output,
            Source = "estimated",
        };
    }

    private static int EstimateTokens(int chars)
    {
        if (chars <= 0) return 0;
        return Math.Max(1, (int)Math.Ceiling(chars / 4.0));
    }
}
