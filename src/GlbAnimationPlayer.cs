
using System;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class GlbAnimationPlayer : MonoBehaviour
    {
        public GlbAnimationPlayer(IntPtr ptr) : base(ptr)
        {
        }

        private ModelInstance model;
        private ModelAnimationClip currentClip;

        private float playbackTime;
        private bool isPlaying;
        private bool initialized;

        public bool Loop { get; set; } = true;

        public float PlaybackSpeed { get; set; } = 1f;

        public bool IsPlaying => isPlaying;

        public string CurrentClipName =>
            currentClip != null ? currentClip.Name : null;

        public float CurrentTime => playbackTime;

        public float Duration =>
            currentClip != null ? currentClip.Duration : 0f;

        internal void InitializeModel(ModelInstance modelInstance)
        {
            if (modelInstance == null || !modelInstance.IsValid)
                throw new ArgumentException("A valid ModelInstance is required.");

            model = modelInstance;
            currentClip = null;
            playbackTime = 0f;
            isPlaying = false;
            initialized = true;
        }

        public bool Play(string clipName)
        {
            if (!initialized || model == null || !model.IsValid)
                return false;

            if (string.IsNullOrWhiteSpace(clipName))
                return false;

            foreach (ModelAnimationClip clip in model.Animations)
            {
                if (string.Equals(
                    clip.Name,
                    clipName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    currentClip = clip;
                    playbackTime = 0f;
                    isPlaying = true;

                    ApplyAnimation(0f);
                    return true;
                }
            }

            return false;
        }

        public bool Play(int clipIndex)
        {
            if (!initialized || model == null || !model.IsValid)
                return false;

            if (clipIndex < 0 || clipIndex >= model.Animations.Count)
                return false;

            currentClip = model.Animations[clipIndex];
            playbackTime = 0f;
            isPlaying = true;

            ApplyAnimation(0f);
            return true;
        }

        public void Pause()
        {
            isPlaying = false;
        }

        public void Resume()
        {
            if (initialized &&
                model != null &&
                model.IsValid &&
                currentClip != null)
            {
                isPlaying = true;
            }
        }

        public void Stop()
        {
            isPlaying = false;
            playbackTime = 0f;

            if (currentClip != null)
                ApplyAnimation(0f);
        }

        public void Seek(float time)
        {
            if (currentClip == null)
                return;

            playbackTime = Mathf.Clamp(time, 0f, currentClip.Duration);
            ApplyAnimation(playbackTime);
        }

        public void Update()
        {
            if (!isPlaying ||
                currentClip == null ||
                model == null ||
                !model.IsValid)
            {
                return;
            }

            if (currentClip.Duration <= 0f)
            {
                playbackTime = 0f;
                ApplyAnimation(0f);

                if (!Loop)
                    isPlaying = false;

                return;
            }

            playbackTime += Time.deltaTime * PlaybackSpeed;

            if (Loop)
            {
                playbackTime %= currentClip.Duration;

                if (playbackTime < 0f)
                    playbackTime += currentClip.Duration;
            }
            else
            {
                if (playbackTime >= currentClip.Duration)
                {
                    playbackTime = currentClip.Duration;
                    ApplyAnimation(playbackTime);
                    isPlaying = false;
                    return;
                }

                if (playbackTime <= 0f && PlaybackSpeed < 0f)
                {
                    playbackTime = 0f;
                    ApplyAnimation(playbackTime);
                    isPlaying = false;
                    return;
                }
            }

            ApplyAnimation(playbackTime);
        }

        private void ApplyAnimation(float time)
        {
            if (currentClip == null)
                return;

            foreach (ModelAnimationChannel channel in currentClip.Channels)
            {
                Transform target = channel.Target;

                if (target == null)
                    continue;

                float[] times = channel.Times;
                Vector4[] values = channel.Values;

                if (times == null ||
                    values == null ||
                    times.Length == 0 ||
                    times.Length != values.Length)
                {
                    continue;
                }

                int last = times.Length - 1;

                if (time <= times[0])
                {
                    ApplyValue(target, channel.Path, values[0]);
                    continue;
                }

                if (time >= times[last])
                {
                    ApplyValue(target, channel.Path, values[last]);
                    continue;
                }

                int low = 0;
                int high = last;

                while (low + 1 < high)
                {
                    int middle = low + (high - low) / 2;

                    if (times[middle] <= time)
                        low = middle;
                    else
                        high = middle;
                }

                if (channel.Interpolation == ModelAnimationInterpolation.Step)
                {
                    ApplyValue(target, channel.Path, values[low]);
                    continue;
                }

                float segmentLength = times[high] - times[low];

                float t = segmentLength > 0f
                    ? (time - times[low]) / segmentLength
                    : 0f;

                if (channel.Path == ModelAnimationPath.Rotation)
                {
                    Quaternion a = new Quaternion(
                        values[low].x,
                        values[low].y,
                        values[low].z,
                        values[low].w);

                    Quaternion b = new Quaternion(
                        values[high].x,
                        values[high].y,
                        values[high].z,
                        values[high].w);

                    Quaternion rotation = Quaternion.Slerp(
                        a, b, t);

                    target.localRotation = rotation;
                }
                else
                {
                    Vector4 value = Vector4.Lerp(
                        values[low], values[high], t);

                    ApplyValue(target, channel.Path, value);
                }
            }
        }

        private static void ApplyValue(
            Transform target,
            ModelAnimationPath path,
            Vector4 value)
        {
            switch (path)
            {
                case ModelAnimationPath.Translation:
                    target.localPosition = new Vector3(
                        value.x, value.y, value.z);
                    break;

                case ModelAnimationPath.Rotation:
                    target.localRotation = new Quaternion(
                        value.x, value.y, value.z, value.w);
                    break;

                case ModelAnimationPath.Scale:
                    target.localScale = new Vector3(
                        value.x, value.y, value.z);
                    break;
            }
        }
    }

    public static class GlbAnimationPlayerFactory
    {
        public static GlbAnimationPlayer Attach(ModelInstance model)
        {
            if (model == null || !model.IsValid || model.Root == null)
                throw new ArgumentException("A valid model is required.");

            GlbAnimationPlayer player =
                model.Root.AddComponent<GlbAnimationPlayer>();

            player.InitializeModel(model);
            return player;
        }
    }
}
