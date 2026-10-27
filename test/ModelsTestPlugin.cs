
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using ULTRADROID.Models;
using UnityEngine;

namespace ULTRADROID.Models.Test
{
    [BepInPlugin(
        "somerandommaid.ultradroid.models.test",
        "ULTRADROID Models Test",
        "0.1.0")]
    public sealed class ModelsTestPlugin : BasePlugin
    {
        private const string ModelPath =
            "/storage/emulated/0/BepInEx_Android/com.aarongamingdev.ULTRAKILL/modpacks/import models test/plugins/ULTRADROID.BladesOfChaos/blades_of_chaos_god.glb";

        public override void Load()
        {
            Log.LogInfo("ULTRADROID Models Test started.");
            Log.LogInfo("Models API version: " + ModelAPI.Version);
            Log.LogInfo("Model path: " + ModelPath);

            try
            {
                if (!File.Exists(ModelPath))
                {
                    Log.LogError("MODEL FILE NOT FOUND.");
                    Log.LogError("Check the path and Android storage permissions.");
                    return;
                }

                var info = new FileInfo(ModelPath);
                Log.LogInfo("Model file exists. Size: " + info.Length + " bytes.");

                var options = new ModelLoadOptions
                {
                    Name = "BladesOfChaosTest",
                    Position = Vector3.zero,
                    RotationEuler = Vector3.zero,
                    Scale = Vector3.one,
                    LoadTextures = true,
                    CreateColliders = false,
                    FlipUvV = false
                };

                Log.LogInfo("Calling ModelAPI.LoadGLB...");

                ModelInstance instance = ModelAPI.LoadGLB(ModelPath, options);

                if (instance == null || !instance.IsValid)
                {
                    Log.LogError("API returned no valid model instance.");
                    return;
                }

                Log.LogInfo("MODEL LOADED SUCCESSFULLY: " + instance.Root.name);
            }
            catch (Exception ex)
            {
                Log.LogError("MODEL LOAD FAILED:");
                Log.LogError(ex.ToString());
            }
        }
    }
}
