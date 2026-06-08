using System.IO;
using Vcad.Core.Validation;
using Xunit;

namespace Vcad.Core.Tests;

public class SampleAndSchemaTests
{
    [Fact]
    public void Repo_sample_demo_json_parses_and_validates()
    {
        var path = FindRepoFile("samples/commands/demo_rectangle_text.json");
        var json = File.ReadAllText(path);
        var r = DslValidator.ParseAndValidate(json);
        Assert.True(r.IsValid, "expected sample to validate, got: " + r.ErrorMessage);
        Assert.NotNull(r.Request);
        Assert.True(r.Request!.Commands.Count >= 2);
    }

    [Fact]
    public void Dsl_schema_file_exists_and_is_json()
    {
        var path = FindRepoFile("schema/vcad_dsl_v1.schema.json");
        var json = File.ReadAllText(path);
        Newtonsoft.Json.Linq.JObject.Parse(json); // throws on invalid JSON
    }

    [Fact]
    public void Result_schema_file_exists_and_is_json()
    {
        var path = FindRepoFile("schema/vcad_result_v1.schema.json");
        var json = File.ReadAllText(path);
        Newtonsoft.Json.Linq.JObject.Parse(json);
    }

    private static string FindRepoFile(string relative)
    {
        var dir = Path.GetFullPath(Directory.GetCurrentDirectory());
        for (int i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
            var parent = Directory.GetParent(dir);
            if (parent == null) break;
            dir = parent.FullName;
        }
        throw new FileNotFoundException("Cannot locate " + relative + " from CWD upward.");
    }
}
