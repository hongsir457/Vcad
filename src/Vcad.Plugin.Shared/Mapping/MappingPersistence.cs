using System;
using System.IO;
using Newtonsoft.Json;
using Vcad.Core.Mapping;

namespace Vcad.Plugin.Mapping
{
    internal static class MappingPersistence
    {
        public static string LogDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VCAD", "logs");

        public static void Append(IdMap map, string requestId)
        {
            if (map == null || map.Count == 0) return;
            Directory.CreateDirectory(LogDir);
            var path = Path.Combine(LogDir, "mapping-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".jsonl");

            var record = new MappingLog
            {
                RequestId = requestId,
                CreatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
            foreach (var kv in map.All())
            {
                record.Items.Add(new IdMapEntry
                {
                    DslId = kv.Value.DslId,
                    Handle = kv.Value.Handle,
                    ObjectId = kv.Value.ObjectId,
                    EntityType = kv.Value.EntityType,
                    Layer = kv.Value.Layer,
                });
            }

            File.AppendAllText(path, JsonConvert.SerializeObject(record) + Environment.NewLine);
        }
    }
}
