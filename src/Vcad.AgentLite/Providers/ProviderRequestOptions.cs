namespace Vcad.AgentLite.Providers;

internal sealed class ProviderRequestOptions
{
    public string Name { get; init; } = "echo";
    public string? BaseUrl { get; init; }
    public string? Model { get; init; }
    public string? ApiKey { get; init; }
    public bool StrictJson { get; init; } = true;

    public static ProviderRequestOptions From(ParseRequest req)
    {
        var cfg = req.provider;
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
