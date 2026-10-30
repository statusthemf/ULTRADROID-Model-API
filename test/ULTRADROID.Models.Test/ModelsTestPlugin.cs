
using System;
using System.IO;
using System.Collections.Generic;
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
        "0.4.0")]
    [BepInDependency("somerandommaid.ultradroid.models")]
    [BepInDependency(
        ULTRADROIDControls.PluginGuid,
        BepInDependency.DependencyFlags.HardDependency)]
    public class ModelsTestPlugin : BasePlugin
    {
        internal static ManualLogSource Log;
        internal static ModelOwner Owner;

        internal const string ModelPath =
            "/storage/emulated/0/BepInEx_Android/com.aarongamingdev.ULTRAKILL/modpacks/import models test/plugins/ULTRADROID.BladesOfChaos/blades_of_chaos_god.glb";

        public override void Load()
        {
            Log = base.Log;

            Owner = ModelAPI.CreateOwner(
                "somerandommaid.ultradroid.models.test");

            try
            {
                ClassInjector.RegisterTypeInIl2Cpp<GlbAnimationPlayer>();
                ClassInjector.RegisterTypeInIl2Cpp<ModelTestLoader>();

                AddComponent<ModelTestLoader>();

                Log.LogInfo(
                    "[Models Test] Runtime initialized.");
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
        private bool testButtonsRegistered;

        private Camera currentCamera;

        private ModelInstance pendingDestroyModel;
        private string pendingDestroyId;
        private int pendingDestroyCheckFrame = -1;

        private readonly List<ModelInstance> models =
            new List<ModelInstance>();

        private readonly List<GlbAnimationPlayer> animationPlayers =
            new List<GlbAnimationPlayer>();

        private ModelInstance instance;
        private GlbAnimationPlayer animationPlayer;

        private bool animationsEnabled = true;

        private const string AnimationName = "dance_loop_fit";
        private const float DefaultTransitionDuration = 0.35f;

        private bool animationStarted;
        private int currentAnimationIndex = -1;

        private readonly List<string> animationNames =
            new List<string>();

        private readonly List<ButtonHandle> testButtons =
            new List<ButtonHandle>();

        private ButtonHandle previousAnimationButton;
        private ButtonHandle nextAnimationButton;
        private ButtonHandle layerToggleButton;

        private bool testLayerEnabled;
        private const string TestLayerAnimation = "wave";

        private int spawnCounter;

        private float perfElapsed;
        private float perfFrameTimeSum;
        private float perfFrameTimeMax;
        private int perfFrameCount;

        public ModelTestLoader(IntPtr ptr) : base(ptr)
        {
        }

        private void SamplePerformance()
        {
            float frameTime = Time.unscaledDeltaTime;

            if (frameTime <= 0f)
                return;

            perfElapsed += frameTime;
            perfFrameTimeSum += frameTime;
            perfFrameCount++;

            if (frameTime > perfFrameTimeMax)
                perfFrameTimeMax = frameTime;

            if (perfElapsed < 5f)
                return;

            float averageMs =
                (perfFrameTimeSum / perfFrameCount) * 1000f;

            float maximumMs = perfFrameTimeMax * 1000f;

            float averageFps =
                perfFrameCount / perfElapsed;

            ModelsTestPlugin.Log.LogInfo(
                "[Performance] Models: " + models.Count +
                " | Average FPS: " + averageFps.ToString("F1") +
                " | Average frame: " + averageMs.ToString("F2") + " ms" +
                " | Maximum frame: " + maximumMs.ToString("F2") + " ms");

            perfElapsed = 0f;
            perfFrameTimeSum = 0f;
            perfFrameTimeMax = 0f;
            perfFrameCount = 0;
        }

        public void Update()
        {
            try
            {
                SamplePerformance();
                VerifyPendingDestroy();

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

            DestroyCurrentModels();
            RegisterTestButtons();

            ModelsTestPlugin.Log.LogInfo(
                "[Scene] Gameplay scene detected: " + sceneName);
        }

        private void ResetGameplayState()
        {
            ModelsTestPlugin.Log.LogInfo(
                "[Scene] No playable scene detected. Cleaning up test models.");

            gameplayDetected = false;
            detectedGameplayScene = "";
            modelLoadAttempted = false;
            currentCamera = null;

            DestroyCurrentModels();
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

        // ---------------------------------------------------------
        // MODEL SPAWNING
        // ---------------------------------------------------------

        private ModelLoadOptions CreateLoadOptions(int index)
        {
            return new ModelLoadOptions
            {
                Name = "BladesOfChaosTest_" + index,
                Parent = currentCamera.transform,
                Position = new Vector3(0f, 0f, 1.5f),
                RotationEuler = Vector3.zero,
                Scale = new Vector3(0.2f, 0.2f, 0.2f),
                FlipUvV = true
            };
        }

        private bool IsModelValid(ModelInstance model)
        {
            return model != null &&
                   model.IsValid &&
                   model.Root != null;
        }

        private void AddModel(ModelInstance model)
        {
            if (!IsModelValid(model))
                return;

            models.Add(model);
            instance = model;

            ModelsTestPlugin.Log.LogInfo(
                "[Model] Registered instance. ID: " +
                model.Id +
                " | Owner: " +
                model.OwnerId +
                " | Total instances: " +
                models.Count);
        }

        private ModelInstance SpawnModel(bool attachAnimation)
        {
            if (!File.Exists(ModelsTestPlugin.ModelPath))
            {
                ModelsTestPlugin.Log.LogError(
                    "[Model] File missing: " +
                    ModelsTestPlugin.ModelPath);

                return null;
            }

            if (currentCamera == null ||
                !currentCamera.isActiveAndEnabled)
            {
                currentCamera = FindUsableCamera();
            }

            if (currentCamera == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Camera] No usable camera found.");

                return null;
            }

            try
            {
                spawnCounter++;

                ModelLoadOptions options =
                    CreateLoadOptions(spawnCounter - 1);

                ModelInstance model =
                    ModelsTestPlugin.Owner.LoadGLB(
                        ModelsTestPlugin.ModelPath,
                        options);

                if (!IsModelValid(model))
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Model] LoadGLB returned an invalid model.");

                    return null;
                }

                AddModel(model);

                ModelsTestPlugin.Log.LogInfo(
                    "[TEST PASS] Model spawning succeeded.");

                ModelsTestPlugin.Log.LogInfo(
                    "[TEST] Model ID: " + model.Id);

                ModelsTestPlugin.Log.LogInfo(
                    "[TEST] Source path: " + model.SourcePath);

                ModelsTestPlugin.Log.LogInfo(
                    "[TEST] Root object: " + model.Root.name);

                if (attachAnimation)
                    InitializeAnimation(model);

                return model;
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Model] Spawn failed: " + ex);

                return null;
            }
        }

        private void TryLoadModel()
        {
            modelLoadAttempted = true;

            currentCamera = FindUsableCamera();

            if (currentCamera == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Camera] No usable camera yet. Retrying in three seconds.");

                modelLoadAttempted = false;
                nextModelAttemptTime = Time.unscaledTime + 3f;
                return;
            }

            SpawnModel(true);
        }

        private void SpawnAdditionalModel()
        {
            SpawnModel(true);
        }

        // ---------------------------------------------------------
        // ASYNC LOADING
        // ---------------------------------------------------------

        private void TestAsyncLoading()
        {
            if (!File.Exists(ModelsTestPlugin.ModelPath))
            {
                ModelsTestPlugin.Log.LogError(
                    "[Async] GLB file does not exist.");

                return;
            }

            if (currentCamera == null)
                currentCamera = FindUsableCamera();

            if (currentCamera == null)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Async] No usable camera.");

                return;
            }

            try
            {
                int index = spawnCounter++;

                ModelLoadOptions options =
                    CreateLoadOptions(index);

                ModelsTestPlugin.Log.LogInfo(
                    "[TEST] Starting asynchronous GLB loading.");

                ModelsTestPlugin.Owner.LoadGLBAsync(
                    ModelsTestPlugin.ModelPath,
                    model =>
                    {
                        if (!IsModelValid(model))
                        {
                            ModelsTestPlugin.Log.LogError(
                                "[Async] Completed with an invalid model.");

                            return;
                        }

                        AddModel(model);

                        ModelsTestPlugin.Log.LogInfo(
                            "[TEST PASS] Async loading succeeded. ID: " +
                            model.Id);
                    },
                    error =>
                    {
                        ModelsTestPlugin.Log.LogError(
                            "[Async] Loading failed: " + error);
                    },
                    options);
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Async] Could not start async loading: " + ex);
            }
        }

        // ---------------------------------------------------------
        // MODEL HANDLES AND LOOKUP
        // ---------------------------------------------------------

        private ModelInstance GetLatestModel()
        {
            for (int i = models.Count - 1; i >= 0; i--)
            {
                if (IsModelValid(models[i]))
                    return models[i];
            }

            return null;
        }

        private void TestModelLookup()
        {
            ModelInstance model = GetLatestModel();

            if (model == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Lookup] No model available to look up.");

                return;
            }

            ModelInstance globalResult =
                ModelAPI.GetModel(model.Id);

            ModelInstance ownerResult =
                ModelsTestPlugin.Owner.GetModel(model.Id);

            bool globalFound =
                IsModelValid(globalResult);

            bool ownerFound =
                IsModelValid(ownerResult);

            bool ownerMatches =
                ownerResult != null &&
                string.Equals(
                    ownerResult.OwnerId,
                    ModelsTestPlugin.Owner.Id,
                    StringComparison.Ordinal);

            ModelsTestPlugin.Log.LogInfo(
                "[Lookup] Model ID: " + model.Id);

            ModelsTestPlugin.Log.LogInfo(
                "[Lookup] Global lookup: " + globalFound);

            ModelsTestPlugin.Log.LogInfo(
                "[Lookup] Owner lookup: " + ownerFound);

            ModelsTestPlugin.Log.LogInfo(
                "[Lookup] Owner ID matches: " + ownerMatches);

            if (globalFound && ownerFound && ownerMatches)
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[TEST PASS] Model lookup and ownership lookup succeeded.");
            }
            else
            {
                ModelsTestPlugin.Log.LogError(
                    "[TEST FAIL] Model lookup returned an unexpected result.");
            }
        }

        private void TestOwnership()
        {
            ModelInstance model = GetLatestModel();

            if (model == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Ownership] No model available.");

                return;
            }

            bool ownerIdMatches =
                string.Equals(
                    model.OwnerId,
                    ModelsTestPlugin.Owner.Id,
                    StringComparison.Ordinal);

            ModelInstance ownedModel =
                ModelsTestPlugin.Owner.GetModel(model.Id);

            bool ownerCanFindModel =
                IsModelValid(ownedModel);

            ModelsTestPlugin.Log.LogInfo(
                "[Ownership] Model ID: " + model.Id);

            ModelsTestPlugin.Log.LogInfo(
                "[Ownership] Model Owner ID: " + model.OwnerId);

            ModelsTestPlugin.Log.LogInfo(
                "[Ownership] Current Owner ID: " +
                ModelsTestPlugin.Owner.Id);

            ModelsTestPlugin.Log.LogInfo(
                "[Ownership] Owner can retrieve model: " +
                ownerCanFindModel);

            if (ownerIdMatches && ownerCanFindModel)
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[TEST PASS] Ownership test succeeded.");
            }
            else
            {
                ModelsTestPlugin.Log.LogError(
                    "[TEST FAIL] Ownership test failed.");
            }
        }

        // ---------------------------------------------------------
        // TRANSFORM CONTROLS
        // ---------------------------------------------------------

        private void MoveLatestModel()
        {
            ModelInstance model = GetLatestModel();

            if (model == null)
                return;

            Vector3 position = model.Root.transform.localPosition;
            position.x += 0.25f;

            model.SetLocalPosition(position);

            ModelsTestPlugin.Log.LogInfo(
                "[Transform] Moved model to: " + position);
        }

        private void RotateLatestModel()
        {
            ModelInstance model = GetLatestModel();

            if (model == null)
                return;

            Vector3 rotation =
                model.Root.transform.localEulerAngles;

            rotation.y += 45f;

            model.SetLocalRotation(rotation);

            ModelsTestPlugin.Log.LogInfo(
                "[Transform] Rotated model to: " + rotation);
        }

        private void ScaleLatestModel()
        {
            ModelInstance model = GetLatestModel();

            if (model == null)
                return;

            Vector3 scale = model.Root.transform.localScale;
            scale *= 1.25f;

            model.SetScale(scale);

            ModelsTestPlugin.Log.LogInfo(
                "[Transform] Scaled model to: " + scale);
        }

        // ---------------------------------------------------------
        // DESTROY AND UNLOAD
        // ---------------------------------------------------------

        private void DestroyLatestModel()
        {
            if (pendingDestroyModel != null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Destroy] Waiting for previous destruction verification.");

                return;
            }

            ModelInstance model = GetLatestModel();

            if (model == null)
            {
                ModelsTestPlugin.Log.LogWarning(
                    "[Destroy] No model available.");

                return;
            }

            string id = model.Id;

            model.Destroy();
            models.Remove(model);

            if (instance == model)
                instance = GetLatestModel();

            if (animationPlayer != null && instance == null)
            {
                animationPlayer = null;
                animationStarted = false;
            }

            bool removedFromRegistry =
                ModelAPI.GetModel(id) == null;

            ModelsTestPlugin.Log.LogInfo(
                "[Destroy] Requested destruction: " + id);

            ModelsTestPlugin.Log.LogInfo(
                "[Destroy] Removed from registry: " +
                removedFromRegistry);

            pendingDestroyModel = model;
            pendingDestroyId = id;
            pendingDestroyCheckFrame = Time.frameCount;
        }

        private void VerifyPendingDestroy()
        {
            if (pendingDestroyModel == null)
                return;

            if (Time.frameCount <= pendingDestroyCheckFrame)
                return;

            ModelInstance model = pendingDestroyModel;
            string id = pendingDestroyId;

            bool destroyed =
                !model.IsValid ||
                model.Root == null;

            bool removedFromRegistry =
                ModelAPI.GetModel(id) == null;

            ModelsTestPlugin.Log.LogInfo(
                "[Destroy] Model ID: " + id);

            ModelsTestPlugin.Log.LogInfo(
                "[Destroy] Root destroyed: " + destroyed);

            ModelsTestPlugin.Log.LogInfo(
                "[Destroy] Removed from registry: " +
                removedFromRegistry);

            if (destroyed && removedFromRegistry)
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[TEST PASS] Model destruction succeeded.");
            }
            else
            {
                ModelsTestPlugin.Log.LogError(
                    "[TEST FAIL] Model destruction verification failed.");
            }

            pendingDestroyModel = null;
            pendingDestroyId = null;
            pendingDestroyCheckFrame = -1;
        }

        private void TestUnloadAll()
        {
            int before = ModelsTestPlugin.Owner.GetModels().Count;

            ModelsTestPlugin.Owner.UnloadAll();

            models.Clear();
            instance = null;
            animationPlayer = null;
            animationPlayers.Clear();
            animationStarted = false;
            animationsEnabled = true;
            animationNames.Clear();
            currentAnimationIndex = -1;

            int after = ModelsTestPlugin.Owner.GetModels().Count;

            ModelsTestPlugin.Log.LogInfo(
                "[Unload] Models before: " + before);

            ModelsTestPlugin.Log.LogInfo(
                "[Unload] Models after: " + after);

            if (after == 0)
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[TEST PASS] Owner.UnloadAll succeeded.");
            }
            else
            {
                ModelsTestPlugin.Log.LogError(
                    "[TEST FAIL] Owner.UnloadAll left registered models.");
            }
        }

        // ---------------------------------------------------------
        // OWNER LIFECYCLE
        // ---------------------------------------------------------

        private void TestOwnerDisposal()
        {
            ModelOwner oldOwner = ModelsTestPlugin.Owner;

            if (oldOwner == null)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Owner] No owner exists.");

                return;
            }

            int before = oldOwner.GetModels().Count;
            string oldOwnerId = oldOwner.Id;

            oldOwner.Dispose();

            bool disposed = oldOwner.IsDisposed;
            int remaining = oldOwner.GetModels().Count;

            ModelsTestPlugin.Log.LogInfo(
                "[Owner] Disposed owner: " + oldOwnerId);

            ModelsTestPlugin.Log.LogInfo(
                "[Owner] Models before disposal: " + before);

            ModelsTestPlugin.Log.LogInfo(
                "[Owner] Models after disposal: " + remaining);

            if (disposed && remaining == 0)
            {
                ModelsTestPlugin.Log.LogInfo(
                    "[TEST PASS] Owner disposal succeeded.");
            }
            else
            {
                ModelsTestPlugin.Log.LogError(
                    "[TEST FAIL] Owner disposal verification failed.");
            }

            models.Clear();
            instance = null;
            animationPlayer = null;
            animationPlayers.Clear();
            animationStarted = false;
            animationsEnabled = true;
            animationNames.Clear();
            currentAnimationIndex = -1;

            ModelsTestPlugin.Owner = ModelAPI.CreateOwner(
                "somerandommaid.ultradroid.models.test");

            ModelsTestPlugin.Log.LogInfo(
                "[Owner] New owner created: " +
                ModelsTestPlugin.Owner.Id);
        }

        private void TestAsyncOwnerCancellation()
        {
            if (!File.Exists(ModelsTestPlugin.ModelPath))
            {
                ModelsTestPlugin.Log.LogError(
                    "[Async Cancellation] GLB file does not exist.");

                return;
            }

            if (currentCamera == null)
                currentCamera = FindUsableCamera();

            if (currentCamera == null)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Async Cancellation] No usable camera.");

                return;
            }

            ModelOwner oldOwner = ModelsTestPlugin.Owner;

            try
            {
                ModelLoadOptions options =
                    CreateLoadOptions(spawnCounter++);

                ModelsTestPlugin.Log.LogInfo(
                    "[TEST] Starting async load, then disposing its owner.");

                oldOwner.LoadGLBAsync(
                    ModelsTestPlugin.ModelPath,
                    model =>
                    {
                        ModelsTestPlugin.Log.LogError(
                            "[TEST FAIL] Async load completed successfully " +
                            "after its owner was disposed.");

                        model?.Destroy();
                    },
                    error =>
                    {
                        if (error is OperationCanceledException)
                        {
                            ModelsTestPlugin.Log.LogInfo(
                                "[TEST PASS] Async loading was cancelled " +
                                "after owner disposal.");
                        }
                        else
                        {
                            ModelsTestPlugin.Log.LogWarning(
                                "[Async Cancellation] Received error: " +
                                error);
                        }
                    },
                    options);

                oldOwner.Dispose();

                ModelsTestPlugin.Owner = ModelAPI.CreateOwner(
                    "somerandommaid.ultradroid.models.test");

                models.Clear();
                instance = null;
                animationPlayer = null;
                animationPlayers.Clear();
                animationStarted = false;
                animationsEnabled = true;
                animationNames.Clear();
                currentAnimationIndex = -1;

                ModelsTestPlugin.Log.LogInfo(
                    "[Async Cancellation] Old owner disposed; new owner created.");
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Async Cancellation] Test failed to start: " + ex);
            }
        }

        // ---------------------------------------------------------
        // ANIMATION TESTS
        // ---------------------------------------------------------

        private void InitializeAnimation(ModelInstance model)
        {
            if (!IsModelValid(model))
                return;

            int animationCount = model.Animations.Count;

            ModelsTestPlugin.Log.LogInfo(
                "[Animation] Supported clips found: " +
                animationCount);

            animationNames.Clear();
            currentAnimationIndex = -1;
            animationPlayer = null;
            animationStarted = false;

            for (int i = 0; i < animationCount; i++)
            {
                ModelAnimationClip clip = model.Animations[i];

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
                ModelsTestPlugin.Log.LogWarning(
                    "[Animation] No usable animation clips found.");

                return;
            }

            animationPlayer =
                GlbAnimationPlayerFactory.Attach(model);

            if (animationPlayer == null)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Animation] Failed to attach animation player.");

                return;
            }

            animationPlayers.Add(animationPlayer);
            animationPlayer.Loop = true;
            animationPlayer.PlaybackSpeed = animationsEnabled ? 1f : 0f;

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

        private bool PlayAnimationAtIndex(int index)
        {
            if (animationPlayer == null ||
                index < 0 ||
                index >= animationNames.Count)
                return false;

            string clipName = animationNames[index];

            animationPlayer.Loop = true;
            animationPlayer.PlaybackSpeed = animationsEnabled ? 1f : 0f;

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

        private void ToggleAllAnimations()
        {
            animationsEnabled = !animationsEnabled;

            foreach (GlbAnimationPlayer player in animationPlayers)
            {
                if (player == null)
                    continue;

                player.PlaybackSpeed = animationsEnabled ? 1f : 0f;
            }

            ModelsTestPlugin.Log.LogInfo(
                "[Performance Test] Animations: " +
                (animationsEnabled ? "ENABLED" : "DISABLED") +
                " | Players: " + animationPlayers.Count);
        }

        // ---------------------------------------------------------
        // TOUCH BUTTON REGISTRATION
        // ---------------------------------------------------------

        private void AddTestButton(
            string id,
            string label,
            Vector2 position,
            Action callback)
        {
            ButtonHandle handle =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = id,
                    Label = label,
                    PositionOffset = position,
                    ScaleMultiplier = 1f,
                    ApplyTint = false,
                    OnClick = callback
                });

            if (handle != null)
                testButtons.Add(handle);
        }

        private void RegisterTestButtons()
        {
            if (testButtonsRegistered)
                return;

            testButtonsRegistered = true;

            AddTestButton(
                "ULTRADROID.Models.Spawn",
                "SPAWN",
                new Vector2(-240f, 90f),
                SpawnAdditionalModel);

            AddTestButton(
                "ULTRADROID.Models.AsyncSpawn",
                "ASYNC",
                new Vector2(0f, 90f),
                TestAsyncLoading);

            AddTestButton(
                "ULTRADROID.Models.Destroy",
                "DESTROY",
                new Vector2(240f, 90f),
                DestroyLatestModel);

            AddTestButton(
                "ULTRADROID.Models.Move",
                "MOVE",
                new Vector2(-240f, 180f),
                MoveLatestModel);

            AddTestButton(
                "ULTRADROID.Models.Rotate",
                "ROTATE",
                new Vector2(0f, 180f),
                RotateLatestModel);

            AddTestButton(
                "ULTRADROID.Models.Scale",
                "SCALE",
                new Vector2(240f, 180f),
                ScaleLatestModel);

            AddTestButton(
                "ULTRADROID.Models.ToggleAllAnimations",
                "ANIM ON/OFF",
                new Vector2(0f, 450f),
                ToggleAllAnimations);

            AddTestButton(
                "ULTRADROID.Models.Lookup",
                "LOOKUP",
                new Vector2(-240f, 270f),
                TestModelLookup);

            AddTestButton(
                "ULTRADROID.Models.Ownership",
                "OWNER",
                new Vector2(0f, 270f),
                TestOwnership);

            AddTestButton(
                "ULTRADROID.Models.UnloadAll",
                "UNLOAD ALL",
                new Vector2(240f, 270f),
                TestUnloadAll);

            AddTestButton(
                "ULTRADROID.Models.DisposeOwner",
                "DISPOSE OWNER",
                new Vector2(-120f, 360f),
                TestOwnerDisposal);

            AddTestButton(
                "ULTRADROID.Models.AsyncCancel",
                "ASYNC CANCEL",
                new Vector2(120f, 360f),
                TestAsyncOwnerCancellation);

            ModelsTestPlugin.Log.LogInfo(
                "[Tests] Model management test buttons registered.");
        }

        private void RegisterAnimationButtons()
        {
            if (previousAnimationButton != null ||
                nextAnimationButton != null ||
                layerToggleButton != null)
                return;

            previousAnimationButton =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = "ULTRADROID.Models.PreviousAnimation",
                    Label = "PREV ANIM",
                    PositionOffset = new Vector2(-120f, -90f),
                    ScaleMultiplier = 1f,
                    ApplyTint = false,
                    OnClick = PlayPreviousAnimation
                });

            nextAnimationButton =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = "ULTRADROID.Models.NextAnimation",
                    Label = "NEXT ANIM",
                    PositionOffset = new Vector2(120f, -90f),
                    ScaleMultiplier = 1f,
                    ApplyTint = false,
                    OnClick = PlayNextAnimation
                });

            layerToggleButton =
                ULTRADROIDControls.AddButton(new ButtonConfig
                {
                    Id = "ULTRADROID.Models.ToggleAnimationLayer",
                    Label = "LAYER",
                    PositionOffset = new Vector2(0f, -180f),
                    ScaleMultiplier = 1f,
                    ApplyTint = false,
                    OnClick = ToggleTestLayer
                });

            ModelsTestPlugin.Log.LogInfo(
                "[Animation] Animation test buttons registered.");
        }

        // ---------------------------------------------------------
        // CLEANUP
        // ---------------------------------------------------------

        private void DestroyCurrentModels()
        {
            DisposeAnimationButtons();
            DisposeTestButtons();

            animationPlayer = null;
            animationPlayers.Clear();
            animationStarted = false;
            animationsEnabled = true;
            testLayerEnabled = false;

            animationNames.Clear();
            currentAnimationIndex = -1;

            ModelOwner owner = ModelsTestPlugin.Owner;

            if (owner != null && !owner.IsDisposed)
                owner.UnloadAll();

            models.Clear();
            instance = null;
        }

        private void DisposeAnimationButtons()
        {
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
        }

        private void DisposeTestButtons()
        {
            foreach (ButtonHandle button in testButtons)
            {
                if (button != null)
                    button.Dispose();
            }

            testButtons.Clear();
            testButtonsRegistered = false;
        }

        public void OnDestroy()
        {
            DestroyCurrentModels();

            ModelsTestPlugin.Owner?.Dispose();
            ModelsTestPlugin.Owner = null;
        }
    }
}
