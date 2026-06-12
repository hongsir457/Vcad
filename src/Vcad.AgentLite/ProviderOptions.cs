namespace Vcad.AgentLite;

public sealed class AgentAttachment
{
    public string? id { get; set; }
    public string name { get; set; } = "";
    public string kind { get; set; } = "binary";
    public string mime_type { get; set; } = "application/octet-stream";
    public long size_bytes { get; set; }
    public int? page_count { get; set; }
    public string? sha256 { get; set; }
    public string? text_excerpt { get; set; }
    public string? data_base64 { get; set; }
    public string? note { get; set; }
}

public sealed class ProviderConfig
{
    public string? name { get; set; }
    public string? base_url { get; set; }
    public string? model { get; set; }
    public string? api_key { get; set; }
    public bool? strict_json { get; set; }
}

public sealed class ProviderUsage
{
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int TotalTokens { get; set; }
    public string Source { get; set; } = "";
}

internal sealed class ProviderRequestOptions
{
    public string Name { get; init; } = "echo";
    public string? BaseUrl { get; init; }
    public string? Model { get; init; }
    public string? ApiKey { get; init; }
    public bool StrictJson { get; init; } = true;

    public static ProviderRequestOptions From(ProviderConfig? cfg)
    {
        return new ProviderRequestOptions
        {
            Name = FirstNonEmpty(cfg?.name, AgentEnv.Provider, "echo")!,
            BaseUrl = FirstNonEmpty(cfg?.base_url, AgentEnv.BaseUrl),
            Model = FirstNonEmpty(cfg?.model, AgentEnv.Model),
            ApiKey = FirstNonEmpty(cfg?.api_key, AgentEnv.ApiKey),
            StrictJson = cfg?.strict_json ?? true,
        };
    }

    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }
        return null;
    }
}
