using Newtonsoft.Json.Linq;
using Vcad.Plugin.Pipeline;
using Xunit;

namespace Vcad.Core.Tests;

public class CadAgentPipelineTests
{
    [Fact]
    public void Adapter_requires_confirmed_cad_ir_and_does_not_reuse_interpreter_dsl()
    {
        var candidate = CadAgentPipeline.Interpret("画一个矩形", ValidRectangleDsl());

        Assert.Equal("cad_ir_v1", candidate.CadIr.Value<string>("schema"));
        Assert.Equal("create_geometry", candidate.CadIr.Value<string>("action"));
        Assert.Throws<InvalidOperationException>(() => CadAgentPipeline.AdaptToAdapterCommand(candidate));

        candidate.Confirmed = true;
        candidate.InterpreterDsl = "{ not the adapter input }";
        var adapterCommand = CadAgentPipeline.AdaptToAdapterCommand(candidate);

        Assert.Equal("vcad_adapter_command_v1", adapterCommand.Value<string>("schema"));
        Assert.Equal("cad_ir_v1", adapterCommand.Value<string>("source_schema"));
        Assert.True(adapterCommand.Value<bool>("safe_to_execute"));

        var command = JObject.Parse(adapterCommand.Value<string>("command")!);
        Assert.Equal("vcad_dsl_v1", command.Value<string>("version"));
        Assert.Equal("draw_rectangle", command["commands"]![0]!.Value<string>("type"));
    }

    [Fact]
    public void High_risk_cad_ir_requires_second_confirmation()
    {
        var candidate = CadAgentPipeline.Interpret("批量缩放这些对象", ValidRectangleDsl());

        Assert.Equal("high", candidate.RiskLevel);
        Assert.True(candidate.RequiresSecondConfirmation);

        candidate.Confirmed = true;
        Assert.Throws<InvalidOperationException>(() => CadAgentPipeline.AdaptToAdapterCommand(candidate));

        candidate.SecondConfirmed = true;
        var adapterCommand = CadAgentPipeline.AdaptToAdapterCommand(candidate);
        Assert.Equal("vcad_dsl", adapterCommand.Value<string>("command_type"));
    }

    [Fact]
    public void Script_like_payload_is_blocked_before_adapter()
    {
        var dsl = """
        {
          "version": "vcad_dsl_v1",
          "commands": [
            {
              "type": "draw_text",
              "id": "T1",
              "text": "OK",
              "position": [0, 0],
              "height": 250,
              "lisp": "(command \"_.ERASE\" \"ALL\" \"\")"
            }
          ]
        }
        """;

        var candidate = CadAgentPipeline.Interpret("写一个文字", dsl);

        Assert.False(candidate.Safety.IsAllowed);
        Assert.Contains(candidate.Safety.Blocks, b => b.Contains("AutoLISP", StringComparison.OrdinalIgnoreCase));
    }

    private static string ValidRectangleDsl()
    {
        return """
        {
          "version": "vcad_dsl_v1",
          "unit": "mm",
          "coordinate_system": "WCS",
          "commands": [
            {
              "type": "draw_rectangle",
              "id": "R1",
              "origin": [0, 0],
              "width": 100,
              "height": 50,
              "layer": "A-WALL"
            }
          ]
        }
        """;
    }
}
