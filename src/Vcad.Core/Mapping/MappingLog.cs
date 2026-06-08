using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Vcad.Core.Mapping
{
    public class MappingLog
    {
        public const string CurrentVersion = "vcad_mapping_v1";

        [JsonProperty("mapping_version")]
        public string MappingVersion { get; set; } = CurrentVersion;

        [JsonProperty("request_id")]
        public string RequestId { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

        [JsonProperty("items")]
        public List<IdMapEntry> Items { get; set; } = new List<IdMapEntry>();
    }
}
