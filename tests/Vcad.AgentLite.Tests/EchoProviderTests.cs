using System.Text.Json.Nodes;
using Vcad.AgentLite;
using Vcad.AgentLite.Providers;
using Xunit;

namespace Vcad.AgentLite.Tests;

public class EchoProviderTests
{
    [Fact]
    public async Task Rectangle_keyword_emits_rectangle()
    {
        var p = new EchoProvider();
        var dsl = await p.ParseAsync(new ParseRequest { text = "draw a rectangle 6m x 4m" });

        Assert.NotNull(dsl);
        Assert.Equal("vcad_dsl_v1", dsl["version"]!.GetValue<string>());
        var cmds = dsl["commands"]!.AsArray();
        Assert.NotEmpty(cmds);
        Assert.Contains(cmds, c => c!["type"]!.GetValue<string>() == "draw_rectangle");
    }

    [Fact]
    public async Task Empty_text_falls_back_to_text_command()
    {
        var p = new EchoProvider();
        var dsl = await p.ParseAsync(new ParseRequest { text = "" });
        var cmds = dsl["commands"]!.AsArray();
        Assert.Contains(cmds, c => c!["type"]!.GetValue<string>() == "draw_text");
    }

    [Fact]
    public async Task All_commands_are_in_the_v01_whitelist()
    {
        var allowed = new HashSet<string> { "create_layer", "draw_line", "draw_rectangle", "draw_text" };
        var p = new EchoProvider();
        foreach (var t in new[] { "rectangle", "矩形", "room", "draw text", "" })
        {
            var dsl = await p.ParseAsync(new ParseRequest { text = t });
            foreach (var c in dsl["commands"]!.AsArray())
            {
                Assert.Contains(c!["type"]!.GetValue<string>(), allowed);
            }
        }
    }
}
