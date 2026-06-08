using Newtonsoft.Json;

namespace Vcad.Core.Dsl.Commands
{
    public class DrawLineCommand
    {
        [JsonProperty("type")]
        public string Type { get; set; } = CommandTypes.DrawLine;

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("start")]
        public double[] Start { get; set; }

        [JsonProperty("end")]
        public double[] End { get; set; }

        [JsonProperty("layer")]
        public string Layer { get; set; }
    }
}
