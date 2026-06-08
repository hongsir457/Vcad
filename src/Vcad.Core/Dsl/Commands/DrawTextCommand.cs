using Newtonsoft.Json;

namespace Vcad.Core.Dsl.Commands
{
    public class DrawTextCommand
    {
        [JsonProperty("type")]
        public string Type { get; set; } = CommandTypes.DrawText;

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("position")]
        public double[] Position { get; set; }

        [JsonProperty("height")]
        public double Height { get; set; }

        [JsonProperty("rotation")]
        public double Rotation { get; set; }

        [JsonProperty("alignment")]
        public string Alignment { get; set; } = "left";

        [JsonProperty("text_style")]
        public string TextStyle { get; set; } = "STANDARD";

        [JsonProperty("layer")]
        public string Layer { get; set; }
    }
}
