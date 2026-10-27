
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.1.0")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("=== Newtonsoft Runtime Probe ===");

        try
        {
            var assembly = AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetName().Name == "Newtonsoft.Json");

            if (assembly == null)
                assembly = Assembly.Load("Newtonsoft.Json");

            Log.LogInfo("Assembly: " + assembly.FullName);

            var jObject = assembly.GetType(
                "Newtonsoft.Json.Linq.JObject");

            if (jObject == null)
            {
                Log.LogError("JObject type not found.");
                return;
            }

            Log.LogInfo("JObject found: " + jObject.FullName);

            foreach (var method in jObject.GetMethods(
                BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Parse"))
            {
                Log.LogInfo("JObject.Parse: " + method);
            }

            var jsonConvert = assembly.GetType(
                "Newtonsoft.Json.JsonConvert");

            if (jsonConvert != null)
            {
                foreach (var method in jsonConvert.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                    .Where(m =>
                        m.Name == "SerializeObject" ||
                        m.Name == "DeserializeObject"))
                {
                    Log.LogInfo("JsonConvert: " + method);
                }
            }

            Log.LogInfo("=== Probe complete ===");
        }
        catch (Exception ex)
        {
            Log.LogError("Probe failed: " + ex);
        }
    }
}
