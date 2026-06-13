using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Vcad.Plugin.Net;

namespace Vcad.Plugin.Config
{
    internal class UsageRecord
    {
        [JsonProperty("timestamp_utc")]
        public DateTime TimestampUtc { get; set; }

        [JsonProperty("request_id")]
        public string RequestId { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("input_tokens")]
        public int InputTokens { get; set; }

        [JsonProperty("output_tokens")]
        public int OutputTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("cost_usd")]
        public decimal CostUsd { get; set; }

        [JsonProperty("pricing_note")]
        public string PricingNote { get; set; }

        [JsonProperty("usage_source")]
        public string UsageSource { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("elapsed_ms")]
        public long ElapsedMs { get; set; }
    }

    internal class UsageSummary
    {
        public int Requests { get; set; }
        public int Success { get; set; }
        public int Failed { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int TotalTokens { get; set; }
        public decimal CostUsd { get; set; }
        public long TotalMs { get; set; }
        public List<UsageRecord> Recent { get; set; } = new List<UsageRecord>();
    }

    internal static class UsageLedgerStore
    {
        public static string TodayPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VCAD", "usage-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".json");

        public static UsageRecord FromAgentUsage(
            AgentUsage usage,
            string requestId,
            bool success,
            long elapsedMs,
            AgentSettings settings)
        {
            var provider = FirstNonEmpty(usage?.Provider, settings?.Provider, "unknown");
            var model = FirstNonEmpty(usage?.Model, settings?.Model, "unknown");
            var input = usage?.InputTokens ?? 0;
            var output = usage?.OutputTokens ?? 0;
            var total = usage != null && usage.TotalTokens > 0 ? usage.TotalTokens : input + output;
            var cost = ModelPricing.Estimate(provider, model, input, output);
            return new UsageRecord
            {
                TimestampUtc = DateTime.UtcNow,
                RequestId = requestId,
                Provider = provider,
                Model = model,
                InputTokens = input,
                OutputTokens = output,
                TotalTokens = total,
                CostUsd = cost.CostUsd,
                PricingNote = cost.Note,
                UsageSource = string.IsNullOrWhiteSpace(usage?.Source) ? "unknown" : usage.Source,
                Success = success,
                ElapsedMs = elapsedMs,
            };
        }

        public static void Append(UsageRecord record)
        {
            if (record == null) return;
            try
            {
                var records = LoadRecords();
                records.Add(record);
                var dir = Path.GetDirectoryName(TodayPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(TodayPath, JsonConvert.SerializeObject(records, Formatting.Indented));
            }
            catch
            {
                // Usage display must never break CAD operations.
            }
        }

        public static void ClearToday()
        {
            try
            {
                if (File.Exists(TodayPath)) File.Delete(TodayPath);
            }
            catch
            {
                // Usage reset is best-effort and must not block CAD operations.
            }
        }

        public static UsageSummary LoadTodaySummary()
        {
            var records = LoadRecords();
            return new UsageSummary
            {
                Requests = records.Count,
                Success = records.Count(r => r.Success),
                Failed = records.Count(r => !r.Success),
                InputTokens = records.Sum(r => r.InputTokens),
                OutputTokens = records.Sum(r => r.OutputTokens),
                TotalTokens = records.Sum(r => r.TotalTokens),
                CostUsd = records.Sum(r => r.CostUsd),
                TotalMs = records.Sum(r => r.ElapsedMs),
                Recent = records.OrderByDescending(r => r.TimestampUtc).Take(50).ToList(),
            };
        }

        private static List<UsageRecord> LoadRecords()
        {
            try
            {
                if (!File.Exists(TodayPath)) return new List<UsageRecord>();
                var records = JsonConvert.DeserializeObject<List<UsageRecord>>(File.ReadAllText(TodayPath)) ??
                    new List<UsageRecord>();
                foreach (var record in records)
                {
                    RepriceIfNeeded(record);
                }
                return records;
            }
            catch
            {
                return new List<UsageRecord>();
            }
        }

        private static void RepriceIfNeeded(UsageRecord record)
        {
            if (record == null) return;
            if (record.InputTokens <= 0 && record.OutputTokens <= 0) return;
            if (record.CostUsd > 0m && !IsMissingPricing(record.PricingNote)) return;

            var cost = ModelPricing.Estimate(record.Provider, record.Model, record.InputTokens, record.OutputTokens);
            record.CostUsd = cost.CostUsd;
            record.PricingNote = cost.Note;
        }

        private static bool IsMissingPricing(string note)
        {
            return !string.IsNullOrWhiteSpace(note) &&
                note.IndexOf("pricing not configured", StringComparison.OrdinalIgnoreCase) >= 0;
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

    internal sealed class CostEstimate
    {
        public decimal CostUsd { get; set; }
        public string Note { get; set; }
    }

    internal static class ModelPricing
    {
        private static readonly Dictionary<string, Tuple<decimal, decimal>> Prices =
            new Dictionary<string, Tuple<decimal, decimal>>(StringComparer.OrdinalIgnoreCase)
            {
                { "openai:gpt-5.5", Tuple.Create(5.00m, 30.00m) },
                { "openai:gpt-5.5-pro", Tuple.Create(30.00m, 180.00m) },
                { "openai:gpt-5.4", Tuple.Create(2.50m, 15.00m) },
                { "openai:gpt-5.4-mini", Tuple.Create(0.375m, 2.25m) },
                { "openai:gpt-5.4-nano", Tuple.Create(0.10m, 0.625m) },
                { "openai:gpt-5.4-pro", Tuple.Create(15.00m, 90.00m) },
                { "openai:gpt-5", Tuple.Create(1.25m, 10.00m) },
                { "openai:gpt-4.1", Tuple.Create(2.00m, 8.00m) },
                { "openai:gpt-4o", Tuple.Create(2.50m, 10.00m) },

                { "deepseek:deepseek-v4-flash", Tuple.Create(0.14m, 0.28m) },
                { "deepseek:deepseek-v4-pro", Tuple.Create(0.435m, 0.87m) },
                { "deepseek:deepseek-chat", Tuple.Create(0.14m, 0.28m) },
                { "deepseek:deepseek-reasoner", Tuple.Create(0.435m, 0.87m) },

                { "anthropic:claude-fable-5", Tuple.Create(10.00m, 50.00m) },
                { "anthropic:claude-opus-4-8", Tuple.Create(5.00m, 25.00m) },
                { "anthropic:claude-opus-4-7", Tuple.Create(5.00m, 25.00m) },
                { "anthropic:claude-opus-4-6", Tuple.Create(5.00m, 25.00m) },
                { "anthropic:claude-sonnet-4-6", Tuple.Create(3.00m, 15.00m) },
                { "anthropic:claude-haiku-4-5", Tuple.Create(1.00m, 5.00m) },

                { "gemini:gemini-3.5-flash", Tuple.Create(1.50m, 9.00m) },
                { "gemini:gemini-3.1-pro-preview", Tuple.Create(2.00m, 12.00m) },
                { "gemini:gemini-3-flash-preview", Tuple.Create(0.50m, 3.00m) },
                { "gemini:gemini-2.5-pro", Tuple.Create(1.25m, 10.00m) },
                { "gemini:gemini-2.5-flash", Tuple.Create(0.30m, 2.50m) },
                { "gemini:gemini-2.5-flash-lite", Tuple.Create(0.10m, 0.40m) },
            };

        public static CostEstimate Estimate(string provider, string model, int inputTokens, int outputTokens)
        {
            var key = (provider ?? "").Trim().ToLowerInvariant() + ":" + (model ?? "").Trim().ToLowerInvariant();
            if (!TryGetPrice(key, out var price, out var matchedKey))
            {
                return new CostEstimate
                {
                    CostUsd = 0m,
                    Note = "pricing not configured for " + provider + "/" + model,
                };
            }

            var cost = (inputTokens * price.Item1 + outputTokens * price.Item2) / 1000000m;
            return new CostEstimate
            {
                CostUsd = decimal.Round(cost, 8),
                Note = "$" + price.Item1 + "/M input, $" + price.Item2 + "/M output" +
                    (string.Equals(matchedKey, key, StringComparison.OrdinalIgnoreCase) ? "" : " (" + matchedKey + ")"),
            };
        }

        private static bool TryGetPrice(string key, out Tuple<decimal, decimal> price, out string matchedKey)
        {
            if (Prices.TryGetValue(key, out price))
            {
                matchedKey = key;
                return true;
            }

            foreach (var item in Prices.OrderByDescending(x => x.Key.Length))
            {
                if (IsSnapshotOf(item.Key, key))
                {
                    price = item.Value;
                    matchedKey = item.Key;
                    return true;
                }
            }

            matchedKey = key;
            price = null;
            return false;
        }

        private static bool IsSnapshotOf(string priceKey, string actualKey)
        {
            if (string.IsNullOrWhiteSpace(priceKey) || string.IsNullOrWhiteSpace(actualKey)) return false;
            return actualKey.StartsWith(priceKey + "-", StringComparison.OrdinalIgnoreCase) ||
                actualKey.StartsWith(priceKey + ".", StringComparison.OrdinalIgnoreCase);
        }
    }
}
