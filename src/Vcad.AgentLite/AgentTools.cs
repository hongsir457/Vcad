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
            Description = "Read drawing metadata, layers, linetypes, text/dim styles, blocks, entities, geometry index, block references, and expanded block internals from the active DWG.",
        },
        new()
        {
            Name = "cad.read_layers",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Read full layer table with color, linetype, lineweight, plot/off/frozen/locked state.",
        },
        new()
        {
            Name = "cad.read_styles",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Read linetype, text style, dimension style, and current database style settings.",
        },
        new()
        {
            Name = "cad.read_blocks",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Read block definitions, block reference summaries, xref-ish metadata, and block entity counts.",
        },
        new()
        {
            Name = "cad.query_entities",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Query entities by selector, layer, type, handle, text, bounds/window, near point, length, and include detailed geometry/properties.",
        },
        new()
        {
            Name = "cad.describe_entity",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Describe a matched entity with geometry, properties, nearby entities, and relation hints.",
        },
        new()
        {
            Name = "cad.describe_selection",
            Category = "cad_context",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Summarize selected entities with counts, bounds, text samples, closed entities, and layer/type breakdown.",
        },
        new()
        {
            Name = "cad.find_near",
            Category = "cad_geometry",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Find entities near a point or selector with distance-ranked results.",
        },
        new()
        {
            Name = "cad.find_intersections",
            Category = "cad_geometry",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Find 2D intersections among line/polyline segments for selected entities.",
        },
        new()
        {
            Name = "cad.find_connected_contours",
            Category = "cad_geometry",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Build connected contour components from line/polyline endpoints and report closed components.",
        },
        new()
        {
            Name = "cad.find_closed_regions",
            Category = "cad_geometry",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Find closed polylines and connected closed contour regions.",
        },
        new()
        {
            Name = "cad.measure_relation",
            Category = "cad_geometry",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Measure relation between two selectors: counts, bounds, center distance, intersection, containment, parallel/perpendicular hints.",
        },
        new()
        {
            Name = "cad.semantic_scan",
            Category = "cad_semantics",
            Effect = "read",
            Status = "available_in_plugin",
            Description = "Heuristically identify wall, room, stair, annotation, door, and window candidates from geometry, layers, text, and blocks.",
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
            Name = "cad.draw_arc",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw an arc by center, radius, and start/end angles.",
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
            Name = "cad.draw_room",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw a room outline with optional inner wall thickness using closed polylines.",
        },
        new()
        {
            Name = "cad.draw_wall",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw a wall as a thick rectangular polyline along a start/end centerline.",
        },
        new()
        {
            Name = "cad.draw_stair",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw a U-shaped double-run stair using width, tread depth, riser height, floor height, and platform depth.",
        },
        new()
        {
            Name = "cad.draw_text",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw an explicit user-requested annotation, label, note, or title.",
        },
        new()
        {
            Name = "cad.draw_mtext",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw explicit multiline annotation text in the active DWG.",
        },
        new()
        {
            Name = "cad.draw_dimension",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Draw an aligned dimension between two points.",
        },
        new()
        {
            Name = "cad.insert_block",
            Category = "cad_action",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Insert an existing block definition by name at a point with rotation and scale.",
        },
        new()
        {
            Name = "cad.move_entities",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Move top-level editable entities selected by selector/layer/type/handle.",
        },
        new()
        {
            Name = "cad.copy_entities",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Copy top-level editable entities selected by selector/layer/type/handle.",
        },
        new()
        {
            Name = "cad.rotate_entities",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Rotate top-level editable entities around a base point.",
        },
        new()
        {
            Name = "cad.scale_entities",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Scale top-level editable entities around a base point.",
        },
        new()
        {
            Name = "cad.offset_entities",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Offset editable curve entities by a distance.",
        },
        new()
        {
            Name = "cad.delete_entities",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Delete top-level editable entities selected by selector/layer/type/handle.",
        },
        new()
        {
            Name = "cad.change_layer",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Move selected editable entities to a target layer.",
        },
        new()
        {
            Name = "cad.set_properties",
            Category = "cad_modify",
            Effect = "write",
            Status = "available_in_plugin",
            Description = "Set selected entity properties such as layer, color, linetype, and lineweight.",
        },
    ];
}
