using Newtonsoft.Json;

namespace Vcad.Core.Results
{
    public class EntityRef
    {
        [JsonProperty("dsl_id")]
        public string DslId { get; set; }

        [JsonProperty("entity_type")]
        public string EntityType { get; set; }

        [JsonProperty("handle")]
        public string Handle { get; set; }

        [JsonProperty("object_id")]
        public string ObjectId { get; set; }

        [JsonProperty("layer", NullValueHandling = NullValueHandling.Ignore)]
        public string Layer { get; set; }
    }
}
