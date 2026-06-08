using Newtonsoft.Json;

namespace Vcad.Core.Dsl.Commands
{
    public class DrawRectangleCommand
    {
        [JsonProperty("type")]
        public string Type { get; set; } = CommandTypes.DrawRectangle;

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("origin")]
        public double[] Origin { get; set; }

        [JsonProperty("width")]
        public double Width { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }

        [JsonProperty("rotation")]
        public double Rotation { get; set; }

        [JsonProperty("layer")]
        public string Layer { get; set; }
    }
}
