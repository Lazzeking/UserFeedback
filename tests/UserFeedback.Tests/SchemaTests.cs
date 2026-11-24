using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Xunit;

namespace UserFeedback.Tests;

/// <summary>schemas/userfeedback.schema.json: a "$schema" line for editors doesn't bother the mod</summary>
public class SchemaTests
{
    [Fact]
    public void SchemaLineIsIgnored()
    {
        FeedbackConfigFile file = JsonConvert.DeserializeObject<FeedbackConfigFile>("""
            {
              "$schema": "https://example.com/userfeedback.schema.json",
              "logMatch": ["PolyBiome"]
            }
            """)!;

        Assert.Equal(new[] { "PolyBiome" }, file.LogMatch);
    }

    [Fact]
    public void SchemaIsValidJsonAndKnowsEveryTopLevelField()
    {
        JObject schema = JObject.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "schemas", "userfeedback.schema.json")));
        var properties = (JObject)schema["properties"]!;

        // A field added to FeedbackConfigFile must be added to the schema too, or editors flag it as a mistake
        foreach (System.Reflection.FieldInfo field in typeof(FeedbackConfigFile).GetFields())
        {
            string name = char.ToLowerInvariant(field.Name[0]) + field.Name[1..];
            Assert.True(properties.ContainsKey(name), $"the schema has no \"{name}\"");
        }
    }

    private static string Root()
    {
        string dir = System.AppContext.BaseDirectory;
        while (!System.IO.File.Exists(System.IO.Path.Combine(dir, "UserFeedback.csproj"))) dir = System.IO.Path.GetDirectoryName(dir)!;
        return dir;
    }
}
