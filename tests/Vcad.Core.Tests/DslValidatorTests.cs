using Vcad.Core.Results;
using Vcad.Core.Validation;
using Xunit;

namespace Vcad.Core.Tests;

public class DslValidatorTests
{
    [Fact]
    public void Rejects_empty_input()
    {
        var r = DslValidator.ParseAndValidate("");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }

    [Fact]
    public void Rejects_invalid_json()
    {
        var r = DslValidator.ParseAndValidate("{not json");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }

    [Fact]
    public void Rejects_missing_version()
    {
        var r = DslValidator.ParseAndValidate(@"{ ""commands"": [] }");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }

    [Fact]
    public void Rejects_unsupported_version()
    {
        var r = DslValidator.ParseAndValidate(@"{ ""version"": ""vcad_dsl_v9"", ""commands"": [{""type"":""draw_line""}] }");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.UnsupportedVersion, r.ErrorCode);
    }

    [Fact]
    public void Rejects_empty_commands()
    {
        var r = DslValidator.ParseAndValidate(@"{ ""version"": ""vcad_dsl_v1"", ""commands"": [] }");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }

    [Fact]
    public void Rejects_unknown_command_type()
    {
        var r = DslValidator.ParseAndValidate(@"{ ""version"": ""vcad_dsl_v1"", ""commands"": [{""type"":""run_shell""}] }");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.CommandNotAllowed, r.ErrorCode);
    }

    [Fact]
    public void Rejects_command_without_type()
    {
        var r = DslValidator.ParseAndValidate(@"{ ""version"": ""vcad_dsl_v1"", ""commands"": [{}] }");
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }

    [Fact]
    public void Rejects_duplicate_command_ids()
    {
        var json = @"{
          ""version"": ""vcad_dsl_v1"",
          ""commands"": [
            {""type"":""draw_line"",""id"":""A""},
            {""type"":""draw_line"",""id"":""A""}
          ]
        }";
        var r = DslValidator.ParseAndValidate(json);
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }

    [Fact]
    public void Accepts_minimal_valid_dsl()
    {
        var json = @"{
          ""version"": ""vcad_dsl_v1"",
          ""commands"": [
            { ""type"": ""draw_rectangle"", ""id"": ""R1"", ""origin"": [0,0], ""width"": 100, ""height"": 50 }
          ]
        }";
        var r = DslValidator.ParseAndValidate(json);
        Assert.True(r.IsValid);
        Assert.NotNull(r.Request);
        Assert.Single(r.Request!.Commands);
    }

    [Fact]
    public void Rejects_too_many_commands()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(@"{ ""version"":""vcad_dsl_v1"", ""commands"":[");
        for (int i = 0; i < ParameterLimits.MaxCommandsPerRequest + 1; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(@"{ ""type"":""draw_line"" }");
        }
        sb.Append("] }");
        var r = DslValidator.ParseAndValidate(sb.ToString());
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.ParamRange, r.ErrorCode);
    }

    [Fact]
    public void Rejects_oversize_request_body()
    {
        // Build a clearly-over-1MB string.
        var huge = new string('x', (int)ParameterLimits.JsonRequestMaxBytes + 16);
        var json = @"{""version"":""vcad_dsl_v1"",""commands"":[{""type"":""draw_text"",""text"":""" + huge + @"""}]}";
        var r = DslValidator.ParseAndValidate(json);
        Assert.False(r.IsValid);
        Assert.Equal(ErrorCodes.SchemaInvalid, r.ErrorCode);
    }
}
