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
        private float nextModelAttemptTime;

        private string lastActiveSceneName = "";
        private string detectedGameplayScene = "";

        private bool gameplayDetected;
        private bool modelLoadAttempted;
        private bool sceneEventSubscribed;

        private Camera selectedCamera;
        private ModelInstance instance;

        public ModelTestLoader(IntPtr ptr) : base(ptr)
        {
        }

        private void Awake()
        {
            ModelsTestPlugin.Log.LogInfo(
                "[Runtime] Awake called. Component is alive.");
        }

        private void OnEnable()
        {
            ModelsTestPlugin.Log.LogInfo(
                "[Runtime] OnEnable called.");

            if (!sceneEventSubscribed)
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
                sceneEventSubscribed = true;
            }

            LogCurrentScenes("OnEnable");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                ModelsTestPlugin.Log.LogInfo(
                    $"[SceneEvent] Scene loaded: '{scene.name}', " +
                    $"mode={mode}, loaded={scene.isLoaded}");

                if (IsPlayableSceneName(scene.name))
                    OnGameplaySceneDetected(scene.name);
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    $"[SceneEvent] Error: {ex}");
            }
        }

        public void Update()
        {
            try
            {
                // This heartbeat must appear every three seconds if Update runs.
                if (Time.unscaledTime >= nextDiagnosticTime)
                {
                    nextDiagnosticTime = Time.unscaledTime + 3f;

                    ModelsTestPlugin.Log.LogInfo(
                        "[Heartbeat] ModelTestLoader.Update IS RUNNING.");

                    LogCurrentScenes("Heartbeat");
                }

                DiagnoseActiveScene();

                if (!gameplayDetected || modelLoadAttempted)
                    return;

                if (Time.unscaledTime < nextModelAttemptTime)
                    return;

                TryLoadModel();
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    $"[Runtime] Update exception: {ex}");
            }
        }

        private void LogCurrentScenes(string source)
        {
            try
            {
                Scene active = SceneManager.GetActiveScene();

                ModelsTestPlugin.Log.LogInfo(
                    $"[Scene:{source}] Active='{active.name}', " +
                    $"valid={active.IsValid()}, loaded={active.isLoaded}, " +
                    $"sceneCount={SceneManager.sceneCount}");

                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);

                    ModelsTestPlugin.Log.LogInfo(
                        $"[Scene:{source}] [{i}] '{scene.name}', " +
                        $"valid={scene.IsValid()}, loaded={scene.isLoaded}");
                }
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    $"[Scene:{source}] Scene inspection failed: {ex}");
            }
        }

        private void DiagnoseActiveScene()
        {
            Scene active = SceneManager.GetActiveScene();

            string activeName = active.IsValid()
                ? active.name
                : "<invalid>";

            if (activeName != lastActiveSceneName)
            {
                lastActiveSceneName = activeName;

                ModelsTestPlugin.Log.LogInfo(
                    $"[ScenePoll] Active scene changed: '{activeName}'");
            }

            if (IsPlayableSceneName(activeName))
                OnGameplaySceneDetected(activeName);

            // Also search loaded scenes in case gameplay is additive.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (scene.IsValid() &&
                    scene.isLoaded &&
                    IsPlayableSceneName(scene.name))
                {
                    OnGameplaySceneDetected(scene.name);
                    break;
                }
            }
        }

        private bool IsPlayableSceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            string name = sceneName.Trim();

            if (string.Equals(
                name, "uk_construct",
                StringComparison.OrdinalIgnoreCase))
                return true;

            if (name.StartsWith(
                "Level ",
                StringComparison.OrdinalIgnoreCase))
                return true;

            // Examples: 0-1, P-1, A-1, E-1.
            if (name.Length == 3 &&
                name[1] == '-' &&
                char.IsDigit(name[2]))
            {
                char prefix = char.ToUpperInvariant(name[0]);

                return char.IsDigit(prefix) ||
                       prefix == 'P' ||
                       prefix == 'A' ||
                       prefix == 'E';
            }

            return false;
        }

        private void OnGameplaySceneDetected(string sceneName)
        {
            if (gameplayDetected &&
                string.Equals(
                    detectedGameplayScene,
                    sceneName,
                    StringComparison.OrdinalIgnoreCase))
                return;

            ModelsTestPlugin.Log.LogInfo(
                $"[Gameplay] Recognized candidate scene: '{sceneName}'");

            detectedGameplayScene = sceneName;
            gameplayDetected = true;
            modelLoadAttempted = false;

            // Allow the game a little time to initialize its camera.
            nextModelAttemptTime = Time.unscaledTime + 2f;

            selectedCamera = null;
            DestroyCurrentModel();

            ModelsTestPlugin.Log.LogInfo(
                "[Gameplay] Model load scheduled in two seconds.");
        }

        private Camera FindUsableCamera()
        {
            Camera main = Camera.main;

            if (main != null && main.isActiveAndEnabled)
            {
                ModelsTestPlugin.Log.LogInfo(
                    $"[Camera] Camera.main='{main.name}'");

                return main;
            }

            Camera[] cameras = FindObjectsOfType<Camera>();

            ModelsTestPlugin.Log.LogInfo(
                $"[Camera] Found {cameras.Length} camera(s).");

            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.isActiveAndEnabled)
                    continue;

                ModelsTestPlugin.Log.LogInfo(
                    $"[Camera] Candidate='{camera.name}', " +
                    $"tag='{camera.tag}', " +
                    $"position={camera.transform.position}");

                if (camera.CompareTag("MainCamera"))
                    return camera;
            }

            foreach (Camera camera in cameras)
            {
                if (camera != null && camera.isActiveAndEnabled)
                    return camera;
            }

            return null;
        }

        private void TryLoadModel()
        {
            ModelsTestPlugin.Log.LogInfo(
                $"[Model] Attempting load in '{detectedGameplayScene}'.");

            if (!File.Exists(ModelsTestPlugin.ModelPath))
            {
                modelLoadAttempted = true;

                ModelsTestPlugin.Log.LogError(
                    $"[Model] File missing: {ModelsTestPlugin.ModelPath}");

                return;
            }

            selectedCamera = FindUsableCamera();

            if (selectedCamera == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Camera] No usable camera yet. Retrying in three seconds.");

                nextModelAttemptTime = Time.unscaledTime + 3f;
                return;
            }

            modelLoadAttempted = true;

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

                if (instance == null || instance.Root == null)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Model] LoadGLB returned no valid root.");

                    return;
                }

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] LOAD SUCCESS: '{instance.Root.name}'");

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Camera='{selectedCamera.name}', " +
                    $"position={selectedCamera.transform.position}");

                ModelsTestPlugin.Log.LogInfo(
                    $"[Model] Root local position=" +
                    $"{instance.Root.transform.localPosition}, " +
                    $"scale={instance.Root.transform.localScale}");

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
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);

            MeshFilter[] filters =
                root.GetComponentsInChildren<MeshFilter>(true);

            ModelsTestPlugin.Log.LogInfo(
                $"[Model] Renderers={renderers.Length}, " +
                $"MeshFilters={filters.Length}");

            foreach (Renderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                ModelsTestPlugin.Log.LogInfo(
                    $"[Renderer] '{renderer.name}', " +
                    $"enabled={renderer.enabled}, " +
                    $"active={renderer.gameObject.activeInHierarchy}, " +
                    $"bounds={renderer.bounds}");
            }

            foreach (MeshFilter filter in filters)
            {
                if (filter == null)
                    continue;

                Mesh mesh = filter.sharedMesh;

                ModelsTestPlugin.Log.LogInfo(
                    mesh == null
                        ? $"[Mesh] '{filter.name}' has NO mesh."
                        : $"[Mesh] '{filter.name}', " +
                          $"vertices={mesh.vertexCount}, " +
                          $"submeshes={mesh.subMeshCount}");
            }
        }

        private void OnDisable()
        {
            if (sceneEventSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                sceneEventSubscribed = false;
            }

            ModelsTestPlugin.Log.LogInfo(
                "[Runtime] OnDisable called.");
        }

        private void OnDestroy()
        {
            if (sceneEventSubscribed)
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                sceneEventSubscribed = false;
            }

            DestroyCurrentModel();
        }

        private void DestroyCurrentModel()
        {
            if (instance == null)
                return;

            try
            {
                instance.Destroy();
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogWarning(
                    $"[Model] Destroy failed: {ex}");
            }

            instance = null;
        }
    }
}
