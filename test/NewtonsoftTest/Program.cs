
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

int passed = 0;
int failed = 0;

void Test(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"PASS: {name}");
        passed++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL: {name} - {ex}");
        failed++;
    }
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

Console.WriteLine("ULTRADROID Newtonsoft.Json DLL Test");
Console.WriteLine($"Assembly: {typeof(JsonConvert).Assembly.FullName}");
Console.WriteLine();

Test("Load Newtonsoft.Json assembly", () =>
{
    Assert(typeof(JsonConvert).Assembly != null,
        "Newtonsoft.Json assembly did not load.");
});

Test("Parse JSON object", () =>
{
    JObject obj = JObject.Parse(
        """{"name":"ULTRADROID","version":1}""");

    Assert((string?)obj["name"] == "ULTRADROID",
        "Incorrect name.");

    Assert((int?)obj["version"] == 1,
        "Incorrect version.");
});

Test("Read nested objects and arrays", () =>
{
    JObject obj = JObject.Parse(
        """{"nodes":[{"name":"Root","children":[1,2]}]}""");

    Assert((string?)obj["nodes"]?[0]?["name"] == "Root",
        "Nested object failed.");

    Assert((int?)obj["nodes"]?[0]?["children"]?[1] == 2,
        "Array reading failed.");
});

Test("Handle Unicode and emoji", () =>
{
    JObject obj = JObject.Parse(
        """{"text":"Omega: \u03A9, Rocket: \uD83D\uDE80"}""");

    Assert((string?)obj["text"] == "Omega: Ω, Rocket: 🚀",
        "Unicode decoding failed.");
});

Test("Handle null values", () =>
{
    JObject obj = JObject.Parse("""{"value":null}""");

    Assert(obj["value"]?.Type == JTokenType.Null,
        "JSON null was not preserved.");
});

Test("Reject malformed JSON", () =>
{
    bool threw = false;

    try
    {
        JObject.Parse("{ invalid json }");
    }
    catch (JsonReaderException)
    {
        threw = true;
    }

    Assert(threw, "Malformed JSON was accepted.");
});

Test("Serialize and deserialize", () =>
{
    var original = new TestData
    {
        Name = "ULTRADROID",
        Version = 2
    };

    string serialized = JsonConvert.SerializeObject(original);
    TestData? restored =
        JsonConvert.DeserializeObject<TestData>(serialized);

    Assert(restored?.Name == original.Name,
        "Name did not survive round-trip.");

    Assert(restored?.Version == original.Version,
        "Version did not survive round-trip.");
});

Console.WriteLine();
Console.WriteLine($"Results: {passed} passed, {failed} failed.");

if (failed > 0)
{
    Environment.ExitCode = 1;
}

public sealed class TestData
{
    public string Name { get; set; } = "";
    public int Version { get; set; }
}
