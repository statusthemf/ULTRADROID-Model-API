using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using ULTRADROID.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ULTRADROID.Models.Test
{
    [BepInPlugin(
        "somerandommaid.ultradroid.models.test",
        "ULTRADROID Models Test",
        "0.3.1")]
    [BepInDependency("somerandommaid.ultradroid.models")]
    public class ModelsTestPlugin : BasePlugin
    {
        internal static ManualLogSource Log;

        internal const string ModelPath =
            "/storage/emulated/0/BepInEx_Android/com.aarongamingdev.ULTRAKILL/modpacks/import models test/plugins/ULTRADROID.BladesOfChaos/blades_of_chaos_god.glb";

        public override void Load()
        {
            Log = base.Log;

            Log.LogInfo("========================================");
            Log.LogInfo("ULTRADROID Models Test 0.3.1 starting.");
            Log.LogInfo($"Models API version: {ModelAPI.Version}");
            Log.LogInfo($"Model path: {ModelPath}");
            Log.LogInfo($"Model file exists: {File.Exists(ModelPath)}");

            if (File.Exists(ModelPath))
            {
                Log.LogInfo(
                    $"Model file size: {new FileInfo(ModelPath).Length} bytes.");
            }

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<ModelTestLoader>();

                GameObject host = new GameObject(
                    "ULTRADROID_Models_Test_Runtime");

                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<ModelTestLoader>();

                Log.LogInfo("Diagnostic runtime component registered.");
                Log.LogInfo("Waiting for scene diagnostics...");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to initialize runtime: {ex}");
            }

            Log.LogInfo("========================================");
        }
    }

    public class ModelTestLoader : MonoBehaviour
    {
        private float nextDiagnosticTime;
        private float gameplayDetectedTime;

        private string lastActiveSceneName = "";
        private string detectedGameplayScene = "";

        private bool gameplayDetected;
        private bool modelLoadAttempted;

        private Camera selectedCamera;
        private ModelInstance instance;

        public ModelTestLoader(IntPtr ptr) : base(ptr)
        {
        }

        private void Update()
        {
            try
            {
                DiagnoseScenes();

                if (!gameplayDetected || modelLoadAttempted)
                    return;

                if (Time.time - gameplayDetectedTime < 2f)
                    return;

                TryLoadModel();
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    $"[Runtime] Unhandled error: {ex}");
            }
        }

        private void DiagnoseScenes()
        {
            Scene activeScene = SceneManager.GetActiveScene();

            string activeName = activeScene.IsValid()
                ? activeScene.name
                : "<invalid>";

            if (activeName != lastActiveSceneName)
            {
                lastActiveSceneName = activeName;

                ModelsTestPlugin.Log.LogInfo(
                    $"[Scene] Active scene changed to: '{activeName}'");
            }

            // Report diagnostics every three seconds.
            if (Time.time >= nextDiagnosticTime)
            {
                nextDiagnosticTime = Time.time + 3f;

                ModelsTestPlugin.Log.LogInfo(
                    $"[Diagnostic] Update is running. Active scene: '{activeName}'");

                LogAllLoadedScenes();

                ModelsTestPlugin.Log.LogInfo(
                    $"[Diagnostic] Gameplay detected: {gameplayDetected}; " +
                    $"scene: '{detectedGameplayScene}'; " +
                    $"load attempted: {modelLoadAttempted}");
            }

            if (gameplayDetected)
            {
                if (activeName != detectedGameplayScene &&
                    IsPlayableSceneName(activeName))
                {
                    OnGameplaySceneDetected(activeName);
                }

                return;
            }

            // Search all loaded scenes, not only the active scene.
            int sceneCount = SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                if (!IsPlayableSceneName(scene.name))
                    continue;

                OnGameplaySceneDetected(scene.name);
                break;
            }
        }

        private void LogAllLoadedScenes()
        {
            int count = SceneManager.sceneCount;

            if (count == 0)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Scene] Unity reports zero loaded scenes.");
                return;
            }

            string sceneList = "";

            for (int i = 0; i < count; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (i > 0)
                    sceneList += " | ";

                sceneList +=
                    $"[{i}] '{scene.name}' " +
                    $"(loaded={scene.isLoaded}, valid={scene.IsValid()})";
            }

            ModelsTestPlugin.Log.LogInfo(
                $"[Scene] All loaded scenes: {sceneList}");
        }

        private bool IsPlayableSceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            string name = sceneName.Trim();

            // Sandbox.
            if (string.Equals(
                name,
                "uk_construct",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Campaign scenes.
            if (name.StartsWith(
                "Level ",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Raw identifiers: 0-1, P-1, A-1, E-1, etc.
            if (name.Length == 3 &&
                name[1] == '-' &&
                char.IsDigit(name[2]))
            {
                char prefix = char.ToUpperInvariant(name[0]);

                if (char.IsDigit(prefix) ||
                    prefix == 'P' ||
                    prefix == 'A' ||
                    prefix == 'E')
                {
                    return true;
                }
            }

            return false;
        }

        private void OnGameplaySceneDetected(string sceneName)
        {
            if (gameplayDetected &&
                detectedGameplayScene == sceneName)
            {
                return;
            }

            ModelsTestPlugin.Log.LogInfo(
                $"[Gameplay] Recognized scene: '{sceneName}'");

            detectedGameplayScene = sceneName;
            gameplayDetected = true;
            gameplayDetectedTime = Time.time;
            modelLoadAttempted = false;

            DestroyCurrentModel();
            selectedCamera = null;

            ModelsTestPlugin.Log.LogInfo(
                "[Gameplay] Waiting two seconds for initialization.");
        }

        private Camera FindUsableCamera()
        {
            ModelsTestPlugin.Log.LogInfo(
                "[Camera] Searching for a usable camera...");

            Camera mainCamera = Camera.main;

            if (mainCamera != null && mainCamera.isActiveAndEnabled)
            {
                ModelsTestPlugin.Log.LogInfo(
                    $"[Camera] Selected Camera.main: '{mainCamera.name}'");

                return mainCamera;
            }

            Camera[] cameras = FindObjectsOfType<Camera>();

            ModelsTestPlugin.Log.LogInfo(
                $"[Camera] Found {cameras.Length} camera(s).");

            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.isActiveAndEnabled)
                    continue;

                if (camera.CompareTag("MainCamera"))
                {
                    ModelsTestPlugin.Log.LogInfo(
                        $"[Camera] Selected tagged camera: '{camera.name}'");

                    return camera;
                }
            }

            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.isActiveAndEnabled)
                    continue;

                ModelsTestPlugin.Log.LogInfo(
                    $"[Camera] Selected fallback camera: '{camera.name}'");

                return camera;
            }

            ModelsTestPlugin.Log.LogWarning(
                "[Camera] No active camera was found.");

            return null;
        }

        private void TryLoadModel()
        {
            modelLoadAttempted = true;

            ModelsTestPlugin.Log.LogInfo(
                $"[Gameplay] Attempting model load in '{detectedGameplayScene}'.");

            if (!File.Exists(ModelsTestPlugin.ModelPath))
            {
                ModelsTestPlugin.Log.LogError(
                    $"[Model] File does not exist: {ModelsTestPlugin.ModelPath}");
                return;
            }

            selectedCamera = FindUsableCamera();

            if (selectedCamera == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Model] No usable camera yet. Will retry.");

                modelLoadAttempted = false;
                gameplayDetectedTime = Time.time + 3f;
                return;
            }

            try
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[Model] Calling ModelAPI.LoadGLB...");

                ModelLoadOptions options = new ModelLoadOptions
                {
                    Name = "BladesOfChaosTest",
                    Parent = selectedCamera.transform,
                    Position = new Vector3(0f, 0f, 2f),
                    RotationEuler = Vector3.zero,
                    Scale = new Vector3(0.1f, 0.1f, 0.1f)
                };

                instance = ModelAPI.LoadGLB(
                    ModelsTestPlugin.ModelPath,
                    options);

                if (instance == null)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Model] LoadGLB returned null.");
                    return;
                }

                if (instance.Root == null)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Model] ModelInstance.Root is null.");
                    return;
                }

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] LOAD SUCCESS. Root: '{instance.Root.name}'");

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Camera: '{selectedCamera.name}'");

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Root position: {instance.Root.transform.position}");

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Root local position: {instance.Root.transform.localPosition}");

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Root local scale: {instance.Root.transform.localScale}");

                LogModelRenderers(instance.Root);
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    $"[Model] LOAD FAILED: {ex}");
            }
        }

        private void LogModelRenderers(GameObject root)
        {
            if (root == null)
                return;

            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);

            MeshFilter[] meshFilters =
                root.GetComponentsInChildren<MeshFilter>(true);

            ModelsTestPlugin.Log.LogInfo(
                $"[Model] Renderer count: {renderers.Length}; " +
                $"MeshFilter count: {meshFilters.Length}");

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Renderer '{renderer.name}': " +
                    $"enabled={renderer.enabled}, " +
                    $"active={renderer.gameObject.activeInHierarchy}, " +
                    $"bounds={renderer.bounds}");
            }

            foreach (MeshFilter filter in meshFilters)
            {
                if (filter == null)
                    continue;

                Mesh mesh = filter.sharedMesh;

                if (mesh == null)
                {
                    ModelsTestPlugin.Log.LogWarning(
                        $"[Model] MeshFilter '{filter.name}' has no mesh.");
                }
                else
                {
                    ModelsTestPlugin.Log.LogInfo(
                        $"[Model] MeshFilter '{filter.name}': " +
                        $"mesh='{mesh.name}', " +
                        $"vertices={mesh.vertexCount}, " +
                        $"submeshes={mesh.subMeshCount}");
                }
            }
        }

        private void DestroyCurrentModel()
        {
            if (instance == null)
                return;

            try
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[Model] Destroying previous model instance.");

                instance.Destroy();
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogWarning(
                    $"[Model] Error destroying previous instance: {ex}");
            }

            instance = null;
        }

        private void OnDestroy()
        {
            DestroyCurrentModel();
        }
    }
}
