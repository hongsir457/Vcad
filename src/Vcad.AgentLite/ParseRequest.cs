namespace Vcad.AgentLite;

public class ParseRequest
{
    public string? request_id { get; set; }
    public string text { get; set; } = "";
    public ParseContext? context { get; set; }
    public ParseOptions? options { get; set; }
    public ProviderConfig? provider { get; set; }
}

public class ParseContext
{
    public string unit { get; set; } = "mm";
    public string coordinate_system { get; set; } = "WCS";
}

public class ParseOptions
{
    public int max_commands { get; set; } = 50;
    public bool return_explanation { get; set; } = false;
}

public class ProviderConfig
{
    public string? name { get; set; }
    public string? base_url { get; set; }
    public string? model { get; set; }
    public string? api_key { get; set; }
    public bool? strict_json { get; set; }
}
