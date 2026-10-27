using BepInEx;
using BepInEx.Logging;

namespace ULTRADROID.Models
{
    [BepInPlugin(
        "somerandommaid.ultradroid.models",
        "ULTRADROID Models API",
        "0.2.0")]
    public sealed class ModelsPlugin : BasePlugin
    {
        internal static ManualLogSource PluginLogger;

        public override void Load()
        {
            PluginLogger = Log;
            Log.LogInfo("ULTRADROID Models API 0.2.0 loaded.");
        }
    }
}
