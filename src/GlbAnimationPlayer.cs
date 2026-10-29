using System;
using System.Collections.Generic;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class GlbAnimationPlayer : MonoBehaviour
    {
        private int stateId;

        public GlbAnimationPlayer(IntPtr ptr) : base(ptr) { }

        public void BindState(int id) => stateId = id;

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

        public bool IsPlaying => GlbAnimationPlayerFactory.IsPlaying(stateId);
        public string CurrentClipName => GlbAnimationPlayerFactory.GetClipName(stateId);
        public float CurrentTime => GlbAnimationPlayerFactory.GetTime(stateId);
        public float Duration => GlbAnimationPlayerFactory.GetDuration(stateId);

        public bool Play(string name) => GlbAnimationPlayerFactory.Play(stateId, name);
        public bool Play(int index) => GlbAnimationPlayerFactory.Play(stateId, index);
        public void Pause() => GlbAnimationPlayerFactory.Pause(stateId);
        public void Resume() => GlbAnimationPlayerFactory.Resume(stateId);
        public void Stop() => GlbAnimationPlayerFactory.Stop(stateId);
        public void Seek(float time) => GlbAnimationPlayerFactory.Seek(stateId, time);

        public bool CrossFade(string name, float duration)
            => GlbAnimationPlayerFactory.CrossFade(stateId, name, duration);

        public void Queue(string name)
            => GlbAnimationPlayerFactory.Queue(stateId, name);

        public void ClearQueue()
            => GlbAnimationPlayerFactory.ClearQueue(stateId);

        public void SetLayer(string name, float weight, string[] allowedNodeNames = null)
            => GlbAnimationPlayerFactory.SetLayer(stateId, name, weight, allowedNodeNames);

        public void ClearLayers()
            => GlbAnimationPlayerFactory.ClearLayers(stateId);

        public bool RootMotionEnabled
        {
            get => GlbAnimationPlayerFactory.GetRootMotion(stateId);
            set => GlbAnimationPlayerFactory.SetRootMotion(stateId, value);
        }

        public void AddEvent(string clipName, float time, Action callback)
            => GlbAnimationPlayerFactory.AddEvent(stateId, clipName, time, callback);

        public void ClearEvents()
            => GlbAnimationPlayerFactory.ClearEvents(stateId);

        public void Update()
            => GlbAnimationPlayerFactory.Tick(stateId, Time.deltaTime);

        public void OnDestroy()
            => GlbAnimationPlayerFactory.Release(stateId);
    }

    public static class GlbAnimationPlayerFactory
    {
        private sealed class EventEntry
        {
            public string Clip;
            public float Time;
            public Action Callback;
        }

        private sealed class LayerEntry
        {
            public ModelAnimationClip Clip;
            public float Weight;
            public HashSet<string> AllowedNames;
        }

        private sealed class PlaybackState
        {
            public ModelInstance Model;
            public ModelAnimationClip Clip;
            public float Time;
            public float Speed = 1f;
            public bool Loop = true;
            public bool Playing;

            public ModelAnimationClip FadeFrom;
            public float FadeFromTime;
            public float FadeDuration;
            public float FadeElapsed;

            public readonly Queue<string> Queue = new Queue<string>();
            public readonly List<EventEntry> Events = new List<EventEntry>();
            public readonly List<LayerEntry> Layers = new List<LayerEntry>();

            public readonly Dictionary<Transform, Pose> OriginalPoses =
                new Dictionary<Transform, Pose>();

            public readonly Dictionary<SkinnedMeshRenderer, float[]> OriginalMorphWeights =
                new Dictionary<SkinnedMeshRenderer, float[]>();

            public bool RestorePoseOnStop = true;
            public bool RootMotion;
            public Vector3 PreviousRootPosition;
        }

        private struct Pose
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;

            public Pose(Transform transform)
            {
                Position = transform.localPosition;
                Rotation = transform.localRotation;
                Scale = transform.localScale;
            }

            public void Restore(Transform transform)
            {
                transform.localPosition = Position;
                transform.localRotation = Rotation;
                transform.localScale = Scale;
            }
        }

        private static readonly Dictionary<int, PlaybackState> States =
            new Dictionary<int, PlaybackState>();

        private static int nextId = 1;

        public static GlbAnimationPlayer Attach(ModelInstance model)
        {
            if (model == null || !model.IsValid || model.Root == null)
                throw new ArgumentException("A valid model is required.");

            int id = nextId++;

            PlaybackState state = new PlaybackState { Model = model };

            Transform[] transforms = model.Root.GetComponentsInChildren<Transform>(true);

            foreach (Transform transform in transforms)
                state.OriginalPoses[transform] = new Pose(transform);

            SkinnedMeshRenderer[] renderers =
                model.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer.sharedMesh == null)
                    continue;

                float[] weights = new float[renderer.sharedMesh.blendShapeCount];

                for (int i = 0; i < weights.Length; i++)
                    weights[i] = renderer.GetBlendShapeWeight(i);

                state.OriginalMorphWeights[renderer] = weights;
            }

            States[id] = state;

            GlbAnimationPlayer player = model.Root.AddComponent<GlbAnimationPlayer>();
            player.BindState(id);
            return player;
        }

        private static PlaybackState GetState(int id)
        {
            if (id == 0 || !States.TryGetValue(id, out PlaybackState state))
                return null;

            if (state.Model == null || !state.Model.IsValid || state.Model.Root == null)
            {
                States.Remove(id);
                return null;
            }

            return state;
        }

        private static ModelAnimationClip FindClip(PlaybackState state, string name)
        {
            if (state == null || string.IsNullOrWhiteSpace(name))
                return null;

            for (int i = 0; i < state.Model.Animations.Count; i++)
            {
                ModelAnimationClip clip = state.Model.Animations[i];

                if (clip != null && string.Equals(
                    clip.Name, name, StringComparison.OrdinalIgnoreCase))
                    return clip;
            }

            return null;
        }

        public static bool Play(int id, string name)
        {
            PlaybackState state = GetState(id);
            ModelAnimationClip clip = FindClip(state, name);

            if (clip == null)
                return false;

            state.Clip = clip;
            state.Time = 0f;
            state.Playing = true;
            state.FadeFrom = null;
            state.FadeDuration = 0f;
            state.FadeElapsed = 0f;
            Apply(state, 0f);
            return true;
        }

        public static bool Play(int id, int index)
        {
            PlaybackState state = GetState(id);

            if (state == null || index < 0 ||
                index >= state.Model.Animations.Count)
                return false;

            ModelAnimationClip clip = state.Model.Animations[index];

            if (clip == null)
                return false;

            state.Clip = clip;
            state.Time = 0f;
            state.Playing = true;
            state.FadeFrom = null;
            Apply(state, 0f);
            return true;
        }

        public static bool CrossFade(int id, string name, float duration)
        {
            PlaybackState state = GetState(id);
            ModelAnimationClip next = FindClip(state, name);

            if (state == null || next == null ||
                float.IsNaN(duration) || float.IsInfinity(duration) ||
                duration < 0f)
                return false;

            if (state.Clip == null || duration == 0f)
                return Play(id, name);

            state.FadeFrom = state.Clip;
            state.FadeFromTime = state.Time;
            state.FadeDuration = duration;
            state.FadeElapsed = 0f;
            state.Clip = next;
            state.Time = 0f;
            state.Playing = true;
            return true;
        }

        public static void Queue(int id, string name)
        {
            PlaybackState state = GetState(id);

            if (state != null && FindClip(state, name) != null)
                state.Queue.Enqueue(name);
        }

        public static void ClearQueue(int id)
        {
            PlaybackState state = GetState(id);
            state?.Queue.Clear();
        }

        public static void SetLayer(
            int id, string name, float weight, string[] allowedNodeNames = null)
        {
            PlaybackState state = GetState(id);
            ModelAnimationClip clip = FindClip(state, name);

            if (state == null || clip == null ||
                float.IsNaN(weight) || float.IsInfinity(weight))
                return;

            HashSet<string> allowed = null;

            if (allowedNodeNames != null)
                allowed = new HashSet<string>(
                    allowedNodeNames, StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < state.Layers.Count; i++)
            {
                if (state.Layers[i].Clip == clip)
                {
                    state.Layers[i].Weight = Mathf.Clamp01(weight);
                    state.Layers[i].AllowedNames = allowed;
                    return;
                }
            }

            state.Layers.Add(new LayerEntry
            {
                Clip = clip,
                Weight = Mathf.Clamp01(weight),
                AllowedNames = allowed
            });
        }

        public static void ClearLayers(int id)
        {
            PlaybackState state = GetState(id);
            state?.Layers.Clear();
        }

        public static bool GetRootMotion(int id)
        {
            PlaybackState state = GetState(id);
            return state != null && state.RootMotion;
        }

        public static void SetRootMotion(int id, bool enabled)
        {
            PlaybackState state = GetState(id);

            if (state == null)
                return;

            state.RootMotion = enabled;
            state.PreviousRootPosition = state.Model.Root.transform.position;
        }

        public static void AddEvent(
            int id, string clipName, float time, Action callback)
        {
            PlaybackState state = GetState(id);

            if (state == null || callback == null ||
                float.IsNaN(time) || float.IsInfinity(time) || time < 0f ||
                FindClip(state, clipName) == null)
                return;

            state.Events.Add(new EventEntry
            {
                Clip = clipName,
                Time = time,
                Callback = callback
            });
        }

        public static void ClearEvents(int id)
        {
            PlaybackState state = GetState(id);
            state?.Events.Clear();
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
            state.FadeFrom = null;
            state.FadeDuration = 0f;
            state.Queue.Clear();

            RestorePose(state);
        }

        public static void Seek(int id, float time)
        {
            PlaybackState state = GetState(id);

            if (state == null || state.Clip == null ||
                float.IsNaN(time) || float.IsInfinity(time))
                return;

            state.Time = Mathf.Clamp(time, 0f, Mathf.Max(0f, state.Clip.Duration));
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
            return state?.Clip?.Name;
        }

        public static float GetTime(int id)
        {
            PlaybackState state = GetState(id);
            return state != null ? state.Time : 0f;
        }

        public static float GetDuration(int id)
        {
            PlaybackState state = GetState(id);
            return state?.Clip != null ? state.Clip.Duration : 0f;
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

            if (state != null && !float.IsNaN(value) && !float.IsInfinity(value))
                state.Speed = value;
        }

        public static void Tick(int id, float deltaTime)
        {
            PlaybackState state = GetState(id);

            if (state == null || !state.Playing || state.Clip == null ||
                float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) ||
                deltaTime < 0f)
                return;

            float oldTime = state.Time;
            float duration = state.Clip.Duration;

            if (duration <= 0f)
            {
                state.Time = 0f;
                Apply(state, 0f);
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
            else if (state.Time >= duration)
            {
                state.Time = duration;
                FireEvents(state, oldTime, state.Time, false);
                Apply(state, state.Time);
                state.Playing = false;

                if (state.Queue.Count > 0)
                    Play(id, state.Queue.Dequeue());

                return;
            }
            else if (state.Time <= 0f && state.Speed < 0f)
            {
                state.Time = 0f;
                Apply(state, 0f);
                state.Playing = false;
                return;
            }

            FireEvents(state, oldTime, state.Time, state.Loop);

            if (state.FadeFrom != null)
            {
                state.FadeElapsed += deltaTime;

                if (state.FadeElapsed >= state.FadeDuration)
                {
                    state.FadeFrom = null;
                    state.FadeDuration = 0f;
                }
            }

            Apply(state, state.Time);

            if (state.RootMotion)
            {
                Transform root = state.Model.Root.transform;
                Vector3 current = root.position;
                Vector3 delta = current - state.PreviousRootPosition;
                root.position += delta;
                state.PreviousRootPosition = root.position;
            }
        }

        private static void FireEvents(
            PlaybackState state, float oldTime, float newTime, bool loop)
        {
            for (int i = 0; i < state.Events.Count; i++)
            {
                EventEntry entry = state.Events[i];

                if (!string.Equals(
                    entry.Clip, state.Clip.Name,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                bool crossed = oldTime <= newTime
                    ? entry.Time > oldTime && entry.Time <= newTime
                    : entry.Time > oldTime || entry.Time <= newTime;

                if (loop && newTime < oldTime)
                    crossed = entry.Time > oldTime || entry.Time <= newTime;

                if (crossed)
                {
                    try { entry.Callback?.Invoke(); }
                    catch (Exception ex) { Debug.LogError(ex.ToString()); }
                }
            }
        }

        private static void RestorePose(PlaybackState state)
        {
            foreach (KeyValuePair<Transform, Pose> item in state.OriginalPoses)
            {
                if (item.Key != null)
                    item.Value.Restore(item.Key);
            }

            foreach (KeyValuePair<SkinnedMeshRenderer, float[]> item
                in state.OriginalMorphWeights)
            {
                SkinnedMeshRenderer renderer = item.Key;

                if (renderer == null || renderer.sharedMesh == null)
                    continue;

                for (int i = 0;
                     i < item.Value.Length && i < renderer.sharedMesh.blendShapeCount;
                     i++)
                    renderer.SetBlendShapeWeight(i, item.Value[i]);
            }
        }

        private static void Apply(PlaybackState state, float time)
        {
            if (state == null || state.Clip == null)
                return;

            ApplyClip(state, state.Clip, time, 1f, null);

            if (state.FadeFrom != null && state.FadeDuration > 0f)
            {
                float alpha = Mathf.Clamp01(state.FadeElapsed / state.FadeDuration);

                // Crossfade blends the incoming pose over the outgoing pose.
                ApplyClip(state, state.FadeFrom, state.FadeFromTime, 1f - alpha, null);
                ApplyClip(state, state.Clip, time, alpha, null);
            }

            for (int i = 0; i < state.Layers.Count; i++)
            {
                LayerEntry layer = state.Layers[i];

                if (layer.Clip != null && layer.Weight > 0f)
                    ApplyClip(state, layer.Clip, time, layer.Weight, layer.AllowedNames);
            }
        }

        private static void ApplyClip(
            PlaybackState state,
            ModelAnimationClip clip,
            float time,
            float weight,
            HashSet<string> allowedNames)
        {
            if (clip == null || weight <= 0f)
                return;

            foreach (ModelAnimationChannel channel in clip.Channels)
            {
                Transform target = channel.Target;

                if (target == null)
                    continue;

                if (allowedNames != null && !allowedNames.Contains(target.name))
                    continue;

                float[] times = channel.Times;
                Vector4[] values = channel.Values;

                if (times == null || values == null ||
                    times.Length == 0 || times.Length != values.Length)
                    continue;

                int last = times.Length - 1;
                Vector4 value;

                if (time <= times[0])
                    value = values[0];
                else if (time >= times[last])
                    value = values[last];
                else
                {
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
                        value = values[low];
                    }
                    else
                    {
                        float dt = times[high] - times[low];
                        float t = dt > 0f ? (time - times[low]) / dt : 0f;

                        if (channel.Interpolation == ModelAnimationInterpolation.CubicSpline &&
                            channel.InTangents != null &&
                            channel.OutTangents != null &&
                            channel.InTangents.Length == values.Length &&
                            channel.OutTangents.Length == values.Length)
                        {
                            float t2 = t * t;
                            float t3 = t2 * t;

                            float h00 = 2f * t3 - 3f * t2 + 1f;
                            float h10 = t3 - 2f * t2 + t;
                            float h01 = -2f * t3 + 3f * t2;
                            float h11 = t3 - t2;

                            value =
                                values[low] * h00 +
                                channel.OutTangents[low] * (h10 * dt) +
                                values[high] * h01 +
                                channel.InTangents[high] * (h11 * dt);
                        }
                        else
                        {
                            value = Vector4.Lerp(values[low], values[high], t);
                        }
                    }
                }

                ApplyWeightedValue(target, channel.Path, value, weight);
            }
        }

        private static void ApplyWeightedValue(
            Transform target,
            ModelAnimationPath path,
            Vector4 value,
            float weight)
        {
            switch (path)
            {
                case ModelAnimationPath.Translation:
                {
                    Vector3 desired = new Vector3(value.x, value.y, value.z);
                    target.localPosition = Vector3.Lerp(target.localPosition, desired, weight);
                    break;
                }

                case ModelAnimationPath.Scale:
                {
                    Vector3 desired = new Vector3(value.x, value.y, value.z);
                    target.localScale = Vector3.Lerp(target.localScale, desired, weight);
                    break;
                }

                case ModelAnimationPath.Rotation:
                {
                    Quaternion desired = new Quaternion(value.x, value.y, value.z, value.w);

                    if (desired.x * desired.x + desired.y * desired.y +
                        desired.z * desired.z + desired.w * desired.w > 0.000001f)
                        desired = Quaternion.Normalize(desired);

                    target.localRotation = Quaternion.Slerp(
                        target.localRotation, desired, weight);
                    break;
                }
            }
        }

        public static void Release(int id)
        {
            if (id != 0)
                States.Remove(id);
        }
    }
}
