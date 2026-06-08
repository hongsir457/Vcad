using Newtonsoft.Json;

namespace Vcad.Core.Mapping
{
    public class IdMapEntry
    {
        [JsonProperty("dsl_id")]
        public string DslId { get; set; }

        [JsonProperty("handle")]
        public string Handle { get; set; }

        [JsonProperty("object_id")]
        public string ObjectId { get; set; }

        [JsonProperty("entity_type")]
        public string EntityType { get; set; }

        [JsonProperty("layer")]
        public string Layer { get; set; }
    }
}
