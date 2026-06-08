using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Vcad.Plugin.Config
{
    internal class AgentConfigStore
    {
        private const string EnvOverrideProvider = "VCAD_AGENT_PROVIDER";
        private const string EnvOverrideBaseUrl = "VCAD_AGENT_BASE_URL";
        private const string EnvOverrideModel = "VCAD_AGENT_MODEL";
        private const string EnvOverrideApiKey = "VCAD_AGENT_API_KEY";
        private const string EnvOverridePort = "VCAD_AGENT_PORT";

        public static string ConfigPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VCAD", "agent.config.json");

        [JsonProperty("active_profile")]
        public string ActiveProfileName { get; set; }

        [JsonProperty("profiles")]
        public List<AgentSettings> Profiles { get; set; } = new List<AgentSettings>();

        public static AgentConfigStore LoadAll()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    return new AgentConfigStore();
                }
                var json = File.ReadAllText(ConfigPath);
                var store = JsonConvert.DeserializeObject<AgentConfigStore>(json) ?? new AgentConfigStore();
                if (store.Profiles == null) store.Profiles = new List<AgentSettings>();
                foreach (var p in store.Profiles)
                {
                    p.ApiKeyPlain = SecretProtector.Unprotect(p.ApiKeyEncrypted);
                }
                return store;
            }
            catch
            {
                return new AgentConfigStore();
            }
        }

        public static void SaveAll(AgentConfigStore store)
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                foreach (var p in store.Profiles)
                {
                    p.ApiKeyEncrypted = SecretProtector.Protect(p.ApiKeyPlain);
                }

                var json = JsonConvert.SerializeObject(store, Formatting.Indented);
                File.WriteAllText(ConfigPath, json);
            }
            catch
            {
                // best-effort, never throw from settings save
            }
        }

        public static AgentSettings LoadActive()
        {
            var store = LoadAll();
            var s = store.GetActiveOrFirst() ?? new AgentSettings { Name = "default" };
            ApplyEnvOverrides(s);
            return s;
        }

        public AgentSettings GetActiveOrFirst()
        {
            if (!string.IsNullOrEmpty(ActiveProfileName))
            {
                var p = GetProfile(ActiveProfileName);
                if (p != null) return p;
            }
            return Profiles.Count > 0 ? Profiles[0] : null;
        }

        public AgentSettings GetProfile(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (var p in Profiles)
            {
                if (string.Equals(p.Name, name, StringComparison.Ordinal)) return p;
            }
            return null;
        }

        public void UpsertProfile(AgentSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Name)) return;
            for (int i = 0; i < Profiles.Count; i++)
            {
                if (Profiles[i].Name == settings.Name)
                {
                    Profiles[i] = settings;
                    return;
                }
            }
            Profiles.Add(settings);
        }

        public void RemoveProfile(string name)
        {
            Profiles.RemoveAll(p => p.Name == name);
            if (ActiveProfileName == name)
            {
                ActiveProfileName = Profiles.Count > 0 ? Profiles[0].Name : null;
            }
        }

        public IEnumerable<string> ProfileNames()
        {
            foreach (var p in Profiles) yield return p.Name;
        }

        private static void ApplyEnvOverrides(AgentSettings s)
        {
            var provider = Environment.GetEnvironmentVariable(EnvOverrideProvider);
            if (!string.IsNullOrEmpty(provider)) s.Provider = provider;
            var baseUrl = Environment.GetEnvironmentVariable(EnvOverrideBaseUrl);
            if (!string.IsNullOrEmpty(baseUrl)) s.ApiBaseUrl = baseUrl;
            var model = Environment.GetEnvironmentVariable(EnvOverrideModel);
            if (!string.IsNullOrEmpty(model)) s.Model = model;
            var key = Environment.GetEnvironmentVariable(EnvOverrideApiKey);
            if (!string.IsNullOrEmpty(key)) s.ApiKeyPlain = key;
            var port = Environment.GetEnvironmentVariable(EnvOverridePort);
            if (!string.IsNullOrEmpty(port) && int.TryParse(port, out var p)) s.AgentPort = p;
        }
    }
}
