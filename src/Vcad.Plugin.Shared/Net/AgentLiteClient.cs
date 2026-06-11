using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vcad.Plugin.Config;

namespace Vcad.Plugin.Net
{
    /// <summary>
    /// Calls the local Vcad.AgentLite service running on 127.0.0.1.
    /// Never sends data to any VCAD-owned endpoint.
    /// </summary>
    internal class AgentLiteClient
    {
        private readonly AgentSettings _settings;

        public AgentLiteClient(AgentSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<bool> HealthAsync()
        {
            var url = BuildUrl("/health");
            using (var client = NewClient())
            {
                try
                {
                    var resp = await client.GetAsync(url).ConfigureAwait(false);
                    return resp.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            }
        }

        public async Task<string> ParseAsync(string naturalLanguage)
        {
            if (string.IsNullOrWhiteSpace(naturalLanguage)) return null;

            var payload = new JObject
            {
                ["request_id"] = "req-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"),
                ["text"] = naturalLanguage,
                ["context"] = new JObject { ["unit"] = "mm", ["coordinate_system"] = "WCS" },
                ["options"] = new JObject { ["max_commands"] = 50 },
                ["provider"] = BuildProviderPayload(),
            };

            using (var client = NewClient())
            using (var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json"))
            {
                var resp = await client.PostAsync(BuildUrl("/parse"), content).ConfigureAwait(false);
                var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("Agent /parse failed: " + (int)resp.StatusCode + " " +
                        SecretRedactor.Redact(body));
                }

                var jo = JObject.Parse(body);
                var dsl = jo["dsl"];
                if (dsl == null) return null;
                return dsl.ToString(Formatting.Indented);
            }
        }

        private string BuildUrl(string path)
        {
            var port = _settings.AgentPort == 0 ? 8765 : _settings.AgentPort;
            return "http://127.0.0.1:" + port + path;
        }

        private JObject BuildProviderPayload()
        {
            var provider = new JObject
            {
                ["name"] = string.IsNullOrWhiteSpace(_settings.Provider) ? "echo" : _settings.Provider,
                ["strict_json"] = _settings.StrictJson,
            };

            if (!string.IsNullOrWhiteSpace(_settings.ApiBaseUrl))
            {
                provider["base_url"] = _settings.ApiBaseUrl;
            }
            if (!string.IsNullOrWhiteSpace(_settings.Model))
            {
                provider["model"] = _settings.Model;
            }
            if (!string.IsNullOrWhiteSpace(_settings.ApiKeyPlain))
            {
                provider["api_key"] = _settings.ApiKeyPlain;
            }

            return provider;
        }

        private HttpClient NewClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds == 0 ? 30 : _settings.TimeoutSeconds),
            };
            client.DefaultRequestHeaders.Add("X-VCAD-Agent-Token", AgentTokenStore.GetOrCreate());
            return client;
        }
    }
}
