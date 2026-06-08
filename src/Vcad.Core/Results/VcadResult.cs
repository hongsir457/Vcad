using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Vcad.Core.Results
{
    public class VcadResult
    {
        [JsonProperty("version")]
        public string Version { get; set; } = DslVersion.ResultCurrent;

        [JsonProperty("request_id")]
        public string RequestId { get; set; }

        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("summary")]
        public SummaryInfo Summary { get; set; } = new SummaryInfo();

        [JsonProperty("results")]
        public List<CommandResult> Results { get; set; } = new List<CommandResult>();

        [JsonProperty("errors")]
        public List<ErrorInfo> Errors { get; set; } = new List<ErrorInfo>();

        public static VcadResult NewFailure(string requestId, string code, string message)
        {
            var result = new VcadResult
            {
                RequestId = requestId ?? Guid.NewGuid().ToString("N"),
                Success = false,
            };
            result.Errors.Add(new ErrorInfo(code, message));
            return result;
        }
    }

    public class SummaryInfo
    {
        [JsonProperty("total")]
        public int Total { get; set; }

        [JsonProperty("succeeded")]
        public int Succeeded { get; set; }

        [JsonProperty("failed")]
        public int Failed { get; set; }
    }
}
