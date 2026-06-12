using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Vcad.Plugin.Config
{
    internal sealed class UserRuleMemory
    {
        [JsonProperty("rules")]
        public List<string> Rules { get; set; } = new List<string>();

        [JsonProperty("updated_utc")]
        public DateTime UpdatedUtc { get; set; }
    }

    internal static class UserRuleMemoryStore
    {
        private const int MaxRules = 40;

        public static string MemoryPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VCAD", "user.rules.json");

        public static UserRuleMemory Load()
        {
            try
            {
                if (!File.Exists(MemoryPath)) return new UserRuleMemory();
                return JsonConvert.DeserializeObject<UserRuleMemory>(File.ReadAllText(MemoryPath)) ??
                    new UserRuleMemory();
            }
            catch
            {
                return new UserRuleMemory();
            }
        }

        public static bool TryLearnFromUserText(string text, out string learnedRule)
        {
            learnedRule = "";
            if (!LooksLikePreference(text)) return false;

            var rule = NormalizeRule(text);
            if (string.IsNullOrWhiteSpace(rule)) return false;

            var memory = Load();
            if (memory.Rules.Any(r => string.Equals(r, rule, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            memory.Rules.Insert(0, rule);
            if (memory.Rules.Count > MaxRules)
            {
                memory.Rules = memory.Rules.Take(MaxRules).ToList();
            }
            memory.UpdatedUtc = DateTime.UtcNow;
            Save(memory);
            learnedRule = rule;
            return true;
        }

        public static string BuildPromptContext()
        {
            var memory = Load();
            if (memory.Rules.Count == 0) return "";
            return "User remembered CAD preferences and rules:\n" +
                   string.Join("\n", memory.Rules.Take(12).Select((r, i) => (i + 1) + ". " + r));
        }

        private static void Save(UserRuleMemory memory)
        {
            try
            {
                var dir = Path.GetDirectoryName(MemoryPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(MemoryPath, JsonConvert.SerializeObject(memory, Formatting.Indented));
            }
            catch
            {
                // Memory is advisory. Never fail CAD work because it cannot be written.
            }
        }

        private static bool LooksLikePreference(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim();
            if (s.Length < 6 || s.Length > 300) return false;
            var markers = new[]
            {
                "以后", "默认", "总是", "每次", "不要", "别再", "优先", "习惯", "规则", "记住",
                "always", "default", "prefer", "remember", "never",
            };
            return markers.Any(m => s.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string NormalizeRule(string text)
        {
            var s = (text ?? "").Trim();
            s = s.Replace("\r", " ").Replace("\n", " ");
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            if (s.Length > 240) s = s.Substring(0, 240);
            return s;
        }
    }
}
