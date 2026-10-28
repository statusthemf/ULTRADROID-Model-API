
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class GlbAnimationPlayer : MonoBehaviour
    {
        private int stateId;

        public GlbAnimationPlayer(IntPtr ptr) : base(ptr)
        {
        }

        // Only an integer crosses the IL2CPP component boundary.
        public void BindState(int id)
        {
            stateId = id;
        }

        public bool Loop
        {
            get => GlbAnimationPlayerFactory.GetLoop(stateId);
            set => GlbAnimationPlayerFactory.SetLoop(stateId, value);
        }

        public float PlaybackSpeed
        {
            get => GlbAnimationPlayerFactory.GetSpeed(stateId);
            set => GlbAnimationPlayerFactory.SetSpeed(stateId, value);
        }

        public bool IsPlaying =>
            GlbAnimationPlayerFactory.IsPlaying(stateId);

        public string CurrentClipName =>
            GlbAnimationPlayerFactory.GetClipName(stateId);

        public float CurrentTime =>
            GlbAnimationPlayerFactory.GetTime(stateId);

        public float Duration =>
            GlbAnimationPlayerFactory.GetDuration(stateId);

        public bool Play(string clipName)
        {
            return GlbAnimationPlayerFactory.Play(stateId, clipName);
        }

        public bool Play(int clipIndex)
        {
            return GlbAnimationPlayerFactory.Play(stateId, clipIndex);
        }

        public void Pause()
        {
            GlbAnimationPlayerFactory.Pause(stateId);
        }

        public void Resume()
        {
            GlbAnimationPlayerFactory.Resume(stateId);
        }

        public void Stop()
        {
            GlbAnimationPlayerFactory.Stop(stateId);
        }

        public void Seek(float time)
        {
            GlbAnimationPlayerFactory.Seek(stateId, time);
        }

        public void Update()
        {
            GlbAnimationPlayerFactory.Tick(stateId, Time.deltaTime);
        }

        public void OnDestroy()
        {
            GlbAnimationPlayerFactory.Release(stateId);
        }
    }

    public static class GlbAnimationPlayerFactory
    {
        private sealed class PlaybackState
        {
            public ModelInstance Model;
            public ModelAnimationClip Clip;
            public float Time;
            public float Speed = 1f;
            public bool Loop = true;
            public bool Playing;
        }

        private static readonly Dictionary<int, PlaybackState> States =
            new Dictionary<int, PlaybackState>();

        private static int nextId = 1;

        public static GlbAnimationPlayer Attach(ModelInstance model)
        {
            if (model == null || !model.IsValid || model.Root == null)
                throw new ArgumentException("A valid model is required.");

            GlbAnimationPlayer player =
                model.Root.AddComponent<GlbAnimationPlayer>();

            int id = nextId++;

            States[id] = new PlaybackState
            {
                Model = model
            };

            player.BindState(id);
            return player;
        }

        private static PlaybackState GetState(int id)
        {
            if (id == 0 || !States.TryGetValue(id, out PlaybackState state))
                return null;

            if (state.Model == null || !state.Model.IsValid)
                return null;

            return state;
        }

        public static bool Play(int id, string name)
        {
            PlaybackState state = GetState(id);

            if (state == null || string.IsNullOrWhiteSpace(name))
                return false;

            for (int i = 0; i < state.Model.Animations.Count; i++)
            {
                ModelAnimationClip clip = state.Model.Animations[i];

                if (string.Equals(
                    clip.Name, name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    state.Clip = clip;
                    state.Time = 0f;
                    state.Playing = true;
                    Apply(state, 0f);
                    return true;
                }
            }

            return false;
        }

        public static bool Play(int id, int index)
        {
            PlaybackState state = GetState(id);

            if (state == null ||
                index < 0 ||
                index >= state.Model.Animations.Count)
                return false;

            state.Clip = state.Model.Animations[index];
            state.Time = 0f;
            state.Playing = true;
            Apply(state, 0f);
            return true;
        }

        public static void Pause(int id)
        {
            PlaybackState state = GetState(id);
            if (state != null)
                state.Playing = false;
        }

        public static void Resume(int id)
        {
            PlaybackState state = GetState(id);
            if (state != null && state.Clip != null)
                state.Playing = true;
        }

        public static void Stop(int id)
        {
            PlaybackState state = GetState(id);
            if (state == null)
                return;

            state.Playing = false;
            state.Time = 0f;

            if (state.Clip != null)
                Apply(state, 0f);
        }

        public static void Seek(int id, float time)
        {
            PlaybackState state = GetState(id);
            if (state == null || state.Clip == null)
                return;

            state.Time = Mathf.Clamp(time, 0f, state.Clip.Duration);
            Apply(state, state.Time);
        }

        public static bool IsPlaying(int id)
        {
            PlaybackState state = GetState(id);
            return state != null && state.Playing;
        }

        public static string GetClipName(int id)
        {
            PlaybackState state = GetState(id);
            return state != null && state.Clip != null
                ? state.Clip.Name : null;
        }

        public static float GetTime(int id)
        {
            PlaybackState state = GetState(id);
            return state != null ? state.Time : 0f;
        }

        public static float GetDuration(int id)
        {
            PlaybackState state = GetState(id);
            return state != null && state.Clip != null
                ? state.Clip.Duration : 0f;
        }

        public static bool GetLoop(int id)
        {
            PlaybackState state = GetState(id);
            return state != null && state.Loop;
        }

        public static void SetLoop(int id, bool value)
        {
            PlaybackState state = GetState(id);
            if (state != null)
                state.Loop = value;
        }

        public static float GetSpeed(int id)
        {
            PlaybackState state = GetState(id);
            return state != null ? state.Speed : 1f;
        }

        public static void SetSpeed(int id, float value)
        {
            PlaybackState state = GetState(id);
            if (state != null && !float.IsNaN(value) &&
                !float.IsInfinity(value))
                state.Speed = value;
        }

        public static void Tick(int id, float deltaTime)
        {
            PlaybackState state = GetState(id);

            if (state == null || !state.Playing || state.Clip == null)
                return;

            float duration = state.Clip.Duration;

            if (duration <= 0f)
            {
                Apply(state, 0f);
                if (!state.Loop)
                    state.Playing = false;
                return;
            }

            state.Time += deltaTime * state.Speed;

            if (state.Loop)
            {
                state.Time %= duration;

                if (state.Time < 0f)
                    state.Time += duration;
            }
            else
            {
                if (state.Time >= duration)
                {
                    state.Time = duration;
                    Apply(state, state.Time);
                    state.Playing = false;
                    return;
                }

                if (state.Time <= 0f && state.Speed < 0f)
                {
                    state.Time = 0f;
                    Apply(state, 0f);
                    state.Playing = false;
                    return;
                }
            }

            Apply(state, state.Time);
        }

        public static void Release(int id)
        {
            if (id != 0)
                States.Remove(id);
        }

        private static void Apply(PlaybackState state, float time)
        {
            if (state == null || state.Clip == null)
                return;

            foreach (ModelAnimationChannel channel in state.Clip.Channels)
            {
                Transform target = channel.Target;

                if (target == null)
                    continue;

                float[] times = channel.Times;
                Vector4[] values = channel.Values;

                if (times == null || values == null ||
                    times.Length == 0 || times.Length != values.Length)
                    continue;

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
                    int mid = low + (high - low) / 2;

                    if (times[mid] <= time)
                        low = mid;
                    else
                        high = mid;
                }

                if (channel.Interpolation == ModelAnimationInterpolation.Step)
                {
                    ApplyValue(target, channel.Path, values[low]);
                    continue;
                }

                float length = times[high] - times[low];
                float t = length > 0f
                    ? (time - times[low]) / length : 0f;

                if (channel.Path == ModelAnimationPath.Rotation)
                {
                    Quaternion a = new Quaternion(
                        values[low].x, values[low].y,
                        values[low].z, values[low].w);

                    Quaternion b = new Quaternion(
                        values[high].x, values[high].y,
                        values[high].z, values[high].w);

                    target.localRotation = Quaternion.Slerp(a, b, t);
                }
                else
                {
                    ApplyValue(
                        target,
                        channel.Path,
                        Vector4.Lerp(values[low], values[high], t));
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
                    target.localPosition =
                        new Vector3(value.x, value.y, value.z);
                    break;

                case ModelAnimationPath.Rotation:
                    target.localRotation = new Quaternion(
                        value.x, value.y, value.z, value.w);
                    break;

                case ModelAnimationPath.Scale:
                    target.localScale =
                        new Vector3(value.x, value.y, value.z);
                    break;
            }
        }
    }
}
