using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.2.0")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("=== Newtonsoft Runtime Probe 0.2.0 ===");

        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetName().Name == "Newtonsoft.Json");

            if (assembly == null)
                assembly = Assembly.Load("Newtonsoft.Json");

            Log.LogInfo("Assembly: " + assembly.FullName);

            InspectType(assembly, "Newtonsoft.Json.Linq.JObject");
            InspectType(assembly, "Newtonsoft.Json.Linq.JToken");
            InspectType(assembly, "Newtonsoft.Json.Linq.JArray");
            InspectType(assembly, "Newtonsoft.Json.JsonConvert");

            TestParseMethods(assembly);

            Log.LogInfo("=== Probe complete ===");
        }
        catch (Exception ex)
        {
            Log.LogError("Probe failed: " + ex);
        }
    }

    private void InspectType(Assembly assembly, string typeName)
    {
        var type = assembly.GetType(typeName);

        if (type == null)
        {
            Log.LogWarning("Type not found: " + typeName);
            return;
        }

        Log.LogInfo("--- Type: " + type.FullName + " ---");

        foreach (var constructor in type.GetConstructors(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly))
        {
            Log.LogInfo("Constructor: " + constructor);
        }

        foreach (var method in type.GetMethods(
            BindingFlags.Public |
            BindingFlags.Static |
            BindingFlags.Instance |
            BindingFlags.DeclaredOnly))
        {
            if (method.Name.Contains("Parse") ||
                method.Name.Contains("Load") ||
                method.Name.Contains("Read") ||
                method.Name.Contains("FromObject") ||
                method.Name.Contains("Serialize") ||
                method.Name.Contains("Deserialize") ||
                method.Name.Contains("Create"))
            {
                Log.LogInfo("Method: " + method);
            }
        }

        foreach (var property in type.GetProperties(
            BindingFlags.Public |
            BindingFlags.Instance |
            BindingFlags.Static |
            BindingFlags.DeclaredOnly))
        {
            Log.LogInfo("Property: " + property);
        }
    }

    private void TestParseMethods(Assembly assembly)
    {
        const string json =
            "{\"probe\":\"ULTRADROID\",\"number\":123}";

        string[] typeNames =
        {
            "Newtonsoft.Json.Linq.JObject",
            "Newtonsoft.Json.Linq.JToken",
            "Newtonsoft.Json.Linq.JArray"
        };

        foreach (var typeName in typeNames)
        {
            var type = assembly.GetType(typeName);
            if (type == null)
                continue;

            foreach (var method in type.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Parse"))
            {
                var parameters = method.GetParameters();

                Log.LogInfo(
                    "Parse candidate: " + typeName + "." + method);

                if (parameters.Length != 1 ||
                    parameters[0].ParameterType != typeof(string))
                {
                    Log.LogInfo(
                        "Skipped: signature is not Parse(string).");
                    continue;
                }

                try
                {
                    var result = method.Invoke(
                        null, new object[] { json });

                    Log.LogInfo(
                        "PARSE SUCCESS: " +
                        (result?.ToString() ?? "<null>"));
                }
                catch (Exception ex)
                {
                    Log.LogError(
                        "PARSE FAILED: " +
                        (ex.InnerException ?? ex));
                }
            }
        }

        Log.LogInfo(
            "Parse candidate scan finished. " +
            "No success message means no compatible Parse(string) " +
            "method was found or invocation failed.");
    }
}
