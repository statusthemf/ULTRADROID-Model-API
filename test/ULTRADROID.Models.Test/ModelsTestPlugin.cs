using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using ULTRADROID.Models;
using ULTRADROID.API;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ULTRADROID.Models.Test
{
    [BepInPlugin(
        "somerandommaid.ultradroid.models.test",
        "ULTRADROID Models Test",
        "0.3.2")]
    [BepInDependency("somerandommaid.ultradroid.models")]
    [BepInDependency(
        ULTRADROIDControls.PluginGuid,
        BepInDependency.DependencyFlags.HardDependency)]
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
        private float nextNativeWeaponDiagnosticTime;
        private bool nativeWeaponDiagnosticsCompleted;

        private Camera currentCamera;
        private ModelInstance instance;
        private GlbAnimationPlayer animationPlayer;

        private const string AnimationName = "dance_loop_fit";
        private const float DefaultTransitionDuration = 0.35f;
        private bool animationStarted;
        private int currentAnimationIndex = -1;

        private readonly System.Collections.Generic.List<string>
            animationNames =
                new System.Collections.Generic.List<string>();

        private ButtonHandle previousAnimationButton;
        private ButtonHandle nextAnimationButton;
        private ButtonHandle layerToggleButton;
        private bool testLayerEnabled;
        private const string TestLayerAnimation = "wave";

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

                if (!nativeWeaponDiagnosticsCompleted &&
                    Time.unscaledTime >= nextNativeWeaponDiagnosticTime)
                {
                    LogNativeWeaponDiagnostics();
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
            nativeWeaponDiagnosticsCompleted = false;
            nextNativeWeaponDiagnosticTime =
                Time.unscaledTime + 4f;

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

        private bool PlayAnimationAtIndex(int index)
        {
            if (animationPlayer == null ||
                index < 0 ||
                index >= animationNames.Count)
                return false;

            string clipName = animationNames[index];

            animationPlayer.Loop = true;
            animationPlayer.PlaybackSpeed = 1f;

            bool played;

            if (animationStarted)
            {
                played = animationPlayer.CrossFade(
                    clipName,
                    DefaultTransitionDuration);
            }
            else
            {
                played = animationPlayer.Play(clipName);
            }

            if (!played)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Animation] Failed to play: " + clipName);

                return false;
            }

            currentAnimationIndex = index;
            animationStarted = true;

            ModelsTestPlugin.Log.LogInfo(
                "[Animation] Now playing: " + clipName);

            return true;
        }

        private void PlayNextAnimation()
        {
            if (animationNames.Count < 2)
                return;

            int nextIndex =
                (currentAnimationIndex + 1) % animationNames.Count;

            PlayAnimationAtIndex(nextIndex);
        }

        private void PlayPreviousAnimation()
        {
            if (animationNames.Count < 2)
                return;

            int previousIndex =
                (currentAnimationIndex - 1 + animationNames.Count)
                % animationNames.Count;

            PlayAnimationAtIndex(previousIndex);
        }

        private void ToggleTestLayer()
        {
            if (animationPlayer == null || !animationStarted)
                return;

            if (testLayerEnabled)
            {
                animationPlayer.ClearLayers();
                testLayerEnabled = false;

                ModelsTestPlugin.Log.LogInfo(
                    "[Animation Layer] Test layer disabled.");

                return;
            }

            animationPlayer.SetLayer(
                TestLayerAnimation,
                0.8f,
                new string[]
                {
                    "Armature",
                    "Spine",
                    "Chest",
                    "UpperArm.L",
                    "UpperArm.R",
                    "Forearm.L",
                    "Forearm.R"
                });

            testLayerEnabled = true;

            ModelsTestPlugin.Log.LogInfo(
                "[Animation Layer] Test layer enabled: " +
                TestLayerAnimation);
        }

        private void RegisterAnimationButtons()
        {
            previousAnimationButton =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = "ULTRADROID.Models.PreviousAnimation",
                    Label = "PREV ANIM",
                    PositionOffset = new Vector2(-120f, 90f),
                    ScaleMultiplier = 1f,
                    ApplyTint = false,
                    OnClick = PlayPreviousAnimation
                });

            nextAnimationButton =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = "ULTRADROID.Models.NextAnimation",
                    Label = "NEXT ANIM",
                    PositionOffset = new Vector2(120f, 90f),
                    ScaleMultiplier = 1f,
                    ApplyTint = false,
                    OnClick = PlayNextAnimation
                });

            layerToggleButton =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = "ULTRADROID.Models.ToggleAnimationLayer",
                    Label = "LAYER",
                    PositionOffset = new Vector2(0f, 180f),
                    ScaleMultiplier = 1f,
                    OnClick = ToggleTestLayer
                });

            ModelsTestPlugin.Log.LogInfo(
                "[Animation] Previous and Next touch buttons registered.");
        }

        private UnityEngine.Object FindIl2CppObjectByTypeName(string typeName)
        {
            UnityEngine.Object[] objects =
                Resources.FindObjectsOfTypeAll<UnityEngine.Object>();

            foreach (UnityEngine.Object obj in objects)
            {
                if (obj == null)
                    continue;

                if (obj.GetType().Name == typeName)
                    return obj;
            }

            return null;
        }

        private void LogNativeWeaponDiagnostics()
        {
            Type gunControlType = null;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                gunControlType = assembly.GetType("GunControl");

                if (gunControlType != null)
                    break;
            }

            if (gunControlType == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Native Weapon] GunControl type not found. Retrying in 2 seconds.");

                nextNativeWeaponDiagnosticTime =
                    Time.unscaledTime + 2f;

                return;
            }

            UnityEngine.Object gunControlObject =
                FindIl2CppObjectByTypeName("GunControl");

            if (gunControlObject == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Native Weapon] GunControl instance not found. Retrying in 2 seconds.");

                nextNativeWeaponDiagnosticTime =
                    Time.unscaledTime + 2f;

                return;
            }

            ModelsTestPlugin.Log.LogInfo("[Native Weapon] GunControl instance found!");

            FieldInfo weaponField = gunControlType.GetField(
                "currentWeapon",
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (weaponField == null)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Native Weapon] currentWeapon field not found.");

                nativeWeaponDiagnosticsCompleted = true;
                return;
            }

            object currentWeapon =
                weaponField.GetValue(gunControlObject);

            if (currentWeapon == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Native Weapon] currentWeapon is null. Retrying in 2 seconds.");

                nextNativeWeaponDiagnosticTime =
                    Time.unscaledTime + 2f;

                return;
            }

            ModelsTestPlugin.Log.LogInfo(
                "[Native Weapon] Current weapon type: " +
                currentWeapon.GetType().FullName);

            Component weaponComponent = currentWeapon as Component;

            if (weaponComponent == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Native Weapon] Current weapon is not a Component.");

                nativeWeaponDiagnosticsCompleted = true;
                return;
            }

            Renderer[] renderers =
                weaponComponent.GetComponentsInChildren<Renderer>(true);

            ModelsTestPlugin.Log.LogInfo(
                "[Native Weapon] Renderer count: " + renderers.Length);

            foreach (Renderer renderer in renderers)
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[Native Weapon] Renderer: " + renderer.name);

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null)
                        continue;

                    ModelsTestPlugin.Log.LogInfo(
                        "[Native Weapon] Material: " + material.name +
                        " | Shader: " +
                        (material.shader != null
                            ? material.shader.name
                            : "NULL"));

                    ModelsTestPlugin.Log.LogInfo(
                        "[Native Weapon] Metallic=" +
                        (material.HasProperty("_Metallic")
                            ? material.GetFloat("_Metallic").ToString()
                            : "N/A") +
                        " | Glossiness=" +
                        (material.HasProperty("_Glossiness")
                            ? material.GetFloat("_Glossiness").ToString()
                            : "N/A") +
                        " | Smoothness=" +
                        (material.HasProperty("_Smoothness")
                            ? material.GetFloat("_Smoothness").ToString()
                            : "N/A") +
                        " | Emission=" +
                        (material.HasProperty("_EmissionColor")
                            ? material.GetColor("_EmissionColor").ToString()
                            : "N/A"));
                }
            }

            nativeWeaponDiagnosticsCompleted = true;

            ModelsTestPlugin.Log.LogInfo(
                "[Native Weapon] Material diagnostics completed.");
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

                animationNames.Clear();
                currentAnimationIndex = -1;

                ModelsTestPlugin.Log.LogInfo(
                    "[Animation] Supported clips found: " + animationCount);

                for (int i = 0; i < animationCount; i++)
                {
                    ModelAnimationClip clip = instance.Animations[i];

                    if (clip == null)
                        continue;

                    animationNames.Add(clip.Name);

                    ModelsTestPlugin.Log.LogInfo(
                        "[Animation] Clip " + i + ": '" +
                        clip.Name + "' (duration: " +
                        clip.Duration + " seconds)");
                }

                if (animationNames.Count == 0)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Animation] No usable animation clips found.");

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

                int initialIndex = animationNames.FindIndex(
                    name => string.Equals(
                        name,
                        AnimationName,
                        StringComparison.OrdinalIgnoreCase));

                if (initialIndex < 0)
                    initialIndex = 0;

                if (PlayAnimationAtIndex(initialIndex))
                    RegisterAnimationButtons();
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

            if (previousAnimationButton != null)
            {
                previousAnimationButton.Dispose();
                previousAnimationButton = null;
            }

            if (nextAnimationButton != null)
            {
                nextAnimationButton.Dispose();
                nextAnimationButton = null;
            }

            if (layerToggleButton != null)
            {
                layerToggleButton.Dispose();
                layerToggleButton = null;
            }

            testLayerEnabled = false;

            animationNames.Clear();
            currentAnimationIndex = -1;

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
