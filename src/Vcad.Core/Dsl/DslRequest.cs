using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Vcad.Core.Dsl
{
    public class DslRequest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("unit")]
        public string Unit { get; set; } = "mm";

        [JsonProperty("coordinate_system")]
        public string CoordinateSystem { get; set; } = "WCS";

        [JsonProperty("limits")]
        public DslLimits Limits { get; set; }

        [JsonProperty("commands")]
        public List<JObject> Commands { get; set; } = new List<JObject>();
    }

    public class DslLimits
    {
        [JsonProperty("max_commands")]
        public int? MaxCommands { get; set; }
    }
}
