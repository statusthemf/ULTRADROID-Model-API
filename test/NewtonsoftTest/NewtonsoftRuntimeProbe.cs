using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.3.0")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("=== Newtonsoft Runtime Probe 0.3.0 ===");

        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetName().Name == "Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json");

            Log.LogInfo("Assembly: " + assembly.FullName);

            InspectReader(assembly);
            TryParseJson(assembly);

            Log.LogInfo("=== Probe complete ===");
        }
        catch (Exception ex)
        {
            Log.LogError("Probe failed: " + ex);
        }
    }

    private void InspectReader(Assembly assembly)
    {
        foreach (var typeName in new[]
        {
            "Newtonsoft.Json.JsonTextReader",
            "Newtonsoft.Json.JsonReader",
            "Newtonsoft.Json.Linq.JsonLoadSettings"
        })
        {
            var type = assembly.GetType(typeName);

            if (type == null)
            {
                Log.LogWarning("Type not found: " + typeName);
                continue;
            }

            Log.LogInfo("--- " + type.FullName + " ---");

            foreach (var ctor in type.GetConstructors(
                BindingFlags.Public | BindingFlags.Instance))
            {
                Log.LogInfo("Constructor: " + ctor);
            }

            foreach (var method in type.GetMethods(
                BindingFlags.Public |
                BindingFlags.Static |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly))
            {
                if (method.Name == "Load" ||
                    method.Name == "ReadFrom" ||
                    method.Name == "Close" ||
                    method.Name == "Read")
                {
                    Log.LogInfo("Method: " + method);
                }
            }
        }

        var objectType =
            assembly.GetType("Newtonsoft.Json.Linq.JObject");

        if (objectType != null)
        {
            foreach (var method in objectType.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Load"))
            {
                Log.LogInfo("JObject.Load candidate: " + method);
            }
        }
    }

    private void TryParseJson(Assembly assembly)
    {
        const string json =
            "{\"probe\":\"ULTRADROID\",\"number\":123}";

        try
        {
            var readerType =
                assembly.GetType("Newtonsoft.Json.JsonTextReader");

            var objectType =
                assembly.GetType("Newtonsoft.Json.Linq.JObject");

            if (readerType == null || objectType == null)
            {
                Log.LogWarning(
                    "Cannot test: JsonTextReader or JObject is missing.");
                return;
            }

            object? reader = null;

            // First, look for a reader constructor accepting a string.
            var stringCtor = readerType.GetConstructors()
                .FirstOrDefault(c =>
                {
                    var p = c.GetParameters();
                    return p.Length == 1 &&
                           p[0].ParameterType == typeof(string);
                });

            if (stringCtor != null)
            {
                reader = stringCtor.Invoke(new object[] { json });
            }
            else
            {
                // Otherwise, look for a constructor accepting a
                // TextReader and try to create a compatible StringReader.
                var textReaderType =
                    assembly.GetType("Il2CppSystem.IO.TextReader");

                var stringReaderType =
                    AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType(
                            "Il2CppSystem.IO.StringReader"))
                        .FirstOrDefault(t => t != null);

                if (textReaderType != null &&
                    stringReaderType != null)
                {
                    var stringReaderCtor =
                        stringReaderType.GetConstructors()
                            .FirstOrDefault(c =>
                            {
                                var p = c.GetParameters();
                                return p.Length == 1 &&
                                       p[0].ParameterType == typeof(string);
                            });

                    var readerCtor = readerType.GetConstructors()
                        .FirstOrDefault(c =>
                        {
                            var p = c.GetParameters();
                            return p.Length == 1 &&
                                   p[0].ParameterType.IsAssignableFrom(
                                       stringReaderType);
                        });

                    if (stringReaderCtor != null &&
                        readerCtor != null)
                    {
                        var stringReader = stringReaderCtor.Invoke(
                            new object[] { json });

                        reader = readerCtor.Invoke(
                            new[] { stringReader });
                    }
                }
            }

            if (reader == null)
            {
                Log.LogWarning(
                    "Could not construct JsonTextReader. " +
                    "Use the constructor signatures above to choose " +
                    "the correct reader creation path.");
                return;
            }

            Log.LogInfo("JsonTextReader constructed successfully.");

            var loadMethod = objectType.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Load")
                .FirstOrDefault(m =>
                {
                    var p = m.GetParameters();
                    return p.Length >= 1 &&
                           p[0].ParameterType.IsInstanceOfType(reader);
                });

            if (loadMethod == null)
            {
                Log.LogWarning(
                    "No compatible JObject.Load overload found.");
                return;
            }

            var parameters = loadMethod.GetParameters();
            var args = new object?[parameters.Length];
            args[0] = reader;

            for (int i = 1; i < args.Length; i++)
            {
                if (parameters[i].ParameterType.IsValueType &&
                    Nullable.GetUnderlyingType(
                        parameters[i].ParameterType) == null)
                {
                    Log.LogWarning(
                        "Load requires an additional value-type argument: " +
                        parameters[i]);
                    return;
                }

                args[i] = null;
            }

            var result = loadMethod.Invoke(null, args);

            if (result == null)
            {
                Log.LogError("JObject.Load returned null.");
                return;
            }

            Log.LogInfo("JSON PARSE SUCCESS.");
            Log.LogInfo("Parsed object: " + result);

            var indexer = objectType.GetProperty("Item",
                new[] { typeof(string) });

            if (indexer != null)
            {
                var token = indexer.GetValue(
                    result, new object[] { "probe" });

                Log.LogInfo(
                    "Property 'probe': " +
                    (token?.ToString() ?? "<null>"));
            }
            else
            {
                Log.LogWarning(
                    "String indexer not found for JObject.");
            }
        }
        catch (Exception ex)
        {
            Log.LogError(
                "JSON parse test failed: " +
                (ex.InnerException ?? ex));
        }
    }
}
