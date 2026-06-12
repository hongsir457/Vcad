using System.Text.Json.Nodes;

namespace Vcad.AgentLite.Providers;

public interface IProvider
{
    Task<ProviderParseResult> ParseAsync(ParseRequest req);
}
