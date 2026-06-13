namespace Vcad.AgentLite;

public static class AgentReferences
{
    public static string Build(AgentTurnRequest req)
    {
        var text = (req.message ?? "").ToLowerInvariant();
        var parts = new List<string>
        {
            DwgSelectors,
        };

        if (ContainsAny(text, "draw", "copy", "move", "offset", "rectangle", "circle", "polyline", "line", "label",
                "annotation", "modify", "create", "生成", "绘制", "画", "复制", "移动", "偏移", "标注", "修改"))
        {
            parts.Add(WriteAndValidationLoop);
        }

        if (req.attachments is { Count: > 0 } ||
            ContainsAny(text, "pdf", "image", "photo", "scan", "report", "鉴定", "报告", "图片", "照片", "扫描"))
        {
            parts.Add(AttachmentContext);
        }

        if (ContainsAny(text, "web", "search", "standard", "code", "规范", "标准", "搜索", "网页", "厂家", "资料"))
        {
            parts.Add(WebContext);
        }

        if (ContainsAny(text, "file", "workspace", "save", "write", "read file", "文件", "保存", "读取"))
        {
            parts.Add(WorkspaceContext);
        }

        return string.Join("\n\n", parts);
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (!string.IsNullOrWhiteSpace(needle) &&
                text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }
        return false;
    }

    private const string DwgSelectors = """
Reference: DWG selectors
- Prefer stable selectors when referring to existing geometry: layer:FROG, handle:1A2F, type:Polyline, block:Door#3/entity:Line#2.
- Use cad.count_entities, cad.measure_bounds, and cad.measure_distance before choosing ambiguous targets.
- include_exploded=true means block internals are visible for understanding and measurement; write tools still act through explicit adapter calls.
""";

    private const string WriteAndValidationLoop = """
Reference: CAD write loop
- For write tasks, observe first when existing geometry matters, then prepare CAD-IR, preview with cad.preview_plan, execute one or more CAD tools, and validate.
- For validation, prefer cad.before_after_diff when a previous snapshot is available, cad.layer_diff for layer count changes, cad.measure_bounds for dimensions, and cad.validate_dwg_state for explicit success criteria.
- If a tool fails, repair the exact arguments and continue the same task. Do not fall back to the initial generic intent options.
""";

    private const string AttachmentContext = """
Reference: attachments
- Use provided PDF text excerpts, image metadata/base64, and file excerpts as task context.
- If a PDF is scanned or incomplete, report the OCR limitation in the panel and ask only for the missing dimensions or permission to OCR/retry.
- Never draw PDF-reading failures or assistant explanations into the DWG.
""";

    private const string WebContext = """
Reference: web context
- Use web.search for external facts, current product/spec information, standards, or manufacturer data.
- Use web.fetch_url for a specific URL. Summarize only relevant facts into the CAD brief.
""";

    private const string WorkspaceContext = """
Reference: workspace files
- Use workspace.read_file for configured workspace files.
- Use workspace.write_file only when the user asks to create/update a file or the execution mode authorizes it.
""";
}
