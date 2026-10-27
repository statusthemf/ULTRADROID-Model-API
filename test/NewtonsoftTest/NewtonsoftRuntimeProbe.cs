using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.9.0")]
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
        Log.LogInfo(" ULTRADROID Newtonsoft Probe 0.9.0");
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
            Run("06 - IL2CPP proxy casting", TestProxyCasting);
            Run("07 - Parse nested object", TestNestedObject);
            Run("08 - Parse JSON array", TestParsedArray);
            Run("09 - Construct JValue tokens", TestJValues);
            Run("10 - Construct and populate JObject", TestBuildObject);
            Run("11 - Construct and populate JArray", TestBuildArray);
            Run("12 - Serialize parsed JObject", TestSerializeObject);
            Run("13 - Serialize constructed JSON", TestSerializeConstructed);
            Run("14 - Parse-serialize-parse round trip", TestRoundTrip);
            Run("15 - Parse empty object", TestEmptyObject);
            Run("16 - Parse null property", TestNullProperty);
            Run("17 - Parse Unicode string", TestUnicode);
            Run("18 - Reject malformed JSON", TestMalformedJson);
            Run("19 - Deserialize primitive string", TestDeserializeString);
            Run("20 - Deserialize primitive integer", TestDeserializeInteger);
            Run("21 - Deserialize primitive Boolean", TestDeserializeBoolean);
            Run("22 - Deserialize to JObject", TestDeserializeObject);
            Run("23 - Token conversion API inspection", InspectConversions);

            Log.LogInfo("========================================");
            Log.LogInfo(
                "FINAL RESULTS: " + passed + " passed, " +
                failed + " failed, " + skipped + " skipped");
            Log.LogInfo("========================================");
        }
        catch (Exception ex)
        {
            Log.LogError(
                "Probe initialization failed: " + Unwrap(ex));
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

    private Type? FindType(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType(name))
            .FirstOrDefault(t => t != null);
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

        method = jTokenType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "ReadFrom")
                    return false;

                var p = m.GetParameters();

                return p.Length == 2 &&
                       p[0].ParameterType.IsInstanceOfType(reader);
            });

        if (method == null)
            throw new Exception("No compatible JToken.ReadFrom found.");

        return method.Invoke(null, new object?[] { reader, null })
            ?? throw new Exception("JToken.ReadFrom returned null.");
    }

    private object? TryCastProxy(object? proxy, Type targetType)
    {
        if (proxy == null)
            return null;

        if (targetType.IsInstanceOfType(proxy))
            return proxy;

        for (Type? current = proxy.GetType();
             current != null;
             current = current.BaseType)
        {
            foreach (var method in current.GetMethods(
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Instance |
                BindingFlags.FlattenHierarchy))
            {
                if (method.Name != "Cast" &&
                    method.Name != "TryCast")
                    continue;

                if (!method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1 ||
                    method.GetParameters().Length != 0)
                    continue;

                try
                {
                    var cast = method.MakeGenericMethod(targetType);
                    var result = cast.Invoke(proxy, null);

                    if (result != null &&
                        targetType.IsInstanceOfType(result))
                        return result;
                }
                catch (Exception ex)
                {
                    Log.LogDebug(
                        method.Name + "<" + targetType.Name + ">: " +
                        Unwrap(ex).Message);
                }
            }
        }

        return null;
    }

    private MethodInfo FindIndexerGetter(
        object obj,
        Type parameterType)
    {
        Type? current = obj.GetType();

        while (current != null)
        {
            var getter = current.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "get_Item")
                        return false;

                    var parameters = m.GetParameters();

                    return parameters.Length == 1 &&
                           parameters[0].ParameterType == parameterType;
                });

            if (getter != null)
                return getter;

            current = current.BaseType;
        }

        throw new Exception(
            "Indexer getter for " + parameterType.FullName +
            " not found on " + obj.GetType().FullName);
    }

    private object? GetProperty(object obj, string name)
    {
        var typedObject = TryCastProxy(obj, jObjectType);

        if (typedObject == null)
            throw new Exception("Could not cast token proxy to JObject.");

        return FindIndexerGetter(typedObject, typeof(string))
            .Invoke(typedObject, new object[] { name });
    }

    private object? GetArrayItem(object obj, int index)
    {
        var typedArray = TryCastProxy(obj, jArrayType);

        if (typedArray == null)
            throw new Exception("Could not cast token proxy to JArray.");

        return FindIndexerGetter(typedArray, typeof(int))
            .Invoke(typedArray, new object[] { index });
    }

    private object? GetTokenValue(object token)
    {
        var typedValue = TryCastProxy(token, jValueType);

        if (typedValue == null)
            return null;

        var getter = jValueType.GetMethods(
                BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m =>
                m.Name == "get_Value" &&
                m.GetParameters().Length == 0);

        if (getter == null)
            throw new Exception("JValue.get_Value() not found.");

        return getter.Invoke(typedValue, null);
    }

    private object ConvertToken(object token, Type targetType)
    {
        var conversion = jTokenType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "op_Explicit" &&
                m.ReturnType == targetType &&
                m.GetParameters().Length == 1);

        if (conversion == null)
        {
            throw new Exception(
                "Explicit token conversion to " +
                targetType.FullName + " not found.");
        }

        try
        {
            return conversion.Invoke(null, new[] { token })
                ?? throw new Exception(
                    "Conversion returned null for " +
                    targetType.FullName);
        }
        catch (Exception ex)
        {
            throw new Exception(
                "Conversion to " + targetType.FullName +
                " failed: " + Unwrap(ex));
        }
    }

    private void LogToken(string label, object? token)
    {
        if (token == null)
        {
            Log.LogInfo(label + ": <null reference>");
            return;
        }

        object? value = GetTokenValue(token);

        Log.LogInfo(
            label +
            ": wrapper=" + token.GetType().FullName +
            "; valueType=" + (value?.GetType().FullName ?? "<null>") +
            "; value=" + (value?.ToString() ?? "<null>"));
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
                "Compatible SerializeObject overload missing.");

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

        Log.LogInfo("All required JSON types found.");
    }

    private void TestStringProperty()
    {
        var obj = ParseObject("{\"probe\":\"ULTRADROID\"}");
        var token = GetProperty(obj, "probe");

        Check(token != null, "probe token missing.");

        var value = ConvertToken(token!, typeof(string));

        Log.LogInfo("Extracted string: " + value);

        Check((string)value == "ULTRADROID",
            "String value did not match.");
    }

    private void TestIntegerProperty()
    {
        var obj = ParseObject("{\"number\":123}");
        var token = GetProperty(obj, "number");

        Check(token != null, "number token missing.");

        var value = ConvertToken(token!, typeof(long));

        Log.LogInfo("Extracted integer: " + value);

        Check((long)value == 123L,
            "Integer value did not match.");
    }

    private void TestBooleanProperty()
    {
        var obj = ParseObject("{\"enabled\":true}");
        var token = GetProperty(obj, "enabled");

        Check(token != null, "enabled token missing.");

        var value = ConvertToken(token!, typeof(bool));

        Log.LogInfo("Extracted Boolean: " + value);

        Check((bool)value,
            "Boolean value did not match.");
    }

    private void TestMissingProperty()
    {
        var obj = ParseObject("{\"present\":1}");
        var token = GetProperty(obj, "missing");

        Check(token == null,
            "Missing property unexpectedly returned a token.");

        Log.LogInfo("Missing property returned null.");
    }

    private void TestProxyCasting()
    {
        var objectToken = ParseToken("{\"value\":42}");
        var arrayToken = ParseToken("[\"alpha\",7]");

        var typedObject = TryCastProxy(objectToken, jObjectType);
        var typedArray = TryCastProxy(arrayToken, jArrayType);

        Check(typedObject != null, "JObject proxy cast failed.");
        Check(typedArray != null, "JArray proxy cast failed.");

        Log.LogInfo("JObject cast: " + typedObject!.GetType().FullName);
        Log.LogInfo("JArray cast: " + typedArray!.GetType().FullName);

        var objectValue = GetProperty(typedObject, "value");
        var arrayValue = GetArrayItem(typedArray, 0);

        Check(objectValue != null, "JObject indexer returned null.");
        Check(arrayValue != null, "JArray indexer returned null.");

        Check(
            (long)ConvertToken(objectValue!, typeof(long)) == 42L,
            "JObject indexer value incorrect.");

        Check(
            (string)ConvertToken(arrayValue!, typeof(string)) == "alpha",
            "JArray indexer value incorrect.");
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

        var number = ConvertToken(value!, typeof(long));

        Check((long)number == 42L, "Nested number was incorrect.");

        Log.LogInfo("Nested value extracted: " + number);
    }

    private void TestParsedArray()
    {
        var array = ParseToken("[\"alpha\",7,true,null]");

        Check(GetArrayItem(array, 0) != null, "Array item 0 missing.");
        Check(GetArrayItem(array, 1) != null, "Array item 1 missing.");
        Check(GetArrayItem(array, 2) != null, "Array item 2 missing.");
        Check(GetArrayItem(array, 3) != null, "Array item 3 missing.");

        var first = ConvertToken(GetArrayItem(array, 0)!, typeof(string));
        var second = ConvertToken(GetArrayItem(array, 1)!, typeof(long));
        var third = ConvertToken(GetArrayItem(array, 2)!, typeof(bool));

        Check((string)first == "alpha", "Array string incorrect.");
        Check((long)second == 7L, "Array integer incorrect.");
        Check((bool)third, "Array Boolean incorrect.");

        Log.LogInfo(
            "Array values: " + first + ", " + second + ", " + third);
        Log.LogInfo("Array JSON: " + Serialize(array));
    }

    private void TestJValues()
    {
        var text = CreateJValue("hello");
        var number = CreateJValue(42L);
        var boolean = CreateJValue(true);

        LogToken("String JValue", text);
        LogToken("Integer JValue", number);
        LogToken("Boolean JValue", boolean);

        Check((string)ConvertToken(text, typeof(string)) == "hello",
            "JValue string conversion failed.");

        Check((long)ConvertToken(number, typeof(long)) == 42L,
            "JValue integer conversion failed.");

        Check((bool)ConvertToken(boolean, typeof(bool)),
            "JValue Boolean conversion failed.");
    }

    private void TestBuildObject()
    {
        var obj = CreateJObject();

        AddObjectProperty(obj, "name", CreateJValue("ULTRADROID"));
        AddObjectProperty(obj, "version", CreateJValue(7L));
        AddObjectProperty(obj, "enabled", CreateJValue(true));

        var json = Serialize(obj);
        Log.LogInfo("Constructed object JSON: " + json);

        Check(json.Contains("\"name\""), "name key missing.");
        Check(json.Contains("\"ULTRADROID\""), "name value missing.");
        Check(json.Contains("\"version\""), "version key missing.");
        Check(json.Contains("\"enabled\""), "enabled key missing.");
    }

    private void TestBuildArray()
    {
        var array = CreateJArray();

        AddArrayItem(array, CreateJValue("first"));
        AddArrayItem(array, CreateJValue(2L));
        AddArrayItem(array, CreateJValue(false));

        var json = Serialize(array);
        Log.LogInfo("Constructed array JSON: " + json);

        Check(json.Contains("\"first\""), "String array item missing.");
        Check(json.Contains("2"), "Integer array item missing.");
        Check(json.Contains("false"), "Boolean array item missing.");
    }

    private void TestSerializeObject()
    {
        var obj = ParseObject("{\"message\":\"hello\",\"count\":42}");
        var json = Serialize(obj);

        Log.LogInfo("Serialized: " + json);

        Check(json.Contains("\"message\""), "message key missing.");
        Check(json.Contains("\"hello\""), "message value missing.");
        Check(json.Contains("\"count\""), "count key missing.");
        Check(json.Contains("42"), "count value missing.");
    }

    private void TestSerializeConstructed()
    {
        var obj = CreateJObject();

        AddObjectProperty(obj, "test", CreateJValue("passed"));

        var json = Serialize(obj);
        Log.LogInfo("Constructed JSON: " + json);

        Check(json.Contains("\"test\""), "Serialized key missing.");
        Check(json.Contains("\"passed\""), "Serialized value missing.");
    }

    private void TestRoundTrip()
    {
        const string original =
            "{\"name\":\"ULTRADROID\",\"count\":42,\"enabled\":true}";

        var first = ParseObject(original);
        var serialized = Serialize(first);
        var second = ParseObject(serialized);

        Log.LogInfo("Round-trip output: " + serialized);

        var name = ConvertToken(GetProperty(second, "name")!,
            typeof(string));
        var count = ConvertToken(GetProperty(second, "count")!,
            typeof(long));
        var enabled = ConvertToken(GetProperty(second, "enabled")!,
            typeof(bool));

        Check((string)name == "ULTRADROID", "Round-trip name incorrect.");
        Check((long)count == 42L, "Round-trip count incorrect.");
        Check((bool)enabled, "Round-trip enabled incorrect.");
    }

    private void TestEmptyObject()
    {
        var obj = ParseObject("{}");
        var json = Serialize(obj);

        Log.LogInfo("Empty object serialized: " + json);

        Check(json == "{}", "Empty object did not serialize as {}.");
        Check(GetProperty(obj, "anything") == null,
            "Unexpected property in empty object.");
    }

    private void TestNullProperty()
    {
        var obj = ParseObject("{\"value\":null}");
        var token = GetProperty(obj, "value");

        Check(token != null, "Explicit null token missing.");

        LogToken("Explicit JSON null", token);

        var json = Serialize(obj);
        Check(json.Contains("\"value\""), "Null property key missing.");
        Check(json.Contains("null"), "Null value missing from JSON.");

        Log.LogInfo("Null property JSON: " + json);
    }

    private void TestUnicode()
    {
        const string original = "{\"text\":\"café 東京 🚀\"}";

        var obj = ParseObject(original);
        var token = GetProperty(obj, "text");

        Check(token != null, "Unicode token missing.");

        var value = ConvertToken(token!, typeof(string));

        Check((string)value == "café 東京 🚀",
            "Unicode string did not match.");

        var serialized = Serialize(obj);
        Log.LogInfo("Unicode serialized: " + serialized);

        var second = ParseObject(serialized);
        var secondValue = ConvertToken(
            GetProperty(second, "text")!, typeof(string));

        Check((string)secondValue == "café 東京 🚀",
            "Unicode value changed during round trip.");
    }

    private void TestMalformedJson()
    {
        try
        {
            ParseObject("{this is not valid JSON");
        }
        catch (Exception ex)
        {
            Log.LogInfo(
                "Malformed JSON rejected: " +
                Unwrap(ex).GetType().FullName);
            return;
        }

        throw new Exception("Malformed JSON was accepted.");
    }

    private void TestDeserializeString()
    {
        TestGenericDeserialize(typeof(string), "\"hello\"", "hello");
    }

    private void TestDeserializeInteger()
    {
        TestGenericDeserialize(typeof(long), "123", "123");
    }

    private void TestDeserializeBoolean()
    {
        TestGenericDeserialize(typeof(bool), "true", "True");
    }

    private void TestGenericDeserialize(
        Type targetType,
        string json,
        string expected)
    {
        var method = jsonConvertType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "DeserializeObject" &&
                m.IsGenericMethodDefinition &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(string));

        if (method == null)
            Skip("Generic DeserializeObject<T>(string) missing.");

        MethodInfo closed;

        try
        {
            closed = method!.MakeGenericMethod(targetType);
        }
        catch (Exception ex)
        {
            Skip("Could not close generic method: " +
                 Unwrap(ex).Message);
            return;
        }

        var result = closed.Invoke(null, new object[] { json });
        var text = result?.ToString() ?? "<null>";

        Log.LogInfo("Deserialized " + targetType.FullName + ": " + text);

        Check(text == expected,
            "Expected '" + expected + "', received '" + text + "'.");
    }

    private void TestDeserializeObject()
    {
        const string json = "{\"name\":\"ULTRADROID\",\"version\":9}";

        var il2CppType = FindType("Il2CppSystem.Type")
            ?? throw new Exception("Il2CppSystem.Type not found.");

        var fromSystemType = il2CppType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
                m.Name == "GetType" &&
                m.GetParameters().Length == 1 &&
                m.GetParameters()[0].ParameterType == typeof(string));

        if (fromSystemType == null)
        {
            Skip("Il2CppSystem.Type.GetType(string) not found.");
            return;
        }

        var targetType = fromSystemType.Invoke(
            null, new object[] { "Newtonsoft.Json.Linq.JObject" });

        if (targetType == null)
        {
            Skip("Could not resolve JObject as Il2CppSystem.Type.");
            return;
        }

        var deserialize = jsonConvertType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
            .FirstOrDefault(m =>
            {
                if (m.Name != "DeserializeObject" ||
                    m.IsGenericMethodDefinition)
                    return false;

                var p = m.GetParameters();

                return p.Length == 3 &&
                       p[0].ParameterType == typeof(string) &&
                       p[1].ParameterType == il2CppType;
            });

        if (deserialize == null)
        {
            Skip("DeserializeObject(string, Il2CppSystem.Type, settings) missing.");
            return;
        }

        var result = deserialize.Invoke(
            null, new object?[] { json, targetType, null });

        Check(result != null, "JObject deserialization returned null.");

        var typedObject = TryCastProxy(result, jObjectType);

        Check(typedObject != null, "Result could not be cast to JObject.");

        var name = ConvertToken(
            GetProperty(typedObject!, "name")!,
            typeof(string));

        var version = ConvertToken(
            GetProperty(typedObject!, "version")!,
            typeof(long));

        Check((string)name == "ULTRADROID", "Deserialized name incorrect.");
        Check((long)version == 9L, "Deserialized version incorrect.");

        Log.LogInfo("JObject deserialization: " + Serialize(typedObject!));
    }

    private void InspectConversions()
    {
        foreach (var type in new[] { jTokenType, jValueType })
        {
            Log.LogInfo("--- Conversion APIs: " + type.FullName + " ---");

            foreach (var method in type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.DeclaredOnly))
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
                BindingFlags.Public |
                BindingFlags.Instance |
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
