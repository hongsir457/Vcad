using Vcad.AgentLite;
using Vcad.AgentLite.Providers;
using Vcad.Core.Validation;
using Xunit;

namespace Vcad.AgentLite.Tests;

/// <summary>
/// The Echo provider is supposed to always produce DSL that the Core
/// validator accepts. If this test breaks, the contract between Agent and
/// plugin is broken.
/// </summary>
public class EchoProviderProducesValidDslTests
{
    [Theory]
    [InlineData("draw a rectangle 6m x 4m")]
    [InlineData("room 6 by 4")]
    [InlineData("画一个房间")]
    [InlineData("hello")]
    [InlineData("")]
    public async Task Echo_DSL_is_accepted_by_Core_validator(string text)
    {
        var p = new EchoProvider();
        var dsl = await p.ParseAsync(new ParseRequest { text = text });
        var json = dsl.Dsl!.ToJsonString();

        var v = DslValidator.ParseAndValidate(json);
        Assert.True(v.IsValid, "echo output rejected: " + v.ErrorMessage + " // dsl=" + json);
    }
}
