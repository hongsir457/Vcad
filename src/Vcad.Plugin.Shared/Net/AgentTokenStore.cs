using System;
using System.IO;
using System.Security.Cryptography;

namespace Vcad.Plugin.Net
{
    internal static class AgentTokenStore
    {
        private static readonly object _lock = new object();
        private static string _cached;

        public static string Path =>
            System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VCAD", "agent.token");

        public static string GetOrCreate()
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(_cached)) return _cached;
                try
                {
                    if (File.Exists(Path))
                    {
                        _cached = File.ReadAllText(Path).Trim();
                        if (!string.IsNullOrEmpty(_cached)) return _cached;
                    }

                    var bytes = new byte[24];
                    using (var rng = RandomNumberGenerator.Create())
                    {
                        rng.GetBytes(bytes);
                    }
                    _cached = Convert.ToBase64String(bytes);
                    var dir = System.IO.Path.GetDirectoryName(Path);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(Path, _cached);
                    return _cached;
                }
                catch
                {
                    _cached = Guid.NewGuid().ToString("N");
                    return _cached;
                }
            }
        }
    }
}
