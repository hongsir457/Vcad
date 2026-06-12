using System.Text.Json.Nodes;

namespace Vcad.AgentLite;

public sealed class AgentTurnRequest
{
    public string? session_id { get; set; }
    public string message { get; set; } = "";
    public JsonObject? cad_observation { get; set; }
    public List<AgentToolResult> tool_results { get; set; } = new();
    public List<AgentAttachment> attachments { get; set; } = new();
    public ProviderConfig? provider { get; set; }
}

public sealed class AgentTurnResponse
{
    public string session_id { get; set; } = "";
    public string assistant_message { get; set; } = "";
    public List<AgentTraceEvent> trace { get; set; } = new();
    public List<AgentToolCall> tool_calls { get; set; } = new();
    public bool requires_user_input { get; set; }
    public AgentClarification? clarification { get; set; }
    public bool done { get; set; }
    public ProviderUsage usage { get; set; } = new();
}

public sealed class AgentToolCall
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public JsonObject args { get; set; } = new();
}

public sealed class AgentToolResult
{
    public string id { get; set; } = "";
    public string name { get; set; } = "";
    public bool success { get; set; }
    public JsonNode? result { get; set; }
    public string? error { get; set; }
}

public sealed class AgentTraceEvent
{
    public string title { get; set; } = "";
    public string summary { get; set; } = "";
}

public sealed class AgentClarification
{
    public string question { get; set; } = "";
    public List<string> options { get; set; } = new();
}
