using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Vcad.AgentLite;

public static class AgentToolRunner
{
    private const int MaxTextChars = 32000;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<JsonObject> RunAsync(string name, JsonObject? args)
    {
        args ??= new JsonObject();
        return name switch
        {
            "web.fetch_url" => await FetchUrlAsync(args),
            "web.search" => await SearchAsync(args),
            "workspace.read_file" => ReadWorkspaceFile(args),
            "workspace.write_file" => WriteWorkspaceFile(args),
            _ => Error("UNKNOWN_TOOL", "Tool is not registered: " + name),
        };
    }

    private static async Task<JsonObject> FetchUrlAsync(JsonObject args)
    {
        var url = args["url"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(url)) return Error("SCHEMA_INVALID", "'url' is required.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return Error("URL_REJECTED", "Only http/https URLs are allowed.");
        }

        using var resp = await Http.GetAsync(uri);
        var body = await resp.Content.ReadAsStringAsync();
        return new JsonObject
        {
            ["success"] = resp.IsSuccessStatusCode,
            ["status_code"] = (int)resp.StatusCode,
            ["url"] = uri.ToString(),
            ["text"] = Truncate(CleanText(body)),
        };
    }

    private static async Task<JsonObject> SearchAsync(JsonObject args)
    {
        var query = args["query"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(query)) return Error("SCHEMA_INVALID", "'query' is required.");
        var url = "https://duckduckgo.com/html/?q=" + Uri.EscapeDataString(query);
        var fetched = await FetchUrlAsync(new JsonObject { ["url"] = url });
        fetched["query"] = query;
        fetched["note"] = "Search is best-effort HTML fetch; production deployments should configure a first-party search API.";
        return fetched;
    }

    private static JsonObject ReadWorkspaceFile(JsonObject args)
    {
        var path = args["path"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path)) return Error("SCHEMA_INVALID", "'path' is required.");
        var resolved = ResolveWorkspacePath(path);
        if (resolved.Error != null) return resolved.Error;
        if (!File.Exists(resolved.Path)) return Error("NOT_FOUND", "File does not exist.");
        return new JsonObject
        {
            ["success"] = true,
            ["path"] = resolved.Path,
            ["text"] = Truncate(File.ReadAllText(resolved.Path, Encoding.UTF8)),
        };
    }

    private static JsonObject WriteWorkspaceFile(JsonObject args)
    {
        var path = args["path"]?.GetValue<string>();
        var content = args["content"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(path)) return Error("SCHEMA_INVALID", "'path' is required.");
        if (content == null) return Error("SCHEMA_INVALID", "'content' is required.");
        if (content.Length > MaxTextChars) return Error("TOO_LARGE", "content exceeds 32000 characters.");

        var resolved = ResolveWorkspacePath(path);
        if (resolved.Error != null) return resolved.Error;
        var dir = Path.GetDirectoryName(resolved.Path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(resolved.Path, content, Encoding.UTF8);
        return new JsonObject
        {
            ["success"] = true,
            ["path"] = resolved.Path,
            ["bytes"] = Encoding.UTF8.GetByteCount(content),
        };
    }

    private static (string Path, JsonObject? Error) ResolveWorkspacePath(string path)
    {
        var root = Environment.GetEnvironmentVariable("VCAD_WORKSPACE_ROOT");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VCAD", "workspace");
        }
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, path));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            return ("", Error("PATH_REJECTED", "Path is outside VCAD_WORKSPACE_ROOT."));
        }
        return (fullPath, null);
    }

    private static JsonObject Error(string code, string message) =>
        new()
        {
            ["success"] = false,
            ["error_code"] = code,
            ["message"] = message,
        };

    private static string Truncate(string text)
    {
        text ??= "";
        return text.Length <= MaxTextChars ? text : text.Substring(0, MaxTextChars) + "\n...[truncated]";
    }

    private static string CleanText(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "\\s+", " ");
        return text.Trim();
    }
}
