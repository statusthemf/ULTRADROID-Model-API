
using Newtonsoft.Json.Linq;

Console.WriteLine("Testing Newtonsoft.Json...");

string json = """
{
    "asset": {
        "version": "2.0",
        "generator": "ULTRADROID"
    },
    "meshes": [
        {
            "name": "TestMesh",
            "primitives": []
        }
    ],
    "unicode": "Ω 🚀"
}
""";

try
{
    JObject root = JObject.Parse(json);

    string? version = (string?)root["asset"]?["version"];
    string? meshName = (string?)root["meshes"]?[0]?["name"];
    string? unicode = (string?)root["unicode"];

    if (version != "2.0" ||
        meshName != "TestMesh" ||
        unicode != "Ω 🚀")
    {
        throw new Exception("A JSON value was incorrect.");
    }

    Console.WriteLine("PASS: Nested JSON");
    Console.WriteLine("PASS: Arrays and objects");
    Console.WriteLine("PASS: Unicode characters");
    Console.WriteLine("All tests passed!");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"TEST FAILED: {ex}");
    Environment.ExitCode = 1;
}
