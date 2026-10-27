using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.3.1")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("=== Newtonsoft Runtime Probe 0.3.1 ===");

        try
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            var assembly = assemblies.FirstOrDefault(a =>
                a.GetName().Name == "Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json");

            Log.LogInfo("Assembly: " + assembly.FullName);

            InspectTextReaders();
            InspectJsonReader(assembly);

            Log.LogInfo("=== Probe complete ===");
        }
        catch (Exception ex)
        {
            Log.LogError("Probe failed: " + ex);
        }
    }

    private void InspectTextReaders()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            foreach (var typeName in new[]
            {
                "Il2CppSystem.IO.StringReader",
                "Il2CppSystem.IO.TextReader",
                "Il2CppSystem.IO.StreamReader",
                "Il2CppSystem.IO.MemoryStream",
                "Il2CppSystem.IO.Stream"
            })
            {
                var type = assembly.GetType(typeName);
                if (type == null)
                    continue;

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
                    if (method.Name == "Read" ||
                        method.Name == "ReadToEnd" ||
                        method.Name == "Close" ||
                        method.Name == "Dispose")
                    {
                        Log.LogInfo("Method: " + method);
                    }
                }
            }
        }
    }

    private void InspectJsonReader(Assembly assembly)
    {
        foreach (var typeName in new[]
        {
            "Newtonsoft.Json.JsonTextReader",
            "Newtonsoft.Json.JsonReader",
            "Newtonsoft.Json.Linq.JObject"
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
                    method.Name == "Read" ||
                    method.Name == "ReadFrom" ||
                    method.Name == "Close")
                {
                    Log.LogInfo("Method: " + method);
                }
            }
        }
    }
}
