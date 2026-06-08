using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public interface IProvider
{
    Task<JsonNode> ParseAsync(ParseRequest req);
}
