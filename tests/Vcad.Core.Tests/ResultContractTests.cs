using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vcad.Core;
using Vcad.Core.Results;
using Xunit;

namespace Vcad.Core.Tests;

public class ResultContractTests
{
    [Fact]
    public void NewFailure_emits_well_formed_envelope()
    {
        var r = VcadResult.NewFailure("req-1", ErrorCodes.SchemaInvalid, "bad");
        var json = JsonConvert.SerializeObject(r);

        var obj = JObject.Parse(json);
        Assert.Equal(DslVersion.ResultCurrent, obj.Value<string>("version"));
        Assert.Equal("req-1", obj.Value<string>("request_id"));
        Assert.False(obj.Value<bool>("success"));
        Assert.NotNull(obj["summary"]);
        Assert.NotNull(obj["results"]);
        Assert.Single(obj["errors"]!);
        Assert.Equal(ErrorCodes.SchemaInvalid, obj["errors"]![0]!["code"]!.Value<string>());
    }

    [Fact]
    public void Successful_result_carries_entity_refs_with_handles()
    {
        var r = new VcadResult
        {
            RequestId = "req-2",
            Success = true,
        };
        r.Summary.Total = 1;
        r.Summary.Succeeded = 1;
        r.Results.Add(new CommandResult
        {
            CommandId = "RECT-001",
            Type = "draw_rectangle",
            Success = true,
            Entities =
            {
                new EntityRef
                {
                    DslId = "RECT-001",
                    EntityType = "Polyline",
                    Handle = "4A2",
                    ObjectId = "8796087349120",
                    Layer = "A-WALL",
                }
            }
        });

        var json = JsonConvert.SerializeObject(r, Formatting.None);
        Assert.Contains("\"handle\":\"4A2\"", json);
        Assert.Contains("\"dsl_id\":\"RECT-001\"", json);
        Assert.Contains("\"layer\":\"A-WALL\"", json);
    }

    [Fact]
    public void Error_code_constants_are_unique()
    {
        var codes = new[]
        {
            ErrorCodes.SchemaInvalid,
            ErrorCodes.CommandNotAllowed,
            ErrorCodes.ParamRange,
            ErrorCodes.LayerInvalid,
            ErrorCodes.EntityNotFound,
            ErrorCodes.AutoCadTransaction,
            ErrorCodes.UnsupportedVersion,
            ErrorCodes.PropertyNotAllowed,
            ErrorCodes.TargetAmbiguous,
        };
        Assert.Equal(codes.Length, codes.Distinct().Count());
        foreach (var c in codes) Assert.StartsWith("E_", c);
    }
}
