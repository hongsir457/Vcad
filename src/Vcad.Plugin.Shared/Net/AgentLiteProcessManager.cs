using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Vcad.Plugin.Config;

namespace Vcad.Plugin.Net
{
    internal sealed class AgentLiteStartResult
    {
        public bool Success { get; set; }
        public bool Started { get; set; }
        public string Message { get; set; }
    }

    internal static class AgentLiteProcessManager
    {
        private static readonly object LockObj = new object();
        private static readonly object LogLock = new object();
        private static Process _process;

        public static async Task<AgentLiteStartResult> EnsureStartedAsync(AgentSettings settings)
        {
            settings = settings ?? AgentConfigStore.LoadActive();
            var client = new AgentLiteClient(settings);
            if (await client.HealthAsync().ConfigureAwait(false))
            {
                return Ok(false, "Agent Lite is connected.");
            }

            string startError;
            if (!TryStart(settings, out startError))
            {
                return Fail("Agent Lite auto-start failed: " + startError);
            }

            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(250).ConfigureAwait(false);
                if (await client.HealthAsync().ConfigureAwait(false))
                {
                    return Ok(true, "Agent Lite started automatically.");
                }
            }

            return Fail("Agent Lite was started, but /health did not become ready. Check %APPDATA%\\VCAD\\logs or port " +
                (settings.AgentPort == 0 ? 8765 : settings.AgentPort) + ".");
        }

        private static bool TryStart(AgentSettings settings, out string error)
        {
            error = null;
            lock (LockObj)
            {
                if (_process != null && !_process.HasExited)
                {
                    return true;
                }

                StartSpec spec;
                if (!TryFindStartSpec(out spec))
                {
                    error = "Could not find Contents\\AgentLite\\Vcad.AgentLite.exe or the development project src\\Vcad.AgentLite.";
                    return false;
                }

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = spec.FileName,
                        Arguments = spec.Arguments,
                        WorkingDirectory = spec.WorkingDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                    psi.EnvironmentVariables["VCAD_AGENT_TOKEN"] = AgentTokenStore.GetOrCreate();
                    psi.EnvironmentVariables["VCAD_AGENT_PORT"] = (settings.AgentPort == 0 ? 8765 : settings.AgentPort).ToString();

                    _process = Process.Start(psi);
                    if (_process == null)
                    {
                        error = "Process.Start returned null.";
                        return false;
                    }

                    AttachLogReaders(_process);
                    return true;
                }
                catch (Exception ex)
                {
                    error = SecretRedactor.Redact(ex.Message);
                    return false;
                }
            }
        }

        private static void AttachLogReaders(Process process)
        {
            try
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "VCAD",
                    "logs");
                Directory.CreateDirectory(logDir);
                var outLog = Path.Combine(logDir, "agentlite.out.log");
                var errLog = Path.Combine(logDir, "agentlite.err.log");

                process.OutputDataReceived += (s, e) => AppendLogLine(outLog, e.Data);
                process.ErrorDataReceived += (s, e) => AppendLogLine(errLog, e.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch
            {
            }
        }

        private static void AppendLogLine(string path, string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            try
            {
                lock (LogLock)
                {
                    File.AppendAllText(path, DateTime.Now.ToString("s") + " " + line + Environment.NewLine);
                }
            }
            catch
            {
            }
        }

        private static bool TryFindStartSpec(out StartSpec spec)
        {
            spec = null;
            var baseDir = GetAssemblyDirectory();
            if (!string.IsNullOrEmpty(baseDir))
            {
                var agentDir = Path.Combine(baseDir, "AgentLite");
                var exe = Path.Combine(agentDir, "Vcad.AgentLite.exe");
                if (File.Exists(exe))
                {
                    spec = new StartSpec(exe, "", agentDir);
                    return true;
                }

                var dll = Path.Combine(agentDir, "Vcad.AgentLite.dll");
                if (File.Exists(dll))
                {
                    spec = new StartSpec("dotnet", Quote(dll), agentDir);
                    return true;
                }
            }

            var project = FindDevProject(baseDir);
            if (!string.IsNullOrEmpty(project))
            {
                spec = new StartSpec("dotnet", "run --project " + Quote(project) + " -c Release", Path.GetDirectoryName(project));
                return true;
            }

            return false;
        }

        private static string GetAssemblyDirectory()
        {
            try
            {
                var location = Assembly.GetExecutingAssembly().Location;
                return string.IsNullOrEmpty(location) ? AppDomain.CurrentDomain.BaseDirectory : Path.GetDirectoryName(location);
            }
            catch
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        private static string FindDevProject(string startDir)
        {
            var dir = string.IsNullOrEmpty(startDir)
                ? new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory)
                : new DirectoryInfo(startDir);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "Vcad.AgentLite", "Vcad.AgentLite.csproj");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
            return null;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static AgentLiteStartResult Ok(bool started, string message)
        {
            return new AgentLiteStartResult { Success = true, Started = started, Message = message };
        }

        private static AgentLiteStartResult Fail(string message)
        {
            return new AgentLiteStartResult { Success = false, Started = false, Message = message };
        }

        private sealed class StartSpec
        {
            public string FileName { get; }
            public string Arguments { get; }
            public string WorkingDirectory { get; }

            public StartSpec(string fileName, string arguments, string workingDirectory)
            {
                FileName = fileName;
                Arguments = arguments ?? "";
                WorkingDirectory = workingDirectory ?? "";
            }
        }
    }
}
