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
                    "[Models Test] Playback test suite initialized.");
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

        // Automatic playback test state.
        // 0 = start looping test
        // 1 = check looping, then pause
        // 2 = check pause, then resume
        // 3 = check resume and test seek
        // 4 = check half speed
        // 5 = check reverse playback
        // 6 = check non-looping playback
        // 7 = all tests completed
        private int testStage = -1;
        private float testStageStartTime;
        private float pausedTime;
        private float reverseStartTime;
        private float testDuration;

        // TEST 1: Track an actual animation-time wrap.
        private float loopPreviousTime;
        private bool loopWrapDetected;

        private bool testSuiteStarted;

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

                UpdatePlaybackTests();
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
            testSuiteStarted = false;
            testStage = -1;

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
            testSuiteStarted = false;
            testStage = -1;
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
                    Position = new Vector3(0f, 0f, 1f),
                    RotationEuler = Vector3.zero,
                    Scale = new Vector3(0.1f, 0.1f, 0.1f),
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
                    "[Animation] Supported clips found: " +
                    animationCount);

                for (int i = 0; i < animationCount; i++)
                {
                    ModelsTestPlugin.Log.LogInfo(
                        "[Animation] Clip " + i + ": '" +
                        instance.Animations[i].Name +
                        "' (duration: " +
                        instance.Animations[i].Duration +
                        " seconds)");
                }

                if (animationCount == 0)
                {
                    ModelsTestPlugin.Log.LogWarning(
                        "[Animation] No supported animation clips found.");

                    return;
                }

                animationPlayer =
                    GlbAnimationPlayerFactory.Attach(instance);

                if (animationPlayer == null)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Animation] Failed to attach animation player.");

                    return;
                }

                testDuration = FindClipDuration("Bow");

                if (testDuration <= 0f)
                {
                    ModelsTestPlugin.Log.LogError(
                        "[Animation] Bow was not found or has invalid duration.");

                    return;
                }

                ModelsTestPlugin.Log.LogInfo(
                    "[Animation] Player attached. Bow duration: " +
                    testDuration);

                StartPlaybackTestSuite();
            }
            catch (Exception ex)
            {
                ModelsTestPlugin.Log.LogError(
                    "[Model] LOAD FAILED: " + ex);
            }
        }

        private float FindClipDuration(string clipName)
        {
            for (int i = 0; i < instance.Animations.Count; i++)
            {
                ModelAnimationClip clip = instance.Animations[i];

                if (clip != null &&
                    string.Equals(
                        clip.Name,
                        clipName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return clip.Duration;
                }
            }

            return 0f;
        }

        private void StartPlaybackTestSuite()
        {
            if (animationPlayer == null)
                return;

            testSuiteStarted = true;

            ModelsTestPlugin.Log.LogInfo(
                "========================================");

            ModelsTestPlugin.Log.LogInfo(
                "[TEST SUITE] Starting all six playback tests.");

            ModelsTestPlugin.Log.LogInfo(
                "[TEST SUITE] Tests will run automatically in sequence.");

            // TEST 1: Normal looping.
            animationPlayer.Loop = true;
            animationPlayer.PlaybackSpeed = 1f;

            if (!animationPlayer.Play("Bow"))
            {
                LogTestResult(
                    "Normal looping",
                    false,
                    "Play(\"Bow\") failed.");

                testStage = 7;
                return;
            }

            testStage = 1;
            testStageStartTime = Time.unscaledTime;

            loopPreviousTime = animationPlayer.CurrentTime;
            loopWrapDetected = false;

            ModelsTestPlugin.Log.LogInfo(
                "[TEST 1/6] Normal looping started at speed 1x.");
        }

        private void UpdatePlaybackTests()
        {
            if (!testSuiteStarted ||
                animationPlayer == null ||
                instance == null ||
                !instance.IsValid ||
                testStage < 1 ||
                testStage > 6)
                return;

            float elapsed = Time.unscaledTime - testStageStartTime;

            switch (testStage)
            {
                case 1:
                {
                    float currentTime = animationPlayer.CurrentTime;

                    // A looping animation should jump back toward zero
                    // when it passes the end of the clip.
                    if (currentTime + 0.05f < loopPreviousTime)
                    {
                        loopWrapDetected = true;
                    }

                    loopPreviousTime = currentTime;

                    // Wait for an actual wrap, allowing extra time for
                    // slow frames or delayed animation updates.
                    float timeout = testDuration * 2f + 2f;

                    if (!loopWrapDetected && elapsed < timeout)
                        return;

                    bool loopPassed =
                        animationPlayer.IsPlaying &&
                        animationPlayer.Loop &&
                        loopWrapDetected;

                    LogTestResult(
                        "Normal looping",
                        loopPassed,
                        "Playing=" + animationPlayer.IsPlaying +
                        ", Loop=" + animationPlayer.Loop +
                        ", Time=" + animationPlayer.CurrentTime +
                        ", Wrap detected=" + loopWrapDetected +
                        ", Elapsed=" + elapsed);

                    // TEST 2: Pause.
                    animationPlayer.Pause();
                    pausedTime = animationPlayer.CurrentTime;

                    testStage = 2;
                    testStageStartTime = Time.unscaledTime;

                    ModelsTestPlugin.Log.LogInfo(
                        "[TEST 2/6] Paused at time " + pausedTime + ".");

                    return;
                }

                case 2:
                {
                    // Leave the player paused long enough to check stability.
                    if (elapsed < 0.6f)
                        return;

                    float pauseDifference = Mathf.Abs(
                        animationPlayer.CurrentTime - pausedTime);

                    bool pausePassed =
                        !animationPlayer.IsPlaying &&
                        pauseDifference <= 0.05f;

                    LogTestResult(
                        "Pause",
                        pausePassed,
                        "Playing=" + animationPlayer.IsPlaying +
                        ", Time difference=" + pauseDifference);

                    // TEST 3: Resume.
                    animationPlayer.Resume();

                    testStage = 3;
                    testStageStartTime = Time.unscaledTime;

                    ModelsTestPlugin.Log.LogInfo(
                        "[TEST 3/6] Resumed playback.");

                    return;
                }

                case 3:
                {
                    if (elapsed < 0.6f)
                        return;

                    float resumeDifference = Mathf.Abs(
                        animationPlayer.CurrentTime - pausedTime);

                    bool resumePassed =
                        animationPlayer.IsPlaying &&
                        resumeDifference > 0.05f;

                    LogTestResult(
                        "Resume",
                        resumePassed,
                        "Playing=" + animationPlayer.IsPlaying +
                        ", Time difference from paused pose=" +
                        resumeDifference);

                    // TEST 4: Seek to the middle of the clip.
                    animationPlayer.Pause();

                    float seekTarget = testDuration * 0.5f;
                    animationPlayer.Seek(seekTarget);

                    float seekDifference = Mathf.Abs(
                        animationPlayer.CurrentTime - seekTarget);

                    bool seekPassed = seekDifference <= 0.05f;

                    LogTestResult(
                        "Seek",
                        seekPassed,
                        "Target=" + seekTarget +
                        ", Actual=" + animationPlayer.CurrentTime +
                        ", Difference=" + seekDifference);

                    // TEST 5: Half speed.
                    animationPlayer.Loop = true;
                    animationPlayer.PlaybackSpeed = 0.5f;

                    if (!animationPlayer.Play("Bow"))
                    {
                        LogTestResult(
                            "Half speed",
                            false,
                            "Play(\"Bow\") failed.");

                        testStage = 7;
                        return;
                    }

                    testStage = 4;
                    testStageStartTime = Time.unscaledTime;

                    ModelsTestPlugin.Log.LogInfo(
                        "[TEST 5/6] Playing Bow at half speed.");

                    return;
                }

                case 4:
                {
                    // One second of real time should advance roughly 0.5
                    // seconds of animation time at speed 0.5.
                    if (elapsed < 1f)
                        return;

                    float halfSpeedTime = animationPlayer.CurrentTime;

                    bool halfSpeedPassed =
                        animationPlayer.IsPlaying &&
                        halfSpeedTime >= 0.25f &&
                        halfSpeedTime <= 0.85f;

                    LogTestResult(
                        "Half speed",
                        halfSpeedPassed,
                        "Expected about 0.5 seconds; actual=" +
                        halfSpeedTime);

                    // TEST 6: Reverse playback.
                    animationPlayer.Loop = true;
                    animationPlayer.PlaybackSpeed = 1f;
                    animationPlayer.Play("Bow");

                    reverseStartTime = testDuration * 0.75f;
                    animationPlayer.Seek(reverseStartTime);
                    animationPlayer.PlaybackSpeed = -1f;

                    testStage = 5;
                    testStageStartTime = Time.unscaledTime;

                    ModelsTestPlugin.Log.LogInfo(
                        "[TEST 6/6] Reverse playback started from time " +
                        reverseStartTime + ".");

                    return;
                }

                case 5:
                {
                    if (elapsed < 0.5f)
                        return;

                    float reverseCurrentTime =
                        animationPlayer.CurrentTime;

                    bool reversePassed =
                        animationPlayer.IsPlaying &&
                        reverseCurrentTime < reverseStartTime &&
                        reverseCurrentTime >= 0f;

                    LogTestResult(
                        "Reverse playback",
                        reversePassed,
                        "Start=" + reverseStartTime +
                        ", Actual=" + reverseCurrentTime +
                        ", Speed=" + animationPlayer.PlaybackSpeed);

                    // TEST 7: Non-looping playback.
                    animationPlayer.Loop = false;
                    animationPlayer.PlaybackSpeed = 1f;

                    if (!animationPlayer.Play("Bow"))
                    {
                        LogTestResult(
                            "Non-looping playback",
                            false,
                            "Play(\"Bow\") failed.");

                        testStage = 7;
                        FinishPlaybackTestSuite();
                        return;
                    }

                    testStage = 6;
                    testStageStartTime = Time.unscaledTime;

                    ModelsTestPlugin.Log.LogInfo(
                        "[TEST 7/7] Non-looping playback started.");

                    return;
                }

                case 6:
                {
                    // Allow the full clip to finish.
                    if (elapsed < testDuration + 0.5f)
                        return;

                    float finalTime = animationPlayer.CurrentTime;

                    bool nonLoopPassed =
                        !animationPlayer.IsPlaying &&
                        Mathf.Abs(finalTime - testDuration) <= 0.05f;

                    LogTestResult(
                        "Non-looping playback",
                        nonLoopPassed,
                        "Playing=" + animationPlayer.IsPlaying +
                        ", Final time=" + finalTime +
                        ", Duration=" + testDuration);

                    testStage = 7;
                    FinishPlaybackTestSuite();

                    return;
                }
            }
        }

        private void LogTestResult(
            string testName,
            bool passed,
            string details)
        {
            string result = passed ? "PASS" : "FAIL";

            ModelsTestPlugin.Log.LogInfo(
                "[TEST RESULT] " + result + " - " +
                testName + " | " + details);
        }

        private void FinishPlaybackTestSuite()
        {
            if (animationPlayer != null)
            {
                // Leave the model in a predictable state after testing.
                animationPlayer.Loop = true;
                animationPlayer.PlaybackSpeed = 1f;
                animationPlayer.Play("Bow");
            }

            ModelsTestPlugin.Log.LogInfo(
                "[TEST SUITE] All playback tests have finished.");

            ModelsTestPlugin.Log.LogInfo(
                "[TEST SUITE] Review the PASS/FAIL entries above.");

            ModelsTestPlugin.Log.LogInfo(
                "========================================");
        }

        public void OnDestroy()
        {
            DestroyCurrentModel();
        }

        private void DestroyCurrentModel()
        {
            animationPlayer = null;

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
