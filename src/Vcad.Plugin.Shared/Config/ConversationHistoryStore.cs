using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Vcad.Plugin.Config
{
    internal sealed class ConversationMessageRecord
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("timestamp_utc")]
        public DateTime TimestampUtc { get; set; }
    }

    internal sealed class ConversationHistoryRecord
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("created_utc")]
        public DateTime CreatedUtc { get; set; }

        [JsonProperty("updated_utc")]
        public DateTime UpdatedUtc { get; set; }

        [JsonProperty("messages")]
        public List<ConversationMessageRecord> Messages { get; set; } = new List<ConversationMessageRecord>();
    }

    internal static class ConversationHistoryStore
    {
        private const int MaxConversations = 40;
        private const int MaxMessagesPerConversation = 80;
        private const int MaxMessageChars = 4000;

        public static string HistoryPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VCAD", "conversation-history.json");

        public static List<ConversationHistoryRecord> LoadAll()
        {
            try
            {
                if (!File.Exists(HistoryPath)) return new List<ConversationHistoryRecord>();
                return JsonConvert.DeserializeObject<List<ConversationHistoryRecord>>(File.ReadAllText(HistoryPath)) ??
                    new List<ConversationHistoryRecord>();
            }
            catch
            {
                return new List<ConversationHistoryRecord>();
            }
        }

        public static void Save(ConversationHistoryRecord record)
        {
            if (record == null || string.IsNullOrWhiteSpace(record.Id)) return;
            try
            {
                record.Messages = (record.Messages ?? new List<ConversationMessageRecord>())
                    .Where(m => !string.IsNullOrWhiteSpace(m?.Text))
                    .Take(Math.Max(1, MaxMessagesPerConversation))
                    .Select(CopyMessage)
                    .ToList();
                if (record.Messages.Count == 0) return;

                record.Title = FirstNonEmpty(record.Title, BuildTitle(record));
                record.UpdatedUtc = record.UpdatedUtc == default(DateTime) ? DateTime.UtcNow : record.UpdatedUtc;
                record.CreatedUtc = record.CreatedUtc == default(DateTime) ? record.UpdatedUtc : record.CreatedUtc;

                var records = LoadAll();
                records.RemoveAll(r => string.Equals(r.Id, record.Id, StringComparison.OrdinalIgnoreCase));
                records.Insert(0, record);
                records = records
                    .OrderByDescending(r => r.UpdatedUtc)
                    .Take(MaxConversations)
                    .ToList();

                var dir = Path.GetDirectoryName(HistoryPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(HistoryPath, JsonConvert.SerializeObject(records, Formatting.Indented));
            }
            catch
            {
                // Conversation history must not block CAD work.
            }
        }

        public static string NewId()
        {
            return "chat-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
        }

        private static ConversationMessageRecord CopyMessage(ConversationMessageRecord message)
        {
            return new ConversationMessageRecord
            {
                Role = FirstNonEmpty(message.Role, "user"),
                Text = Truncate(message.Text, MaxMessageChars),
                TimestampUtc = message.TimestampUtc == default(DateTime) ? DateTime.UtcNow : message.TimestampUtc,
            };
        }

        private static string BuildTitle(ConversationHistoryRecord record)
        {
            var firstUser = (record.Messages ?? new List<ConversationMessageRecord>())
                .FirstOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
            var title = firstUser == null ? "未命名对话" : firstUser.Text;
            return Truncate(title.Replace("\r", " ").Replace("\n", " ").Trim(), 40);
        }

        private static string Truncate(string value, int maxChars)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxChars) return value ?? "";
            return value.Substring(0, Math.Max(0, maxChars - 1)) + "…";
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return "";
        }
    }
}
