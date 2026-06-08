using Vcad.AgentLite;
using Xunit;

namespace Vcad.AgentLite.Tests;

public class SecretRedactorTests
{
    [Fact]
    public void Redacts_openai_keys()
    {
        var input = "Auth failed for sk-abcd1234567890ABCDEF1234567890ABCDEF";
        var redacted = SecretRedactor.Redact(input);
        Assert.DoesNotContain("abcd1234567890ABCDEF", redacted);
        Assert.Contains("sk-***", redacted);
    }

    [Fact]
    public void Redacts_anthropic_keys()
    {
        var input = "header= sk-ant-abc1234567890DEF1234567890";
        var redacted = SecretRedactor.Redact(input);
        Assert.Contains("sk-ant-***", redacted);
    }

    [Fact]
    public void Redacts_bearer_token()
    {
        var input = "Authorization: Bearer eyJabc.def-ghi";
        var redacted = SecretRedactor.Redact(input);
        Assert.DoesNotContain("eyJabc", redacted);
        Assert.Contains("Bearer ***", redacted);
    }

    [Fact]
    public void Redacts_api_key_assignment()
    {
        var input = "api_key=AKIA0123456789ABCDEF";
        var redacted = SecretRedactor.Redact(input);
        Assert.DoesNotContain("AKIA0123456789ABCDEF", redacted);
        Assert.Contains("api_key=***", redacted);
    }

    [Fact]
    public void Handles_null_and_empty()
    {
        Assert.Equal("", SecretRedactor.Redact(null));
        Assert.Equal("", SecretRedactor.Redact(""));
    }
}
