using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.5.0")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    private Assembly? jsonAssembly;
    private Type? jObjectType;
    private Type? jTokenType;
    private Type? jArrayType;
    private Type? jsonConvertType;
    private int passed;
    private int failed;
    private int skipped;

    public override void Load()
    {
        Log.LogInfo("========================================");
        Log.LogInfo(" ULTRADROID Newtonsoft Probe 0.5.0");
        Log.LogInfo("========================================");

        try
        {
            jsonAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetName().Name == "Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json");

            jObjectType = jsonAssembly.GetType(
                "Newtonsoft.Json.Linq.JObject");
            jTokenType = jsonAssembly.GetType(
                "Newtonsoft.Json.Linq.JToken");
            jArrayType = jsonAssembly.GetType(
                "Newtonsoft.Json.Linq.JArray");
            jsonConvertType = jsonAssembly.GetType(
                "Newtonsoft.Json.JsonConvert");

            Log.LogInfo("Assembly: " + jsonAssembly.FullName);

            Test("Type discovery", () =>
            {
                Require(jObjectType, "JObject");
                Require(jTokenType, "JToken");
                Require(jArrayType, "JArray");
                Require(jsonConvertType, "JsonConvert");
            });

            InspectApi();
            TestReaderAndParsing();
            TestTokenConstruction();
            TestArrayConstruction();
            TestSerialization();
            TestDeserialization();
            TestMalformedJson();

            Log.LogInfo("========================================");
            Log.LogInfo(
                $"RESULTS: {passed} passed, {failed} failed, " +
                $"{skipped} skipped");
            Log.LogInfo("========================================");
        }
        catch (Exception ex)
        {
            Log.LogError("Probe initialization failed: " + ex);
        }
    }

    private void Test(string name, Action action)
    {
        try
        {
            Log.LogInfo("[TEST START] " + name);
            action();
            passed++;
            Log.LogInfo("[PASS] " + name);
        }
        catch (Exception ex)
        {
            failed++;
            Log.LogError(
                "[FAIL] " + name + ": " +
                (ex is TargetInvocationException &&
                 ex.InnerException != null
                    ? ex.InnerException
                    : ex));
        }
    }

    private void Skip(string name, string reason)
    {
        skipped++;
        Log.LogWarning("[SKIP] " + name + ": " + reason);
    }

    private static void Require(object? value, string name)
    {
        if (value == null)
            throw new Exception(name + " was not found.");
    }

    private void InspectApi()
    {
        Test("API inventory", () =>
        {
            foreach (var typeName in new[]
            {
                "Newtonsoft.Json.JsonTextReader",
                "Newtonsoft.Json.JsonReader",
                "Newtonsoft.Json.Linq.JObject",
                "Newtonsoft.Json.Linq.JToken",
                "Newtonsoft.Json.Linq.JArray",
                "Newtonsoft.Json.Linq.JValue",
                "Newtonsoft.Json.Linq.JProperty",
                "Newtonsoft.Json.JsonSerializer",
                "Il2CppSystem.IO.StringReader",
                "Il2CppSystem.IO.TextReader"
            })
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(typeName))
                    .FirstOrDefault(t => t != null);

                if (type == null)
                {
                    Log.LogWarning("[API] Missing: " + typeName);
                    continue;
                }

                Log.LogInfo("[API] " + type.FullName);

                foreach (var ctor in type.GetConstructors(
                    BindingFlags.Public | BindingFlags.Instance))
                {
                    Log.LogInfo("  ctor " + ctor);
                }

                foreach (var method in type.GetMethods(
                    BindingFlags.Public |
                    BindingFlags.Static |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly))
                {
                    if (new[]
                    {
                        "Parse", "Load", "ReadFrom", "Create",
                        "FromObject", "ToObject", "Serialize",
                        "Deserialize", "SerializeObject",
                        "DeserializeObject", "WriteTo", "Add",
                        "get_Item", "set_Item"
                    }.Contains(method.Name))
                    {
                        Log.LogInfo("  method " + method);
                    }
                }
            }
        });
    }

    private object CreateStringReader(string text)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Il2CppSystem.IO.StringReader"))
            .FirstOrDefault(t => t != null)
            ?? throw new Exception("IL2CPP StringReader not found.");

        var ctor = type.GetConstructor(new[] { typeof(string) })
            ?? throw new Exception("StringReader(string) not found.");

        return ctor.Invoke(new object[] { text });
    }

    private object CreateJsonReader(string text)
    {
        var assembly = jsonAssembly
            ?? throw new Exception("JSON assembly not initialized.");

        var readerType = assembly.GetType(
            "Newtonsoft.Json.JsonTextReader")
            ?? throw new Exception("JsonTextReader not found.");

        var textReaderType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("Il2CppSystem.IO.TextReader"))
            .FirstOrDefault(t => t != null)
            ?? throw new Exception("IL2CPP TextReader not found.");

        var ctor = readerType.GetConstructor(
            new[] { textReaderType })
            ?? throw new Exception(
                "JsonTextReader(TextReader) not found.");

        return ctor.Invoke(new[] { CreateStringReader(text) });
    }

    private object LoadJObject(object reader)
    {
        var type = jObjectType
            ?? throw new Exception("JObject not found.");

        var method = type.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "Load")
                    return false;

                var p = m.GetParameters();

                return p.Length == 2 &&
                       p[0].ParameterType.IsInstanceOfType(reader);
            })
            ?? throw new Exception(
                "Compatible JObject.Load(reader, settings) not found.");

        return method.Invoke(
            null, new object?[] { reader, null })
            ?? throw new Exception("JObject.Load returned null.");
    }

    private object Parse(string json)
    {
        var reader = CreateJsonReader(json);
        return LoadJObject(reader);
    }

    private object? GetProperty(object jsonObject, string name)
    {
        var type = jObjectType
            ?? throw new Exception("JObject not found.");

        var indexer = type.GetProperty(
            "Item", new[] { typeof(string) })
            ?? throw new Exception("JObject string indexer missing.");

        return indexer.GetValue(jsonObject, new object[] { name });
    }

    private void TestReaderAndParsing()
    {
        Test("Reader creation and JSON parsing", () =>
        {
            const string json =
                "{\"probe\":\"ULTRADROID\",\"number\":123,\"enabled\":true}";

            var parsed = Parse(json);

            Log.LogInfo("Parsed token: " + parsed);

            var probe = GetProperty(parsed, "probe");
            var number = GetProperty(parsed, "number");
            var enabled = GetProperty(parsed, "enabled");

            Require(probe, "probe property");
            Require(number, "number property");
            Require(enabled, "enabled property");

            Log.LogInfo("probe = " + probe);
            Log.LogInfo("number = " + number);
            Log.LogInfo("enabled = " + enabled);
        });
    }

    private void TestTokenConstruction()
    {
        Test("JValue construction", () =>
        {
            var type = jsonAssembly?.GetType(
                "Newtonsoft.Json.Linq.JValue")
                ?? throw new Exception("JValue type not found.");

            var ctor = type.GetConstructor(new[] { typeof(string) })
                ?? throw new Exception("JValue(string) missing.");

            var token = ctor.Invoke(new object[] { "ULTRADROID" });

            Log.LogInfo("JValue result: " + token);
        });
    }

    private void TestArrayConstruction()
    {
        Test("JArray construction", () =>
        {
            var type = jArrayType
                ?? throw new Exception("JArray type missing.");

            var array = Activator.CreateInstance(type)
                ?? throw new Exception("JArray construction failed.");

            Log.LogInfo("Empty JArray: " + array);

            var add = type.GetMethods()
                .FirstOrDefault(m =>
                    m.Name == "Add" &&
                    m.GetParameters().Length == 1);

            if (add == null)
            {
                Skip("JArray.Add", "No one-argument Add method found.");
                return;
            }

            var valueType = jsonAssembly?.GetType(
                "Newtonsoft.Json.Linq.JValue");

            var valueCtor = valueType?.GetConstructor(
                new[] { typeof(string) });

            if (valueCtor == null)
            {
                Skip("JArray.Add", "JValue(string) is unavailable.");
                return;
            }

            var value = valueCtor.Invoke(
                new object[] { "array-item" });

            add.Invoke(array, new[] { value });

            Log.LogInfo("JArray after Add: " + array);
        });
    }

    private void TestSerialization()
    {
        Test("Serialize parsed JObject", () =>
        {
            var parsed = Parse("{\"message\":\"hello\",\"count\":42}");

            var type = jsonConvertType
                ?? throw new Exception("JsonConvert missing.");

            var method = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "SerializeObject")
                        return false;

                    var p = m.GetParameters();

                    return p.Length == 1 &&
                           p[0].ParameterType.IsInstanceOfType(parsed);
                })
                ?? throw new Exception(
                    "Compatible SerializeObject(object) missing.");

            var result = method.Invoke(null, new[] { parsed })?.ToString();

            if (string.IsNullOrEmpty(result))
                throw new Exception("Serialized JSON was empty.");

            Log.LogInfo("Serialized JSON: " + result);
        });
    }

    private void TestDeserialization()
    {
        Test("Deserialize JSON to JObject", () =>
        {
            const string json = "{\"status\":\"ok\",\"value\":7}";

            var type = jsonConvertType
                ?? throw new Exception("JsonConvert missing.");

            var method = type.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "DeserializeObject")
                        return false;

                    var p = m.GetParameters();

                    return p.Length == 3 &&
                           p[0].ParameterType == typeof(string) &&
                           p[1].ParameterType.FullName ==
                               "Il2CppSystem.Type";
                });

            if (method == null)
            {
                Skip(
                    "Deserialize JSON to JObject",
                    "The three-argument IL2CPP overload was not found.");
                return;
            }

            var il2cppType = method.GetParameters()[1].ParameterType;
            var targetType = jObjectType
                ?? throw new Exception("JObject type missing.");

            object? convertedType = null;

            // Try to locate an IL2CPP Type corresponding to JObject.
            foreach (var candidate in AppDomain.CurrentDomain
                .GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                }))
            {
                if (candidate.FullName != targetType.FullName)
                    continue;

                var typeProperty = candidate.GetProperty(
                    "Il2CppType", BindingFlags.Public |
                                  BindingFlags.Static);

                if (typeProperty != null &&
                    il2cppType.IsAssignableFrom(typeProperty.PropertyType))
                {
                    convertedType = typeProperty.GetValue(null);
                    break;
                }
            }

            if (convertedType == null)
            {
                Skip(
                    "Deserialize JSON to JObject",
                    "Could not safely obtain the IL2CPP Type for JObject.");
                return;
            }

            var result = method.Invoke(
                null, new[] { json, convertedType, null });

            Require(result, "Deserialized object");
            Log.LogInfo("Deserialized result: " + result);
        });
    }

    private void TestMalformedJson()
    {
        Test("Malformed JSON rejection", () =>
        {
            try
            {
                Parse("{this is not valid JSON");

                throw new Exception(
                    "Malformed JSON was unexpectedly accepted.");
            }
            catch (TargetInvocationException ex)
                when (ex.InnerException != null)
            {
                Log.LogInfo(
                    "Malformed JSON rejected: " +
                    ex.InnerException.GetType().FullName);
            }
        });
    }
}
