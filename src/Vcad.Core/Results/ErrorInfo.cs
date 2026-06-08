using Newtonsoft.Json;

namespace Vcad.Core.Results
{
    public class ErrorInfo
    {
        [JsonProperty("code")]
        public string Code { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("command_id", NullValueHandling = NullValueHandling.Ignore)]
        public string CommandId { get; set; }

        public ErrorInfo() { }

        public ErrorInfo(string code, string message, string commandId = null)
        {
            Code = code;
            Message = message;
            CommandId = commandId;
        }
    }
}
