using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.6.0")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    private Assembly jsonAssembly = null!;
    private Type jObjectType = null!;
    private Type jTokenType = null!;
    private Type jArrayType = null!;
    private Type jValueType = null!;
    private Type jsonConvertType = null!;

    private int passed;
    private int failed;
    private int skipped;

    public override void Load()
    {
        Log.LogInfo("========================================");
        Log.LogInfo(" ULTRADROID Newtonsoft Probe 0.6.0");
        Log.LogInfo("========================================");

        try
        {
            jsonAssembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetName().Name == "Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json");

            jObjectType = GetJsonType("Newtonsoft.Json.Linq.JObject");
            jTokenType = GetJsonType("Newtonsoft.Json.Linq.JToken");
            jArrayType = GetJsonType("Newtonsoft.Json.Linq.JArray");
            jValueType = GetJsonType("Newtonsoft.Json.Linq.JValue");
            jsonConvertType = GetJsonType("Newtonsoft.Json.JsonConvert");

            Log.LogInfo("Assembly: " + jsonAssembly.FullName);

            Run("01 - Runtime type discovery", TestTypeDiscovery);
            Run("02 - Read string property", TestStringProperty);
            Run("03 - Read integer property", TestIntegerProperty);
            Run("04 - Read Boolean property", TestBooleanProperty);
            Run("05 - Read missing property", TestMissingProperty);
            Run("06 - Parse nested object", TestNestedObject);
            Run("07 - Parse JSON array", TestParsedArray);
            Run("08 - Construct JValue tokens", TestJValues);
            Run("09 - Construct and populate JObject", TestBuildObject);
            Run("10 - Construct and populate JArray", TestBuildArray);
            Run("11 - Serialize parsed JObject", TestSerializeObject);
            Run("12 - Serialize constructed JSON", TestSerializeConstructed);
            Run("13 - Parse-serialize-parse round trip", TestRoundTrip);
            Run("14 - Parse empty object", TestEmptyObject);
            Run("15 - Parse null property", TestNullProperty);
            Run("16 - Parse Unicode string", TestUnicode);
            Run("17 - Reject malformed JSON", TestMalformedJson);
            Run("18 - Deserialize primitive string", TestDeserializeString);
            Run("19 - Deserialize primitive integer", TestDeserializeInteger);
            Run("20 - Deserialize primitive Boolean", TestDeserializeBoolean);
            Run("21 - Deserialize to JObject", TestDeserializeObject);
            Run("22 - Token conversion API inspection", InspectConversions);

            Log.LogInfo("========================================");
            Log.LogInfo(
                "FINAL RESULTS: " + passed + " passed, " +
                failed + " failed, " + skipped + " skipped");
            Log.LogInfo("========================================");
        }
        catch (Exception ex)
        {
            Log.LogError("Probe initialization failed: " + Unwrap(ex));
        }
    }

    private Type GetJsonType(string name)
    {
        return jsonAssembly.GetType(name)
            ?? throw new Exception("Missing type: " + name);
    }

    private void Run(string name, Action action)
    {
        try
        {
            Log.LogInfo("[START] " + name);
            action();
            passed++;
            Log.LogInfo("[PASS] " + name);
        }
        catch (SkipTestException ex)
        {
            skipped++;
            Log.LogWarning("[SKIP] " + name + ": " + ex.Message);
        }
        catch (Exception ex)
        {
            failed++;
            Log.LogError("[FAIL] " + name + ": " + Unwrap(ex));
        }
    }

    private sealed class SkipTestException : Exception
    {
        public SkipTestException(string message) : base(message) { }
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException &&
               ex.InnerException != null)
        {
            ex = ex.InnerException;
        }

        return ex;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private void Skip(string reason)
    {
        throw new SkipTestException(reason);
    }

    private object CreateStringReader(string text)
    {
        var type = FindType("Il2CppSystem.IO.StringReader")
            ?? throw new Exception("IL2CPP StringReader not found.");

        var ctor = type.GetConstructor(new[] { typeof(string) })
            ?? throw new Exception("StringReader(string) missing.");

        return ctor.Invoke(new object[] { text });
    }

    private object CreateJsonReader(string text)
    {
        var readerType = GetJsonType("Newtonsoft.Json.JsonTextReader");
        var textReaderType = FindType("Il2CppSystem.IO.TextReader")
            ?? throw new Exception("IL2CPP TextReader not found.");

        var ctor = readerType.GetConstructor(new[] { textReaderType })
            ?? throw new Exception("JsonTextReader(TextReader) missing.");

        return ctor.Invoke(new[] { CreateStringReader(text) });
    }

    private Type? FindType(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name))
            .FirstOrDefault(t => t != null);
    }

    private object ParseObject(string json)
    {
        var reader = CreateJsonReader(json);

        var method = jObjectType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "Load")
                    return false;

                var p = m.GetParameters();

                return p.Length == 2 &&
                       p[0].ParameterType.IsInstanceOfType(reader);
            })
            ?? throw new Exception("Compatible JObject.Load not found.");

        return method.Invoke(null, new object?[] { reader, null })
            ?? throw new Exception("JObject.Load returned null.");
    }

    private object ParseToken(string json)
    {
        var reader = CreateJsonReader(json);

        var method = jTokenType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "ReadFrom")
                    return false;

                var p = m.GetParameters();

                return p.Length == 1 &&
                       p[0].ParameterType.IsInstanceOfType(reader);
            });

        if (method != null)
        {
            return method.Invoke(null, new[] { reader })
                ?? throw new Exception("JToken.ReadFrom returned null.");
        }

        var load = jTokenType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "ReadFrom")
                    return false;

                var p = m.GetParameters();

                return p.Length == 2 &&
                       p[0].ParameterType.IsInstanceOfType(reader);
            });

        if (load == null)
            throw new Exception("No compatible JToken.ReadFrom found.");

        return load.Invoke(null, new object?[] { reader, null })
            ?? throw new Exception("JToken.ReadFrom returned null.");
    }

    private object? GetProperty(object obj, string name)
    {
        var indexer = jObjectType.GetProperty(
            "Item", new[] { typeof(string) })
            ?? throw new Exception("JObject string indexer missing.");

        return indexer.GetValue(obj, new object[] { name });
    }

    private object? GetArrayItem(object obj, int index)
    {
        var indexer = jArrayType.GetProperty(
            "Item", new[] { typeof(int) })
            ?? throw new Exception("JArray integer indexer missing.");

        return indexer.GetValue(obj, new object[] { index });
    }

    private object? GetTokenValue(object token)
    {
        var type = token.GetType();

        var property = type.GetProperty(
            "Value",
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.FlattenHierarchy);

        if (property != null && property.GetIndexParameters().Length == 0)
        {
            try
            {
                return property.GetValue(token);
            }
            catch
            {
                // Try other token value APIs below.
            }
        }

        var valueMethod = type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == "get_Value" &&
                m.GetParameters().Length == 0);

        if (valueMethod != null)
            return valueMethod.Invoke(token, null);

        return null;
    }

    private string DescribeToken(object? token)
    {
        if (token == null)
            return "<null reference>";

        string typeName = token.GetType().FullName ?? token.GetType().Name;
        object? value = GetTokenValue(token);

        string valueText;

        try
        {
            valueText = value == null ? "<null>" : value.ToString() ?? "<null>";
        }
        catch (Exception ex)
        {
            valueText = "<value conversion failed: " +
                        Unwrap(ex).GetType().Name + ">";
        }

        string tokenText;

        try
        {
            tokenText = token.ToString() ?? "<null>";
        }
        catch
        {
            tokenText = "<ToString failed>";
        }

        return "wrapper=" + typeName +
               "; valueType=" + (value?.GetType().FullName ?? "<null>") +
               "; value=" + valueText +
               "; tokenToString=" + tokenText;
    }

    private void LogToken(string label, object? token)
    {
        Log.LogInfo(label + ": " + DescribeToken(token));
    }

    private string Serialize(object token)
    {
        var method = jsonConvertType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "SerializeObject")
                    return false;

                var p = m.GetParameters();

                return p.Length == 1 &&
                       p[0].ParameterType.IsInstanceOfType(token);
            })
            ?? throw new Exception(
                "Compatible JsonConvert.SerializeObject overload missing.");

        return method.Invoke(null, new[] { token })?.ToString()
            ?? throw new Exception("Serialization returned null.");
    }

    private object CreateJValue(string value)
    {
        var ctor = jValueType.GetConstructor(new[] { typeof(string) })
            ?? throw new Exception("JValue(string) missing.");

        return ctor.Invoke(new object[] { value });
    }

    private object CreateJValue(long value)
    {
        var ctor = jValueType.GetConstructor(new[] { typeof(long) })
            ?? throw new Exception("JValue(long) missing.");

        return ctor.Invoke(new object[] { value });
    }

    private object CreateJValue(bool value)
    {
        var ctor = jValueType.GetConstructor(new[] { typeof(bool) })
            ?? throw new Exception("JValue(bool) missing.");

        return ctor.Invoke(new object[] { value });
    }

    private void AddObjectProperty(object obj, string name, object token)
    {
        var method = jObjectType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "Add")
                    return false;

                var p = m.GetParameters();

                return p.Length == 2 &&
                       p[0].ParameterType == typeof(string) &&
                       p[1].ParameterType.IsInstanceOfType(token);
            })
            ?? throw new Exception("Compatible JObject.Add missing.");

        method.Invoke(obj, new[] { (object)name, token });
    }

    private void AddArrayItem(object array, object token)
    {
        var method = jArrayType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
            {
                if (m.Name != "Add")
                    return false;

                var p = m.GetParameters();

                return p.Length == 1 &&
                       p[0].ParameterType.IsInstanceOfType(token);
            })
            ?? throw new Exception("Compatible JArray.Add missing.");

        method.Invoke(array, new[] { token });
    }

    private object CreateJObject()
    {
        return Activator.CreateInstance(jObjectType)
            ?? throw new Exception("JObject construction failed.");
    }

    private object CreateJArray()
    {
        return Activator.CreateInstance(jArrayType)
            ?? throw new Exception("JArray construction failed.");
    }

    private void TestTypeDiscovery()
    {
        Check(jObjectType != null, "JObject missing.");
        Check(jTokenType != null, "JToken missing.");
        Check(jArrayType != null, "JArray missing.");
        Check(jValueType != null, "JValue missing.");
        Check(jsonConvertType != null, "JsonConvert missing.");

        Log.LogInfo("All required JSON types were found.");
    }

    private void TestStringProperty()
    {
        var obj = ParseObject("{\"probe\":\"ULTRADROID\"}");
        var token = GetProperty(obj, "probe");

        Check(token != null, "probe token missing.");
        LogToken("String token", token);
    }

    private void TestIntegerProperty()
    {
        var obj = ParseObject("{\"number\":123}");
        var token = GetProperty(obj, "number");

        Check(token != null, "number token missing.");
        LogToken("Integer token", token);
    }

    private void TestBooleanProperty()
    {
        var obj = ParseObject("{\"enabled\":true}");
        var token = GetProperty(obj, "enabled");

        Check(token != null, "enabled token missing.");
        LogToken("Boolean token", token);
    }

    private void TestMissingProperty()
    {
        var obj = ParseObject("{\"present\":1}");
        var token = GetProperty(obj, "missing");

        Check(token == null, "Missing property unexpectedly returned a token.");
        Log.LogInfo("Missing property correctly returned null.");
    }

    private void TestNestedObject()
    {
        var obj = ParseObject(
            "{\"outer\":{\"inner\":{\"value\":42}}}");

        var outer = GetProperty(obj, "outer");
        Check(outer != null, "outer missing.");

        var inner = GetProperty(outer!, "inner");
        Check(inner != null, "inner missing.");

        var value = GetProperty(inner!, "value");
        Check(value != null, "nested value missing.");

        LogToken("Nested value", value);
    }

    private void TestParsedArray()
    {
        var token = ParseToken("[\"alpha\",7,true,null]");

        LogToken("Parsed array token", token);

        var array = token;
        var first = GetArrayItem(array, 0);
        var second = GetArrayItem(array, 1);
        var third = GetArrayItem(array, 2);
        var fourth = GetArrayItem(array, 3);

        LogToken("array[0]", first);
        LogToken("array[1]", second);
        LogToken("array[2]", third);
        LogToken("array[3]", fourth);

        Check(first != null, "First array item missing.");
        Check(second != null, "Second array item missing.");
        Check(third != null, "Third array item missing.");
    }

    private void TestJValues()
    {
        LogToken("String JValue", CreateJValue("hello"));
        LogToken("Integer JValue", CreateJValue(42L));
        LogToken("Boolean JValue", CreateJValue(true));
    }

    private void TestBuildObject()
    {
        var obj = CreateJObject();

        AddObjectProperty(obj, "name", CreateJValue("ULTRADROID"));
        AddObjectProperty(obj, "version", CreateJValue(6L));
        AddObjectProperty(obj, "enabled", CreateJValue(true));

        Log.LogInfo("Constructed object JSON: " + Serialize(obj));

        Check(GetProperty(obj, "name") != null, "Constructed name missing.");
        Check(GetProperty(obj, "version") != null, "Constructed version missing.");
        Check(GetProperty(obj, "enabled") != null, "Constructed enabled missing.");
    }

    private void TestBuildArray()
    {
        var array = CreateJArray();

        AddArrayItem(array, CreateJValue("first"));
        AddArrayItem(array, CreateJValue(2L));
        AddArrayItem(array, CreateJValue(false));

        Log.LogInfo("Constructed array JSON: " + Serialize(array));

        LogToken("Constructed array[0]", GetArrayItem(array, 0));
        LogToken("Constructed array[1]", GetArrayItem(array, 1));
        LogToken("Constructed array[2]", GetArrayItem(array, 2));
    }

    private void TestSerializeObject()
    {
        var obj = ParseObject("{\"message\":\"hello\",\"count\":42}");
        var json = Serialize(obj);

        Log.LogInfo("Serialized: " + json);

        Check(json.Contains("\"message\""), "Serialized message key missing.");
        Check(json.Contains("\"count\""), "Serialized count key missing.");
    }

    private void TestSerializeConstructed()
    {
        var obj = CreateJObject();

        AddObjectProperty(obj, "test", CreateJValue("passed"));

        var json = Serialize(obj);

        Log.LogInfo("Constructed JSON: " + json);

        Check(json.Contains("\"test\""), "Serialized key missing.");
        Check(json.Contains("passed"), "Serialized value missing.");
    }

    private void TestRoundTrip()
    {
        const string original =
            "{\"name\":\"ULTRADROID\",\"count\":42,\"enabled\":true}";

        var first = ParseObject(original);
        var serialized = Serialize(first);
        var second = ParseObject(serialized);

        Log.LogInfo("Round-trip output: " + serialized);

        Check(GetProperty(second, "name") != null, "name lost in round trip.");
        Check(GetProperty(second, "count") != null, "count lost in round trip.");
        Check(GetProperty(second, "enabled") != null, "enabled lost in round trip.");

        LogToken("Round-trip name", GetProperty(second, "name"));
        LogToken("Round-trip count", GetProperty(second, "count"));
        LogToken("Round-trip enabled", GetProperty(second, "enabled"));
    }

    private void TestEmptyObject()
    {
        var obj = ParseObject("{}");
        var json = Serialize(obj);

        Log.LogInfo("Empty object serialized: " + json);
        Check(GetProperty(obj, "anything") == null,
            "Empty object returned an unexpected property.");
    }

    private void TestNullProperty()
    {
        var obj = ParseObject("{\"value\":null}");
        var token = GetProperty(obj, "value");

        Check(token != null, "Null property token was missing.");

        LogToken("Explicit JSON null", token);
    }

    private void TestUnicode()
    {
        const string original = "{\"text\":\"café 東京 🚀\"}";

        var obj = ParseObject(original);
        var token = GetProperty(obj, "text");

        Check(token != null, "Unicode token missing.");

        LogToken("Unicode token", token);

        var serialized = Serialize(obj);
        Log.LogInfo("Unicode serialized: " + serialized);

        var second = ParseObject(serialized);
        Check(GetProperty(second, "text") != null,
            "Unicode property lost after round trip.");
    }

    private void TestMalformedJson()
    {
        try
        {
            ParseObject("{this is not valid JSON");
        }
        catch (Exception ex)
        {
            Log.LogInfo("Malformed JSON rejected: " +
                        Unwrap(ex).GetType().FullName);
            return;
        }

        throw new Exception("Malformed JSON was accepted.");
    }

    private void TestDeserializeString()
    {
        TestGenericDeserialize(typeof(string), "\"hello\"");
    }

    private void TestDeserializeInteger()
    {
        TestGenericDeserialize(typeof(long), "123");
    }

    private void TestDeserializeBoolean()
    {
        TestGenericDeserialize(typeof(bool), "true");
    }

    private void TestGenericDeserialize(Type targetType, string json)
    {
        var method = jsonConvertType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "DeserializeObject" &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(string));

        if (method == null)
        {
            Skip("Generic DeserializeObject<T>(string) not exposed.");
            return;
        }

        MethodInfo closed;

        try
        {
            closed = method.MakeGenericMethod(targetType);
        }
        catch (Exception ex)
        {
            Skip("Could not close generic method: " + Unwrap(ex).Message);
            return;
        }

        object? result;

        try
        {
            result = closed.Invoke(null, new object[] { json });
        }
        catch (Exception ex)
        {
            throw new Exception("Generic deserialization invocation failed: " +
                                Unwrap(ex));
        }

        Log.LogInfo(
            "Deserialized " + targetType.FullName + ": " +
            (result?.ToString() ?? "<null>"));
    }

    private void TestDeserializeObject()
    {
        var method = jsonConvertType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "DeserializeObject" ||
                    m.IsGenericMethodDefinition)
                    return false;

                var p = m.GetParameters();

                return p.Length == 3 &&
                       p[0].ParameterType == typeof(string) &&
                       p[1].ParameterType.FullName == "Il2CppSystem.Type";
            });

        if (method == null)
        {
            Skip("Non-generic DeserializeObject(string, Il2CppSystem.Type, settings) missing.");
            return;
        }

        Skip("JObject deserialization needs a confirmed Il2CppSystem.Type instance; " +
             "we will derive the correct type conversion from the runtime.");
    }

    private void InspectConversions()
    {
        foreach (var type in new[] { jTokenType, jValueType })
        {
            Log.LogInfo("--- Conversion APIs: " + type.FullName + " ---");

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                if (method.Name.Contains("ToObject") ||
                    method.Name.Contains("Value") ||
                    method.Name.Contains("Convert") ||
                    method.Name.Contains("op_"))
                {
                    Log.LogInfo("Conversion: " + method);
                }
            }

            foreach (var property in type.GetProperties(
                BindingFlags.Public | BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            {
                if (property.Name.Contains("Value") ||
                    property.Name == "Type")
                {
                    Log.LogInfo("Value property: " + property);
                }
            }
        }
    }
}
