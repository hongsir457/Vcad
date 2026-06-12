using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

/// <summary>
/// Deterministic, offline fallback. Produces a minimal but valid VCAD DSL
/// based on simple keyword heuristics. Used for self-tests and when no
/// real provider is configured.
/// </summary>
public class EchoProvider : IProvider
{
    public Task<ProviderParseResult> ParseAsync(ParseRequest req)
    {
        var dsl = new JsonObject
        {
            ["version"] = "vcad_dsl_v1",
            ["unit"] = "mm",
            ["coordinate_system"] = "WCS",
        };

        var commands = new JsonArray();

        var text = (req.text ?? "").Trim();
        // very small heuristic: pick a rectangle if user says "rectangle/矩形/房间/room"
        if (ContainsAny(text, "rectangle", "矩形", "room", "房间", "rect"))
        {
            commands.Add(new JsonObject
            {
                ["type"] = "create_layer",
                ["id"] = "LAYER-A-WALL",
                ["name"] = "A-WALL",
                ["color"] = 7,
            });
            commands.Add(new JsonObject
            {
                ["type"] = "draw_rectangle",
                ["id"] = "RECT-001",
                ["origin"] = new JsonArray(0, 0),
                ["width"] = 6000,
                ["height"] = 4000,
                ["rotation"] = 0,
                ["layer"] = "A-WALL",
            });
        }
        else
        {
            // default: text label
            commands.Add(new JsonObject
            {
                ["type"] = "create_layer",
                ["id"] = "LAYER-T-TEXT",
                ["name"] = "T-TEXT",
                ["color"] = 2,
            });
            commands.Add(new JsonObject
            {
                ["type"] = "draw_text",
                ["id"] = "TEXT-001",
                ["text"] = string.IsNullOrEmpty(text) ? "VCAD" : text,
                ["position"] = new JsonArray(0, 0),
                ["height"] = 250,
                ["rotation"] = 0,
                ["alignment"] = "left",
                ["text_style"] = "STANDARD",
                ["layer"] = "T-TEXT",
            });
        }

        dsl["commands"] = commands;
        var options = ProviderRequestOptions.From(req);
        return Task.FromResult(ProviderResultFactory.FromModelJson(
            dsl,
            options,
            string.IsNullOrWhiteSpace(options.Model) ? "echo" : options.Model,
            null,
            req));
    }

    private static bool ContainsAny(string haystack, params string[] needles)
    {
        if (string.IsNullOrEmpty(haystack)) return false;
        var lower = haystack.ToLowerInvariant();
        foreach (var n in needles)
        {
            if (lower.Contains(n.ToLowerInvariant())) return true;
        }
        return false;
    }
}
