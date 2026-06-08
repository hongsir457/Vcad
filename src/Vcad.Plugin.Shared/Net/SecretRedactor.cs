using System.Text.RegularExpressions;

namespace Vcad.Plugin.Net
{
    internal static class SecretRedactor
    {
        private static readonly Regex BearerPattern = new Regex(@"(?i)Bearer\s+[A-Za-z0-9\-_\.]+", RegexOptions.Compiled);
        private static readonly Regex AuthHeader = new Regex(@"(?i)Authorization\s*:\s*[^\r\n]+", RegexOptions.Compiled);
        private static readonly Regex KeyEq = new Regex(@"(?i)(api[_-]?key|secret|token)\s*[=:]\s*[^\s,;""']+", RegexOptions.Compiled);
        private static readonly Regex OpenAiKey = new Regex(@"sk-[A-Za-z0-9]{20,}", RegexOptions.Compiled);
        private static readonly Regex AnthropicKey = new Regex(@"sk-ant-[A-Za-z0-9\-]{20,}", RegexOptions.Compiled);

        public static string Redact(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            input = BearerPattern.Replace(input, "Bearer ***");
            input = AuthHeader.Replace(input, "Authorization: ***");
            input = KeyEq.Replace(input, "$1=***");
            input = OpenAiKey.Replace(input, "sk-***");
            input = AnthropicKey.Replace(input, "sk-ant-***");
            return input;
        }
    }
}
