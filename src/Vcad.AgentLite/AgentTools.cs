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
            Name = "cad.preview_plan",
            Category = "cad_preview",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Dry-run a CAD-IR plan against the active DWG context and summarize likely impacted selectors before writes.",
        },
        new()
        {
            Name = "cad.count_entities",
            Category = "cad_validation",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Count DWG entities by selector, layer, type, handle, and expanded block inclusion.",
        },
        new()
        {
            Name = "cad.measure_bounds",
            Category = "cad_validation",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Measure aggregate bounds, width, height, and entity count for matching DWG entities by selector, layer, type, or handle.",
        },
        new()
        {
            Name = "cad.measure_distance",
            Category = "cad_measure",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Measure distance between explicit points or the centers of selected DWG entity groups.",
        },
        new()
        {
            Name = "cad.layer_diff",
            Category = "cad_validation",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Compare layer entity counts between a previous DWG snapshot and the current drawing.",
        },
        new()
        {
            Name = "cad.before_after_diff",
            Category = "cad_validation",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Compare entity counts, layer/type deltas, and bounds between a previous DWG snapshot and the current drawing.",
        },
        new()
        {
            Name = "cad.validate_dwg_state",
            Category = "cad_validation",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Validate expected DWG state such as layers, entity counts, object types, warning count, and layer-specific minimum counts.",
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
            Name = "cad.create_layer",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Create or update a layer in the active AutoCAD drawing.",
        },
        new()
        {
            Name = "cad.draw_line",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw a line in the active AutoCAD drawing.",
        },
        new()
        {
            Name = "cad.draw_polyline",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw an open or closed polyline from a point array in one tool call.",
        },
        new()
        {
            Name = "cad.draw_circle",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw a circle by center point and radius.",
        },
        new()
        {
            Name = "cad.draw_rectangle",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw a rectangle in the active AutoCAD drawing.",
        },
        new()
        {
            Name = "cad.draw_text",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw an explicit user-requested annotation, label, note, or title.",
        },
    ];
}
