using Newtonsoft.Json.Linq;
using Vcad.Core.Results;
using Vcad.Plugin.Pipeline;
using Xunit;

namespace Vcad.Core.Tests;

public class CadAgentPipelineTests
{
    [Fact]
    public void Cad_ir_is_immutable_task_input_and_execution_mode_only_exists_on_adapter_command()
    {
        var candidate = CadAgentPipeline.Interpret("draw a rectangle", ValidRectangleDsl());

        Assert.Equal("cad_ir_v1", candidate.CadIr.Value<string>("schema"));
        Assert.Equal("copy_objects", candidate.CadIr.Value<string>("action"));
        Assert.Null(FindProperty(candidate.CadIr, "dry_run"));
        Assert.Null(FindProperty(candidate.CadIr, "execute"));
        Assert.Null(FindProperty(candidate.CadIr, "mode"));
        Assert.Equal(candidate.TaskRecord.IrHash, candidate.TaskRecord.ToJson().Value<string>("ir_hash"));
        Assert.NotNull(candidate.TaskRecord.PreviewSnapshotHash);

        Assert.Throws<InvalidOperationException>(() =>
            CadAgentPipeline.AdaptToAdapterCommand(candidate, "execute", "idem-1"));

        var token = CadAgentPipeline.Confirm(candidate);
        Assert.False(string.IsNullOrEmpty(token));

        var adapterCommand = CadAgentPipeline.AdaptToAdapterCommand(candidate, "execute", "idem-1");

        Assert.Equal("adapter_command_v1", adapterCommand.Value<string>("schema"));
        Assert.Equal("execute", adapterCommand.Value<string>("mode"));
        Assert.Equal("cad_ir_v1", candidate.TaskRecord.CadIr.Value<string>("schema"));
        Assert.True(adapterCommand.Value<bool>("safe_to_execute"));
    }

    [Fact]
    public void High_risk_task_requires_second_confirm_token_before_execution()
    {
        var candidate = CadAgentPipeline.Interpret("scale selected objects", ValidRectangleDsl());

        Assert.Equal("scale_objects", candidate.CadIr.Value<string>("action"));
        Assert.Equal("high", candidate.RiskLevel);
        Assert.True(candidate.RequiresSecondConfirmation);

        CadAgentPipeline.Confirm(candidate);
        var missingSecond = Assert.Throws<InvalidOperationException>(() =>
            CadAgentPipeline.AdaptToAdapterCommand(candidate, "execute", "idem-high"));
        Assert.Contains("second confirm", missingSecond.Message, StringComparison.OrdinalIgnoreCase);

        var second = CadAgentPipeline.SecondConfirm(candidate);
        Assert.False(string.IsNullOrEmpty(second));
        Assert.Contains(candidate.TaskRecord.AuditEvents, e => e.Value<string>("event_type") == "second_confirm");
    }

    [Fact]
    public void Script_like_payload_and_disabled_actions_are_rejected_before_risk_policy()
    {
        var scriptDsl = """
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

        var scriptCandidate = CadAgentPipeline.Interpret("draw text", scriptDsl);
        Assert.False(scriptCandidate.Safety.IsAllowed);
        Assert.Equal("rejected", scriptCandidate.TaskRecord.Status);
        Assert.Contains(scriptCandidate.Safety.Blocks, b => b.Contains("AutoLISP", StringComparison.OrdinalIgnoreCase));

        var disabledCandidate = CadAgentPipeline.Interpret("global replace all text", ValidRectangleDsl());
        Assert.False(disabledCandidate.Safety.IsAllowed);
        Assert.Equal("global_replace", disabledCandidate.CadIr.Value<string>("action"));
        Assert.Equal("rejected", disabledCandidate.TaskRecord.Status);
    }

    [Fact]
    public void Execute_preflight_marks_task_stale_when_preview_snapshot_changes()
    {
        var candidate = CadAgentPipeline.Interpret("draw a rectangle", ValidRectangleDsl());
        CadAgentPipeline.Confirm(candidate);
        candidate.TaskRecord.PreviewSnapshotHash = "sha256:tampered";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CadAgentPipeline.AdaptToAdapterCommand(candidate, "execute", "idem-stale"));

        Assert.Contains("stale", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("stale", candidate.TaskRecord.Status);
        Assert.Contains(candidate.TaskRecord.AuditEvents, e => e.Value<string>("event_type") == "stale");
    }

    [Fact]
    public void Cad_result_v1_is_idempotent_for_repeated_execute_key()
    {
        var candidate = CadAgentPipeline.Interpret("draw a rectangle", ValidRectangleDsl());
        CadAgentPipeline.Confirm(candidate);
        CadAgentPipeline.AdaptToAdapterCommand(candidate, "execute", "idem-result");

        var result = new VcadResult
        {
            RequestId = "req-1",
            Success = true,
        };
        result.Summary.Total = 1;
        result.Summary.Succeeded = 1;

        var first = CadAgentPipeline.RecordExecutionResult(candidate, result, 25, "idem-result");
        var second = CadAgentPipeline.RecordExecutionResult(candidate, result, 30, "idem-result");

        Assert.Equal("cad_result_v1", first.Value<string>("schema"));
        Assert.Equal("success", first.Value<string>("status"));
        Assert.False(first["idempotency"]!.Value<bool>("replayed"));
        Assert.True(second["idempotency"]!.Value<bool>("replayed"));
        Assert.Single(candidate.TaskRecord.ExecuteKeys);
    }

    private static JProperty? FindProperty(JToken token, string name)
    {
        if (token is JObject obj)
        {
            foreach (var prop in obj.Properties())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) return prop;
                var nested = FindProperty(prop.Value, name);
                if (nested != null) return nested;
            }
        }
        if (token is JArray array)
        {
            foreach (var child in array)
            {
                var nested = FindProperty(child, name);
                if (nested != null) return nested;
            }
        }
        return null;
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
