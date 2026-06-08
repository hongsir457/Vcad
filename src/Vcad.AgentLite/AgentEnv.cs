namespace Vcad.AgentLite;

public static class AgentEnv
{
    public static int Port =>
        int.TryParse(Environment.GetEnvironmentVariable("VCAD_AGENT_PORT"), out var p) ? p : 8765;

    public static string? Token =>
        Environment.GetEnvironmentVariable("VCAD_AGENT_TOKEN");

    public static string Provider =>
        Environment.GetEnvironmentVariable("VCAD_AGENT_PROVIDER") ?? "echo";

    public static string? BaseUrl =>
        Environment.GetEnvironmentVariable("VCAD_AGENT_BASE_URL");

    public static string? Model =>
        Environment.GetEnvironmentVariable("VCAD_AGENT_MODEL");

    public static string? ApiKey =>
        Environment.GetEnvironmentVariable("VCAD_AGENT_API_KEY");
}
