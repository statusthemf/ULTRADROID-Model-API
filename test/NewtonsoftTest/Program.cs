
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
        Console.WriteLine($"FAIL: {name} - {ex.Message}");
        failed++;
    }
}

void Assert(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

string json = """
{
    "asset": {
        "version": "2.0",
        "generator": "ULTRADROID"
    },
    "scene": 0,
    "scenes": [
        {
            "nodes": [0, 1]
        }
    ],
    "nodes": [
        { "name": "Root", "children": [1] },
        { "name": "MeshNode", "mesh": 0 }
    ],
    "meshes": [
        {
            "name": "TestMesh",
            "primitives": [
                {
                    "attributes": {
                        "POSITION": 0
                    }
                }
            ]
        }
    ],
    "buffers": [
        {
            "byteLength": 1024,
            "uri": "model.bin"
        }
    ],
    "unicode": "Omega: \u03A9, Rocket: \uD83D\uDE80",
    "numbers": [0, -1, 3.14, 1e5],
    "nullable": null
}
""";

JObject root = JObject.Parse(json);

Test("Parse root object", () =>
{
    Assert(root.Type == JTokenType.Object,
        "Root is not an object.");
});

Test("Read glTF version", () =>
{
    Assert((string?)root["asset"]?["version"] == "2.0",
        "Incorrect asset version.");
});

Test("Read nested objects", () =>
{
    Assert((string?)root["nodes"]?[1]?["name"] == "MeshNode",
        "Could not read nested node.");
});

Test("Read arrays", () =>
{
    Assert((int?)root["scenes"]?[0]?["nodes"]?[1] == 1,
        "Incorrect array value.");
});

Test("Read mesh attributes", () =>
{
    Assert(
        (int?)root["meshes"]?[0]?["primitives"]?[0]
            ?["attributes"]?["POSITION"] == 0,
        "Incorrect POSITION accessor.");
});

Test("Read buffer properties", () =>
{
    Assert((int?)root["buffers"]?[0]?["byteLength"] == 1024,
        "Incorrect buffer length.");

    Assert((string?)root["buffers"]?[0]?["uri"] == "model.bin",
        "Incorrect buffer URI.");
});

Test("Decode Unicode and emoji", () =>
{
    Assert((string?)root["unicode"] == "Omega: Ω, Rocket: 🚀",
        "Unicode decoding failed.");
});

Test("Read numeric values", () =>
{
    Assert((int?)root["numbers"]?[1] == -1,
        "Negative integer failed.");

    Assert((double?)root["numbers"]?[2] == 3.14,
        "Decimal failed.");

    Assert((double?)root["numbers"]?[3] == 100000,
        "Scientific notation failed.");
});

Test("Handle JSON null", () =>
{
    Assert(root["nullable"]?.Type == JTokenType.Null,
        "Null was not preserved.");
});

Test("Detect missing properties", () =>
{
    Assert(root["propertyThatDoesNotExist"] == null,
        "Missing property was unexpectedly found.");
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

Test("Detect wrong property types", () =>
{
    JToken? version = root["asset"]?["version"];

    Assert(version?.Type == JTokenType.String,
        "Version should be a string.");

    Assert(
        root["buffers"]?[0]?["byteLength"]?.Type
            == JTokenType.Integer,
        "byteLength should be an integer.");
});

Console.WriteLine();
Console.WriteLine($"Results: {passed} passed, {failed} failed.");

if (failed > 0)
{
    Environment.ExitCode = 1;
}
