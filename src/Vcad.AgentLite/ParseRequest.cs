namespace Vcad.AgentLite;

public class ParseRequest
{
    public string? request_id { get; set; }
    public string text { get; set; } = "";
    public ParseContext? context { get; set; }
    public ParseOptions? options { get; set; }
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
