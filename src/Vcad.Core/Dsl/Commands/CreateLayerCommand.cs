using Newtonsoft.Json;

namespace Vcad.Core.Dsl.Commands
{
    public class CreateLayerCommand
    {
        [JsonProperty("type")]
        public string Type { get; set; } = CommandTypes.CreateLayer;

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("color")]
        public int? Color { get; set; }

        [JsonProperty("linetype")]
        public string Linetype { get; set; }

        [JsonProperty("lineweight")]
        public int? Lineweight { get; set; }

        [JsonProperty("plot")]
        public bool? Plot { get; set; }

        [JsonProperty("idempotent")]
        public bool? Idempotent { get; set; } = true;
    }
}
