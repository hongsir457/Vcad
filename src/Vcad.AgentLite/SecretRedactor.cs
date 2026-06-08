using System.Text.RegularExpressions;

namespace Vcad.AgentLite;

public static class SecretRedactor
{
    private static readonly Regex Bearer = new(@"(?i)Bearer\s+[A-Za-z0-9\-_\.]+", RegexOptions.Compiled);
    private static readonly Regex KeyEq = new(@"(?i)(api[_-]?key|secret|token)\s*[=:]\s*[^\s,;""']+", RegexOptions.Compiled);
    private static readonly Regex OpenAiKey = new(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled);
    private static readonly Regex AnthropicKey = new(@"sk-ant-[A-Za-z0-9\-]{20,}", RegexOptions.Compiled);

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? "";
        input = Bearer.Replace(input, "Bearer ***");
        input = KeyEq.Replace(input, "$1=***");
        input = OpenAiKey.Replace(input, "sk-***");
        input = AnthropicKey.Replace(input, "sk-ant-***");
        return input;
    }
}
