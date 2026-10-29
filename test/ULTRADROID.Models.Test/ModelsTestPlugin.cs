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
        "0.3.2")]
    [BepInDependency("somerandommaid.ultradroid.models")]
    public class ModelsTestPlugin : BasePlugin
    {
        internal static ManualLogSource Log;

        internal const string ModelPath =
            "/storage/emulated/0/BepInEx_Android/com.aarongamingdev.ULTRAKILL/modpacks/import models test/plugins/ULTRADROID.BladesOfChaos/blades_of_chaos_god.glb";

        public override void Load()
        {
            Log = base.Log;

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<GlbAnimationPlayer>();
                ClassInjector.RegisterTypeInIl2Cpp<ModelTestLoader>();

                AddComponent<ModelTestLoader>();

                Log.LogInfo(
                    "[Models Test] Animation playback initialized.");
            }
            catch (Exception ex)
            {
                Log.LogError(
                    "[Models Test] Failed to initialize runtime: " + ex);
            }
        }
    }

    public class ModelTestLoader : MonoBehaviour
    {
        private float nextModelAttemptTime;
        private string detectedGameplayScene = "";

        private bool gameplayDetected;
        private bool modelLoadAttempted;

        private Camera currentCamera;
        private ModelInstance instance;
        private GlbAnimationPlayer animationPlayer;

        private const string AnimationName = "dance_loop_fit";
        private bool animationStarted;

        public ModelTestLoader(IntPtr ptr) : base(ptr)
        {
        }

        public void Update()
        {
            try
            {
                Scene active = SceneManager.GetActiveScene();
                string candidate = null;

                if (active.IsValid() &&
                    active.isLoaded &&
                    IsPlayableSceneName(active.name))
                {
                    candidate = active.name;
                }
                else
                {
                    for (int i = 0; i < SceneManager.sceneCount; i++)
                    {
                        Scene scene = SceneManager.GetSceneAt(i);

                        if (scene.IsValid() &&
                            scene.isLoaded &&
                            IsPlayableSceneName(scene.name))
                        {
                            candidate = scene.name;
                            break;
                        }
                    }
                }

                if (candidate == null)
                {
                    if (gameplayDetected)
                        ResetGameplayState();

                    return;
                }

                if (!gameplayDetected ||
                    !string.Equals(
                        detectedGameplayScene,
                        candidate,
                        StringComparison.OrdinalIgnoreCase))
                {
                    OnGameplaySceneDetected(candidate);
                }

                if (!modelLoadAttempted &&
                    Time.unscaledTime >= nextModelAttemptTime)
                {
                    TryLoadModel();
                }
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Models Test] Runtime error: " + ex);
            }
        }

        private bool IsPlayableSceneName(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                return false;

            string name = sceneName.Trim();

            if (string.Equals(
                name,
                "uk_construct",
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

            detectedGameplayScene = sceneName;
            gameplayDetected = true;
            modelLoadAttempted = false;

            nextModelAttemptTime = Time.unscaledTime + 2f;

            currentCamera = null;
            animationStarted = false;

            DestroyCurrentModel();

            ModelsTestPlugin.Log.LogInfo(
                "[Scene] Gameplay scene detected: " + sceneName);
        }

        private void ResetGameplayState()
        {
            ModelsTestPlugin.Log.LogInfo(
                "[Scene] No playable scene detected. Cleaning up test model.");

            gameplayDetected = false;
            detectedGameplayScene = "";
            modelLoadAttempted = false;
            animationStarted = false;
            currentCamera = null;

            DestroyCurrentModel();
        }

        private Camera FindUsableCamera()
        {
            Camera main = Camera.main;

            if (main != null && main.isActiveAndEnabled)
                return main;

            Camera[] cameras = FindObjectsOfType<Camera>();

            foreach (Camera camera in cameras)
            {
                if (camera == null || !camera.isActiveAndEnabled)
                    continue;

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
            if (!File.Exists(ModelsTestPlugin.ModelPath))
            {
                modelLoadAttempted = true;

                ModelsTestPlugin.Log.LogError(
                    "[Model] File missing: " +
                    ModelsTestPlugin.ModelPath);

                return;
            }

            currentCamera = FindUsableCamera();

            if (currentCamera == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Camera] No usable camera yet. Retrying in three seconds.");

                nextModelAttemptTime = Time.unscaledTime + 3f;
                return;
            }

            modelLoadAttempted = true;

            try
            {
                ModelLoadOptions options = new ModelLoadOptions
                {
                    Name = "BladesOfChaosTest",
                    Parent = currentCamera.transform,
                    Position = new Vector3(0f, 0f, 1.5f),
                    RotationEuler = Vector3.zero,
                    Scale = new Vector3(0.2f, 0.2f, 0.2f),
                    FlipUvV = true
                };

                instance = ModelAPI.LoadGLB(
                    ModelsTestPlugin.ModelPath,
                    options);

                if (instance == null ||
                    instance.Root == null ||
                    !instance.IsValid)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Model] LoadGLB returned no valid model root.");

                    return;
                }

                SkinnedMeshRenderer[] skinned =
                    instance.Root.GetComponentsInChildren
                        <SkinnedMeshRenderer>(true);

                MeshRenderer[] staticMeshes =
                    instance.Root.GetComponentsInChildren
                        <MeshRenderer>(true);

                ModelsTestPlugin.Log.LogInfo(
                    "[Animation Debug] Skinned renderers: " +
                    skinned.Length);

                ModelsTestPlugin.Log.LogInfo(
                    "[Animation Debug] Static mesh renderers: " +
                    staticMeshes.Length);

                ModelsTestPlugin.Log.LogInfo(
                    "[Model] GLB loaded successfully: " +
                    instance.SourcePath);

                int animationCount = instance.Animations.Count;

                ModelsTestPlugin.Log.LogInfo(
                    "[Animation] Supported clips found: " + animationCount);

                bool danceFound = false;

                for (int i = 0; i < animationCount; i++)
                {
                    ModelAnimationClip clip = instance.Animations[i];

                    if (clip == null)
                        continue;

                    ModelsTestPlugin.Log.LogInfo(
                        "[Animation] Clip " + i + ": '" +
                        clip.Name + "' (duration: " +
                        clip.Duration + " seconds)");

                    if (string.Equals(
                        clip.Name,
                        AnimationName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        danceFound = true;
                    }
                }

                if (!danceFound)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Animation] Clip '" + AnimationName +
                        "' was not found in the GLB.");

                    return;
                }

                animationPlayer = GlbAnimationPlayerFactory.Attach(instance);

                if (animationPlayer == null)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Animation] Failed to attach animation player.");

                    return;
                }

                animationPlayer.Loop = true;
                animationPlayer.PlaybackSpeed = 1f;

                animationStarted = animationPlayer.Play(AnimationName);

                if (animationStarted)
                {
                    ModelsTestPlugin.Log.LogInfo(
                        "[Animation] Successfully started looping '" +
                        AnimationName + "'.");
                }
                else
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Animation] Failed to start '" + AnimationName + "'.");
                }
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Model] LOAD FAILED: " + ex);
            }
        }

        public void OnDestroy()
        {
            DestroyCurrentModel();
        }

        private void DestroyCurrentModel()
        {
            animationPlayer = null;
            animationStarted = false;

            if (instance == null)
                return;

            try
            {
                instance.Destroy();
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Model] Destroy failed: " + ex);
            }

            instance = null;
        }
    }
}
