using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Unity.IL2CPP;

[BepInPlugin(
    "somerandommaid.ultradroid.newtonsoftprobe",
    "ULTRADROID Newtonsoft Probe",
    "0.4.0")]
public class NewtonsoftRuntimeProbe : BasePlugin
{
    public override void Load()
    {
        Log.LogInfo("=== Newtonsoft Runtime Probe 0.4.0 ===");

        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetName().Name == "Newtonsoft.Json")
                ?? Assembly.Load("Newtonsoft.Json");

            var ioAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a =>
                    a.GetType("Il2CppSystem.IO.StringReader") != null);

            if (ioAssembly == null)
                throw new Exception("Il2CppSystem.IO types not found.");

            var stringReaderType =
                ioAssembly.GetType("Il2CppSystem.IO.StringReader")!;
            var jsonTextReaderType =
                assembly.GetType("Newtonsoft.Json.JsonTextReader")!;
            var jObjectType =
                assembly.GetType("Newtonsoft.Json.Linq.JObject")!;
            var jsonConvertType =
                assembly.GetType("Newtonsoft.Json.JsonConvert")!;

            const string json =
                "{\"probe\":\"ULTRADROID\",\"number\":123}";

            Log.LogInfo("Creating StringReader...");
            var stringReader = Activator.CreateInstance(
                stringReaderType, new object[] { json })
                ?? throw new Exception("StringReader construction returned null.");

            Log.LogInfo("Creating JsonTextReader...");
            var jsonReader = Activator.CreateInstance(
                jsonTextReaderType, new[] { stringReader })
                ?? throw new Exception("JsonTextReader construction returned null.");

            Log.LogInfo("Calling JObject.Load...");
            var loadMethod = jObjectType.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .First(m =>
                {
                    if (m.Name != "Load") return false;
                    var p = m.GetParameters();
                    return p.Length == 2 &&
                           p[0].ParameterType.IsInstanceOfType(jsonReader);
                });

            var loadParameters = loadMethod.GetParameters();
            var settingsType = loadParameters[1].ParameterType;
            var settings = Activator.CreateInstance(settingsType);

            var parsed = loadMethod.Invoke(
                null, new[] { jsonReader, settings })
                ?? throw new Exception("JObject.Load returned null.");

            Log.LogInfo("PARSE SUCCESS");
            Log.LogInfo("Parsed JSON: " + parsed);

            var indexer = jObjectType.GetProperty(
                "Item", new[] { typeof(string) })
                ?? throw new Exception("JObject string indexer not found.");

            var probeToken = indexer.GetValue(
                parsed, new object[] { "probe" });

            var numberToken = indexer.GetValue(
                parsed, new object[] { "number" });

            Log.LogInfo("probe token: " +
                (probeToken?.ToString() ?? "<null>"));
            Log.LogInfo("number token: " +
                (numberToken?.ToString() ?? "<null>"));

            Log.LogInfo("Calling JsonConvert.SerializeObject...");
            var serializeMethod = jsonConvertType.GetMethods(
                    BindingFlags.Public | BindingFlags.Static)
                .First(m =>
                {
                    if (m.Name != "SerializeObject") return false;
                    var p = m.GetParameters();
                    return p.Length == 1 &&
                           p[0].ParameterType.IsInstanceOfType(parsed);
                });

            var serialized = serializeMethod.Invoke(
                null, new[] { parsed })?.ToString();

            if (serialized == null)
                throw new Exception("SerializeObject returned null.");

            Log.LogInfo("SERIALIZE SUCCESS: " + serialized);

            Log.LogInfo("=== JSON parse/serialize test passed ===");
        }
        catch (Exception ex)
        {
            Log.LogError("JSON test failed: " +
                (ex.InnerException ?? ex));
        }
    }
}
