namespace Vcad.AgentLite;

public sealed class AgentToolDescriptor
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Effect { get; set; } = "read";
    public string Status { get; set; } = "available";
    public string Description { get; set; } = "";
}

public static class AgentTools
{
    public static AgentToolDescriptor[] List() =>
    [
        new()
        {
            Name = "cad.read_dwg_snapshot",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Read layers, entities, block references, and expanded block internals from the active DWG.",
        },
        new()
        {
            Name = "attachment.read_pdf_text",
            Category = "document_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Extract copyable text from PDF attachments. Scanned PDFs require OCR.",
        },
        new()
        {
            Name = "attachment.read_image",
            Category = "vision_context",
            Effect = "read",
            Status = "available_for_vision_models",
            Description = "Send small image attachments to vision-capable providers as multimodal input.",
        },
        new()
        {
            Name = "web.search",
            Category = "web_context",
            Effect = "read",
            Status = "available_guarded",
            Description = "Search the web with query, timeout, and result-length limits.",
        },
        new()
        {
            Name = "web.fetch_url",
            Category = "web_context",
            Effect = "read",
            Status = "available_guarded",
            Description = "Fetch and summarize a specific URL with allow/deny rules and size limits.",
        },
        new()
        {
            Name = "workspace.read_file",
            Category = "file_context",
            Effect = "read",
            Status = "available_guarded",
            Description = "Read files under an explicitly configured workspace root.",
        },
        new()
        {
            Name = "workspace.write_file",
            Category = "file_action",
            Effect = "write",
            Status = "available_guarded_write",
            Description = "Write files under an explicitly configured workspace root after authorization.",
        },
        new()
        {
            Name = "cad.execute_adapter_command",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Execute Safety-approved CAD-IR through the AutoCAD adapter.",
        },
    ];
}
