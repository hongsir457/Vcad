using Newtonsoft.Json;

namespace Vcad.Plugin.Config
{
    /// <summary>
    /// User-configurable Agent Lite settings.
    /// ApiKeyPlain is kept in memory only; persistence uses ApiKeyEncrypted.
    /// </summary>
    internal class AgentSettings
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("api_base_url")]
        public string ApiBaseUrl { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("api_key_encrypted")]
        public string ApiKeyEncrypted { get; set; }

        [JsonIgnore]
        public string ApiKeyPlain { get; set; }

        [JsonProperty("agent_port")]
        public int AgentPort { get; set; } = 8765;

        [JsonProperty("strict_json")]
        public bool StrictJson { get; set; } = true;

        [JsonProperty("timeout_seconds")]
        public int TimeoutSeconds { get; set; } = 300;

        [JsonProperty("execution_mode")]
        public string ExecutionMode { get; set; } = "confirm";

        [JsonProperty("memory_enabled")]
        public bool MemoryEnabled { get; set; } = true;

        [JsonProperty("auto_run_after_parse")]
        public bool AutoRunAfterParse { get; set; } = false;
    }
}
