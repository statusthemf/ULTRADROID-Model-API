
using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using ULTRADROID.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ULTRADROID.Models.Test
{
    [BepInPlugin(
        "somerandommaid.ultradroid.models.test",
        "ULTRADROID Models Test",
        "0.2.0")]
    public sealed class ModelsTestPlugin : BasePlugin
    {
        internal static ManualLogSource Logger;

        private const string ModelPath =
            "/storage/emulated/0/BepInEx_Android/com.aarongamingdev.ULTRAKILL/modpacks/import models test/plugins/ULTRADROID.BladesOfChaos/blades_of_chaos_god.glb";

        public override void Load()
        {
            Logger = Log;

            Logger.LogInfo("ULTRADROID Models Test started.");
            Logger.LogInfo("Models API version: " + ModelAPI.Version);

            if (!File.Exists(ModelPath))
            {
                Logger.LogError("Model file not found: " + ModelPath);
                return;
            }

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModelTestLoader>();

                GameObject host = new GameObject(
                    "ULTRADROID_Models_Test_Runtime");

                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<ModelTestLoader>();

                Logger.LogInfo(
                    "Runtime ready. Waiting for a playable level.");
            }
            catch (Exception ex)
            {
                Logger.LogError("Could not initialize model runtime:");
                Logger.LogError(ex.ToString());
            }
        }
    }

    public sealed class ModelTestLoader : MonoBehaviour
    {
        private const float LoadDelay = 2f;

        // Camera-local placement. Positive Z is in front of the camera.
        private static readonly Vector3 ModelPosition =
            new Vector3(0f, 0f, 1.5f);

        private static readonly Vector3 ModelRotation =
            Vector3.zero;

        private static readonly Vector3 ModelScale =
            new Vector3(0.1f, 0.1f, 0.1f);

        private string observedScene;
        private float gameplayStartTime;
        private bool loadAttempted;
        private bool cameraWarningShown;

        private ModelInstance instance;

        public ModelTestLoader(IntPtr ptr) : base(ptr)
        {
        }

        public void Update()
        {
            string sceneName = SceneManager.GetActiveScene().name;

            if (string.IsNullOrEmpty(sceneName))
                return;

            // Detect every scene transition, including level changes.
            if (sceneName != observedScene)
            {
                OnSceneChanged(sceneName);
            }

            if (!IsPlayableScene(sceneName))
                return;

            // Wait until gameplay has had time to initialize.
            if (Time.time - gameplayStartTime < LoadDelay)
                return;

            if (loadAttempted)
                return;

            Camera camera = FindGameplayCamera();

            // Keep waiting if the camera has not initialized yet.
            if (camera == null)
            {
                if (!cameraWarningShown)
                {
                    ModelsTestPlugin.Logger.LogInfo(
                        "Playable scene detected, but no active camera is ready yet.");

                    cameraWarningShown = true;
                }

                return;
            }

            // Attempt only once per playable scene.
            loadAttempted = true;

            LoadModel(camera, sceneName);
        }

        private void OnSceneChanged(string sceneName)
        {
            observedScene = sceneName;
            gameplayStartTime = Time.time;
            loadAttempted = false;
            cameraWarningShown = false;

            if (instance != null)
            {
                instance.Destroy();
                instance = null;
            }

            ModelsTestPlugin.Logger.LogInfo(
                "Scene changed to: " + sceneName);

            if (IsPlayableScene(sceneName))
            {
                ModelsTestPlugin.Logger.LogInfo(
                    "Playable level detected. Waiting two seconds before loading.");
            }
            else
            {
                ModelsTestPlugin.Logger.LogInfo(
                    "Not a playable level. Model loading is disabled.");
            }
        }

        private static bool IsPlayableScene(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            string name = sceneName.Trim();

            // Known non-gameplay scenes.
            if (name.Equals("Bootstrap", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Intro", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Main Menu", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("MainMenu", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // ULTRAKILL's Construct sandbox.
            if (name.Equals("uk_construct", StringComparison.OrdinalIgnoreCase))
                return true;

            // Standard named levels, e.g. "Level 0-1", "Level 8-3", "Level A-1".
            if (name.StartsWith("Level ", StringComparison.OrdinalIgnoreCase))
                return true;

            // Also allow raw level identifiers such as "0-1", "8-3", "A-1".
            if (name.Length == 3 &&
                name[1] == '-' &&
                char.IsDigit(name[2]) &&
                (char.IsDigit(name[0]) ||
                 char.ToUpperInvariant(name[0]) == 'A' ||
                 char.ToUpperInvariant(name[0]) == 'P'))
            {
                return true;
            }

            return false;
        }

        private static Camera FindGameplayCamera()
        {
            if (Camera.main != null &&
                Camera.main.enabled &&
                Camera.main.gameObject.activeInHierarchy)
            {
                return Camera.main;
            }

            // Fallback for scenes whose gameplay camera lacks the MainCamera tag.
            Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();

            foreach (Camera camera in cameras)
            {
                if (camera != null &&
                    camera.enabled &&
                    camera.gameObject.activeInHierarchy)
                {
                    return camera;
                }
            }

            return null;
        }

        private void LoadModel(Camera camera, string sceneName)
        {
            try
            {
                ModelsTestPlugin.Logger.LogInfo(
                    "Loading model into camera for scene: " + sceneName);

                var options = new ModelLoadOptions
                {
                    Name = "BladesOfChaosTest",
                    Position = ModelPosition,
                    RotationEuler = ModelRotation,
                    Scale = ModelScale,
                    LoadTextures = true,
                    CreateColliders = false,
                    FlipUvV = false,
                    Parent = camera.transform
                };

                instance = ModelAPI.LoadGLB(
                    GetModelPath(),
                    options);

                if (instance == null || !instance.IsValid)
                {
                    ModelsTestPlugin.Logger.LogError(
                        "The API returned an invalid model instance.");

                    return;
                }

                ModelsTestPlugin.Logger.LogInfo(
                    "MODEL LOADED SUCCESSFULLY: " + instance.Root.name);

                ModelsTestPlugin.Logger.LogInfo(
                    "Parent camera: " + camera.name);

                ModelsTestPlugin.Logger.LogInfo(
                    "Camera-local position: " +
                    instance.Root.transform.localPosition);

                ModelsTestPlugin.Logger.LogInfo(
                    "Camera-local scale: " +
                    instance.Root.transform.localScale);
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Logger.LogError("MODEL LOAD FAILED:");
                ModelsTestPlugin.Logger.LogError(ex.ToString());
            }
        }

        private static string GetModelPath()
        {
            return "/storage/emulated/0/BepInEx_Android/com.aarongamingdev.ULTRAKILL/modpacks/import models test/plugins/ULTRADROID.BladesOfChaos/blades_of_chaos_god.glb";
        }
    }
}
