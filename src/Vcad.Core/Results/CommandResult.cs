using System.Collections.Generic;
using Newtonsoft.Json;

namespace Vcad.Core.Results
{
    public class CommandResult
    {
        [JsonProperty("command_id")]
        public string CommandId { get; set; }

        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("entities")]
        public List<EntityRef> Entities { get; set; } = new List<EntityRef>();

        [JsonProperty("warnings")]
        public List<string> Warnings { get; set; } = new List<string>();

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public ErrorInfo Error { get; set; }
    }
}
