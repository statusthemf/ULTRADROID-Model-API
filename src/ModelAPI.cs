using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace ULTRADROID.Models
{
    public sealed class ModelLoadOptions
    {
        public string Name { get; set; } = "CustomModel";
        public Vector3 Position { get; set; } = Vector3.zero;
        public Vector3 RotationEuler { get; set; } = Vector3.zero;
        public Vector3 Scale { get; set; } = Vector3.one;
        public bool LoadTextures { get; set; } = true;
        public bool CreateColliders { get; set; }
        public bool FlipUvV { get; set; }
        public Transform Parent { get; set; }
    }

    public enum ModelAnimationPath
    {
        Translation,
        Rotation,
        Scale
    }

    public enum ModelAnimationInterpolation
    {
        Linear,
        Step,
        CubicSpline
    }

    public sealed class ModelAnimationClip
    {
        public string Name { get; }
        public float Duration { get; }

        internal List<ModelAnimationChannel> Channels { get; }

        internal ModelAnimationClip(
            string name,
            float duration,
            List<ModelAnimationChannel> channels)
        {
            Name = name;
            Duration = duration;
            Channels = channels ?? new List<ModelAnimationChannel>();
        }
    }

    internal sealed class ModelAnimationChannel
    {
        // Transform channels use Target and Path.
        public Transform Target;
        public ModelAnimationPath Path;

        // Morph-weight channels will use MorphRenderer and MorphShapeIndices.
        public SkinnedMeshRenderer MorphRenderer;
        public int[] MorphShapeIndices;

        public ModelAnimationInterpolation Interpolation;

        // One timestamp per keyframe.
        public float[] Times;

        // LINEAR / STEP: one value per keyframe.
        // CUBICSPLINE: one value per keyframe, plus its tangents below.
        public Vector4[] Values;

        // glTF CUBICSPLINE tangents are derivatives with respect to time.
        // These arrays are null for LINEAR and STEP channels.
        public Vector4[] InTangents;
        public Vector4[] OutTangents;

        // For a morph channel, each keyframe contains one weight per target.
        public int MorphTargetCount;
        public float[] MorphValues;
        public float[] MorphInTangents;
        public float[] MorphOutTangents;
    }

    public sealed class ModelInstance
    {
        public GameObject Root { get; }
        public string SourcePath { get; }
        public bool IsValid => Root != null;
        public IReadOnlyList<ModelAnimationClip> Animations { get; }

        internal ModelInstance(
            GameObject root,
            string path,
            List<ModelAnimationClip> animations)
        {
            Root = root;
            SourcePath = path;
            Animations = animations ?? new List<ModelAnimationClip>();
        }

        public void SetPosition(Vector3 position)
        {
            if (Root != null)
                Root.transform.position = position;
        }

        public void SetRotation(Vector3 rotation)
        {
            if (Root != null)
                Root.transform.rotation = Quaternion.Euler(rotation);
        }

        public void SetScale(Vector3 scale)
        {
            if (Root != null)
                Root.transform.localScale = scale;
        }

        public void SetParent(Transform parent)
        {
            if (Root != null)
                Root.transform.SetParent(parent, true);
        }

        public void Destroy()
        {
            if (Root != null)
                UnityEngine.Object.Destroy(Root);
        }
    }

    public static class ModelAPI
    {
        public const string Version = "0.2.0";

        public static ModelInstance LoadGLB(
            string path,
            ModelLoadOptions options = null)
        {
            ValidatePath(path);

            return GlbImporter.Load(
                path,
                options ?? new ModelLoadOptions());
        }

        /// <summary>
        /// Loads a GLB incrementally over multiple Unity frames.
        /// Both callbacks execute on Unity's main thread.
        /// </summary>
        public static void LoadGLBAsync(
            string path,
            Action<ModelInstance> onCompleted,
            Action<Exception> onError = null,
            ModelLoadOptions options = null)
        {
            ValidatePath(path);

            if (onCompleted == null)
                throw new ArgumentNullException(nameof(onCompleted));

            ModelLoadRunner.Begin(
                path,
                options ?? new ModelLoadOptions(),
                onCompleted,
                onError);
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException(
                    "A GLB path is required.",
                    nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException(
                    "GLB file not found.",
                    path);
        }
    }

    internal static class GlbImporter
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinChunk = 0x004E4942;

        internal sealed class PreparedGlb : IDisposable
        {
            internal string SourcePath;
            internal ModelLoadOptions Options;
            internal JsonDocument Document;
            internal byte[] Binary;
            internal byte[][] ImageBytes;

            public void Dispose()
            {
                Document?.Dispose();
                Document = null;
                Binary = null;
                ImageBytes = null;
            }
        }

        private sealed class BufferView
        {
            public int Offset;
            public int Length;
            public int Stride;
        }

        private sealed class Accessor
        {
            public int View = -1;
            public int Offset;
            public int Count;
            public int ComponentType;
            public string Type;
            public bool Normalized;
        }

        private sealed class GltfImage
        {
            public Texture2D Texture;
        }

        private sealed class GltfTexture
        {
            public int Source = -1;
        }

        private sealed class GltfMaterial
        {
            public Material Material;
        }

        private sealed class SkinBuildData
        {
            public int[] Joints;
            public int Skeleton = -1;
            public Matrix4x4[] BindPoses;
        }

        private sealed class PrimitiveBuildData
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[][] UVChannels;
            public Color[] Colors;
            public Vector4[] Tangents;
            public BoneWeight[] BoneWeights;
            public int[] Triangles;
            public Material Material;
        }

        internal static PreparedGlb Prepare(
            string path,
            ModelLoadOptions options)
        {
            ReadGlb(path, out string json, out byte[] binary);
            JsonDocument document = JsonDocument.Parse(json);

            try
            {
                JsonElement gltf = document.RootElement;

                if (!gltf.TryGetProperty("asset", out JsonElement asset) ||
                    !asset.TryGetProperty("version", out JsonElement version) ||
                    version.GetString() == null ||
                    !version.GetString().StartsWith(
                        "2.", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Invalid glTF 2.x asset metadata.");
                }

                byte[][] imageBytes = ExtractImageBytes(
                    gltf,
                    path,
                    binary,
                    ReadBufferViews(gltf),
                    options.LoadTextures);

                return new PreparedGlb
                {
                    SourcePath = path,
                    Options = options,
                    Document = document,
                    Binary = binary,
                    ImageBytes = imageBytes
                };
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        internal static ModelInstance Load(
            string path,
            ModelLoadOptions options)
        {
            using (PreparedGlb prepared = Prepare(path, options))
            {
                ModelInstance result = null;

                IEnumerator routine = LoadIncremental(
                    prepared,
                    instance => result = instance);

                try
                {
                    while (routine.MoveNext())
                    {
                        // Synchronous API: consume every loading stage immediately.
                    }
                }
                finally
                {
                    (routine as IDisposable)?.Dispose();
                }

                return result;
            }
        }

        internal static IEnumerator LoadIncremental(
            PreparedGlb prepared,
            Action<ModelInstance> onCompleted)
        {
            string path = prepared.SourcePath;
            ModelLoadOptions options = prepared.Options;
            byte[] binary = prepared.Binary;
            GameObject rootObject = null;
            bool succeeded = false;

            try
            {
                using (JsonDocument doc = prepared.Document)
                {
                    JsonElement gltf = doc.RootElement;

                    if (!gltf.TryGetProperty("asset", out JsonElement asset) ||
                        !asset.TryGetProperty("version", out JsonElement assetVersion) ||
                        assetVersion.GetString() == null ||
                        !assetVersion.GetString().StartsWith("2.", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Invalid glTF 2.x asset metadata.");
                    }

                    List<BufferView> views = ReadBufferViews(gltf);
                    List<Accessor> accessors = ReadAccessors(gltf);
                    yield return null;

                    List<GltfImage> images = null;
                    IEnumerator imageRoutine = CreateImagesIncremental(
                        gltf,
                        prepared.ImageBytes,
                        result => images = result);

                    try
                    {
                        while (imageRoutine.MoveNext())
                            yield return imageRoutine.Current;
                    }
                    finally
                    {
                        (imageRoutine as IDisposable)?.Dispose();
                    }

                    List<GltfTexture> textures = ReadTextures(gltf);
                    List<GltfMaterial> materials = ReadMaterials(
                        gltf, images, textures, options.LoadTextures);
                    yield return null;

                    JsonElement meshes = RequiredArray(gltf, "meshes");
                    JsonElement nodes = OptionalArray(gltf, "nodes");

                    rootObject = new GameObject(
                        string.IsNullOrWhiteSpace(options.Name)
                            ? "CustomModel"
                            : options.Name);

                    if (options.Parent != null)
                    {
                        rootObject.transform.SetParent(options.Parent, false);
                        rootObject.transform.localPosition = options.Position;
                        rootObject.transform.localRotation =
                            Quaternion.Euler(options.RotationEuler);
                        rootObject.transform.localScale = options.Scale;
                    }
                    else
                    {
                        rootObject.transform.position = options.Position;
                        rootObject.transform.rotation =
                            Quaternion.Euler(options.RotationEuler);
                        rootObject.transform.localScale = options.Scale;
                    }

                    yield return null;

                    GameObject[] nodeObjects = new GameObject[
                        nodes.ValueKind == JsonValueKind.Array
                            ? nodes.GetArrayLength()
                            : 0];

                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        JsonElement node = nodes[i];
                        string nodeName = GetString(node, "name", "Node_" + i);
                        nodeObjects[i] = new GameObject(nodeName);
                        ApplyNodeTransform(nodeObjects[i].transform, node);
                        yield return null;
                    }

                    bool[] hasParent = new bool[nodeObjects.Length];

                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        JsonElement children;

                        if (nodes[i].TryGetProperty("children", out children) &&
                            children.ValueKind == JsonValueKind.Array)
                        {
                            foreach (JsonElement childElement in children.EnumerateArray())
                            {
                                int child = childElement.GetInt32();

                                if (child < 0 || child >= nodeObjects.Length)
                                    throw new InvalidDataException(
                                        "Node child index is out of range.");

                                if (hasParent[child])
                                    throw new InvalidDataException(
                                        "A node has multiple parents.");

                                nodeObjects[child].transform.SetParent(
                                    nodeObjects[i].transform,
                                    false);
                                hasParent[child] = true;
                            }
                        }

                        yield return null;
                    }

                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        if (!hasParent[i])
                        {
                            nodeObjects[i].transform.SetParent(
                                rootObject.transform,
                                false);
                        }

                        yield return null;
                    }

                    SkinBuildData[] skins = ReadSkins(
                        gltf, binary, views, accessors, nodeObjects);
                    yield return null;

                    int meshCount = 0;

                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        JsonElement node = nodes[i];

                        if (!node.TryGetProperty("mesh", out JsonElement meshElement))
                        {
                            yield return null;
                            continue;
                        }

                        int meshIndex = meshElement.GetInt32();

                        if (meshIndex < 0 || meshIndex >= meshes.GetArrayLength())
                            throw new InvalidDataException(
                                "Node mesh index is out of range.");

                        Transform[] skinBones = null;
                        Matrix4x4[] skinBindPoses = null;
                        Transform skinRoot = null;

                        if (node.TryGetProperty("skin", out JsonElement skinElement))
                        {
                            int skinIndex = skinElement.GetInt32();

                            if (skinIndex < 0 || skinIndex >= skins.Length)
                                throw new InvalidDataException(
                                    "Node skin index is out of range.");

                            SkinBuildData skin = skins[skinIndex];
                            skinBones = new Transform[skin.Joints.Length];

                            for (int j = 0; j < skin.Joints.Length; j++)
                                skinBones[j] = nodeObjects[skin.Joints[j]].transform;

                            skinBindPoses = skin.BindPoses;

                            if (skin.Skeleton >= 0)
                                skinRoot = nodeObjects[skin.Skeleton].transform;
                            else if (skinBones.Length > 0)
                                skinRoot = skinBones[0];
                        }

                        BuildMesh(
                            meshes[meshIndex],
                            binary,
                            views,
                            accessors,
                            materials,
                            nodeObjects[i].transform,
                            options,
                            skinBones,
                            skinBindPoses,
                            skinRoot);

                        meshCount++;
                        yield return null;
                    }

                    if (nodeObjects.Length == 0)
                    {
                        for (int i = 0; i < meshes.GetArrayLength(); i++)
                        {
                            BuildMesh(
                                meshes[i],
                                binary,
                                views,
                                accessors,
                                materials,
                                rootObject.transform,
                                options);

                            meshCount++;
                            yield return null;
                        }
                    }

                    if (meshCount == 0)
                        throw new NotSupportedException(
                            "No node-referenced meshes were found.");

                    List<ModelAnimationClip> animations = ReadAnimations(
                        gltf,
                        binary,
                        views,
                        accessors,
                        nodeObjects);
                    yield return null;

                    ModelInstance instance = new ModelInstance(
                        rootObject,
                        path,
                        animations);

                    onCompleted(instance);
                    succeeded = true;
                }
            }
            finally
            {
                if (!succeeded && rootObject != null)
                    UnityEngine.Object.Destroy(rootObject);
            }
        }

        private static SkinBuildData[] ReadSkins(
            JsonElement gltf,
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            GameObject[] nodeObjects)
        {
            JsonElement skinArray = OptionalArray(gltf, "skins");

            if (skinArray.ValueKind != JsonValueKind.Array)
                return new SkinBuildData[0];

            SkinBuildData[] result =
                new SkinBuildData[skinArray.GetArrayLength()];

            for (int i = 0; i < result.Length; i++)
            {
                JsonElement skin = skinArray[i];
                JsonElement jointsElement = RequiredArray(skin, "joints");

                if (jointsElement.GetArrayLength() == 0)
                    throw new InvalidDataException(
                        "Skin contains no joints.");

                int[] joints = new int[jointsElement.GetArrayLength()];

                for (int j = 0; j < joints.Length; j++)
                {
                    joints[j] = jointsElement[j].GetInt32();

                    if (joints[j] < 0 || joints[j] >= nodeObjects.Length)
                        throw new InvalidDataException(
                            "Skin joint node index is out of range.");
                }

                Matrix4x4[] bindPoses = null;

                if (skin.TryGetProperty(
                        "inverseBindMatrices",
                        out JsonElement bindPoseElement))
                {
                    Accessor accessor = GetAccessor(
                        accessors, bindPoseElement.GetInt32());

                    if (accessor.Type != "MAT4" ||
                        accessor.ComponentType != 5126 ||
                        accessor.Normalized ||
                        accessor.Count != joints.Length)
                    {
                        throw new InvalidDataException(
                            "Inverse bind matrices must be FLOAT MAT4 values, " +
                            "with one matrix per joint.");
                    }

                    byte[] raw = ReadAccessorBytes(
                        binary, views, accessor, 64);

                    bindPoses = new Matrix4x4[accessor.Count];

                    for (int m = 0; m < bindPoses.Length; m++)
                    {
                        Matrix4x4 matrix = new Matrix4x4();
                        int offset = m * 64;

                        // glTF matrices are stored column-major.
                        for (int column = 0; column < 4; column++)
                        {
                            for (int row = 0; row < 4; row++)
                            {
                                matrix[row, column] = ReadFloat(
                                    raw,
                                    offset + (column * 4 + row) * 4);
                            }
                        }

                        bindPoses[m] = matrix;
                    }
                }

                int skeleton = GetInt(skin, "skeleton", -1);

                if (skeleton >= nodeObjects.Length || skeleton < -1)
                    throw new InvalidDataException(
                        "Skin skeleton node index is out of range.");

                result[i] = new SkinBuildData
                {
                    Joints = joints,
                    Skeleton = skeleton,
                    BindPoses = bindPoses
                };
            }

            return result;
        }

        private static List<ModelAnimationClip> ReadAnimations(
            JsonElement gltf,
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            GameObject[] nodeObjects)
        {
            List<ModelAnimationClip> result =
                new List<ModelAnimationClip>();

            JsonElement animations = OptionalArray(gltf, "animations");

            if (animations.ValueKind != JsonValueKind.Array)
                return result;

            int animationIndex = 0;

            foreach (JsonElement animation in animations.EnumerateArray())
            {
                string name = GetString(
                    animation, "name", "Animation_" + animationIndex);

                JsonElement samplers = RequiredArray(animation, "samplers");
                JsonElement channels = RequiredArray(animation, "channels");

                List<ModelAnimationChannel> parsedChannels =
                    new List<ModelAnimationChannel>();

                float duration = 0f;

                foreach (JsonElement channel in channels.EnumerateArray())
                {
                    int samplerIndex = GetInt(channel, "sampler", -1);

                    if (samplerIndex < 0 ||
                        samplerIndex >= samplers.GetArrayLength())
                    {
                        throw new InvalidDataException(
                            "Animation sampler index is out of range.");
                    }

                    if (!channel.TryGetProperty(
                            "target", out JsonElement target))
                    {
                        throw new InvalidDataException(
                            "Animation channel has no target.");
                    }

                    int nodeIndex = GetInt(target, "node", -1);

                    if (nodeIndex < 0 || nodeIndex >= nodeObjects.Length)
                    {
                        throw new InvalidDataException(
                            "Animation target node index is out of range.");
                    }

                    string path = GetString(target, "path", "");

                    ModelAnimationPath animationPath;

                    switch (path)
                    {
                        case "translation":
                            animationPath = ModelAnimationPath.Translation;
                            break;

                        case "rotation":
                            animationPath = ModelAnimationPath.Rotation;
                            break;

                        case "scale":
                            animationPath = ModelAnimationPath.Scale;
                            break;

                        case "weights":
                            throw new NotSupportedException(
                                "Morph-weight animation requires blend-shape import support.");
        
                        default:
                            throw new NotSupportedException(
                                "Unsupported animation target path: " + path);
                    }

                    JsonElement sampler = samplers[samplerIndex];

                    int inputIndex = GetInt(sampler, "input", -1);
                    int outputIndex = GetInt(sampler, "output", -1);

                    Accessor inputAccessor = GetAccessor(accessors, inputIndex);
                    Accessor outputAccessor = GetAccessor(accessors, outputIndex);

                    if (inputAccessor.Type != "SCALAR" ||
                        inputAccessor.ComponentType != 5126 ||
                        inputAccessor.Normalized)
                    {
                        throw new NotSupportedException(
                            "Animation input must be a non-normalized FLOAT SCALAR.");
                    }

                    string interpolationName = GetString(
                        sampler, "interpolation", "LINEAR");

                    ModelAnimationInterpolation interpolation;

                    switch (interpolationName)
                    {
                        case "LINEAR":
                            interpolation = ModelAnimationInterpolation.Linear;
                            break;

                        case "STEP":
                            interpolation = ModelAnimationInterpolation.Step;
                            break;

                        case "CUBICSPLINE":
                            interpolation = ModelAnimationInterpolation.CubicSpline;
                            break;

                        default:
                            throw new NotSupportedException(
                                "Unsupported animation interpolation: " +
                                interpolationName);
                    }

                    int expectedComponents =
                        animationPath == ModelAnimationPath.Rotation ? 4 : 3;

                    string expectedType =
                        expectedComponents == 4 ? "VEC4" : "VEC3";

                    if (outputAccessor.Type != expectedType ||
                        outputAccessor.ComponentType != 5126 ||
                        outputAccessor.Normalized)
                    {
                        throw new NotSupportedException(
                            "Animation output must be a non-normalized FLOAT " +
                            expectedType + ".");
                    }

                    float[] times = ReadAnimationTimes(
                        binary, views, inputAccessor);

                    if (times.Length == 0)
                    {
                        throw new InvalidDataException(
                            "Animation sampler contains no keyframes.");
                    }

                    for (int i = 0; i < times.Length; i++)
                    {
                        if (float.IsNaN(times[i]) ||
                            float.IsInfinity(times[i]) ||
                            times[i] < 0f)
                        {
                            throw new InvalidDataException(
                                "Animation contains an invalid keyframe time.");
                        }

                        if (i > 0 && times[i] <= times[i - 1])
                        {
                            throw new InvalidDataException(
                                "Animation keyframe times must be strictly increasing.");
                        }
                    }

                    Vector4[] values;
                    Vector4[] inTangents = null;
                    Vector4[] outTangents = null;

                    if (interpolation == ModelAnimationInterpolation.CubicSpline)
                    {
                        int expectedOutputCount = checked(times.Length * 3);

                        if (outputAccessor.Count != expectedOutputCount)
                        {
                            throw new InvalidDataException(
                                "CUBICSPLINE output must contain exactly three " +
                                "values per input keyframe.");
                        }

                        Vector4[] cubicValues = ReadAnimationValues(
                            binary,
                            views,
                            outputAccessor,
                            expectedComponents);

                        values = new Vector4[times.Length];
                        inTangents = new Vector4[times.Length];
                        outTangents = new Vector4[times.Length];

                        for (int i = 0; i < times.Length; i++)
                        {
                            int baseIndex = i * 3;

                            // glTF order: incoming tangent, value, outgoing tangent.
                            inTangents[i] = cubicValues[baseIndex];
                            values[i] = cubicValues[baseIndex + 1];
                            outTangents[i] = cubicValues[baseIndex + 2];
                        }
                    }
                    else
                    {
                        if (outputAccessor.Count != times.Length)
                        {
                            throw new InvalidDataException(
                                "Animation input and output key counts do not match.");
                        }

                        values = ReadAnimationValues(
                            binary,
                            views,
                            outputAccessor,
                            expectedComponents);
                    }

                    for (int i = 0; i < values.Length; i++)
                    {
                        Vector4 value = values[i];

                        if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                            float.IsNaN(value.y) || float.IsInfinity(value.y) ||
                            float.IsNaN(value.z) || float.IsInfinity(value.z) ||
                            float.IsNaN(value.w) || float.IsInfinity(value.w))
                        {
                            throw new InvalidDataException(
                                "Animation contains an invalid keyframe value.");
                        }
                    }

                    if (interpolation == ModelAnimationInterpolation.CubicSpline)
                    {
                        for (int i = 0; i < times.Length; i++)
                        {
                            Vector4 incoming = inTangents[i];
                            Vector4 outgoing = outTangents[i];

                            if (float.IsNaN(incoming.x) || float.IsInfinity(incoming.x) ||
                                float.IsNaN(incoming.y) || float.IsInfinity(incoming.y) ||
                                float.IsNaN(incoming.z) || float.IsInfinity(incoming.z) ||
                                float.IsNaN(incoming.w) || float.IsInfinity(incoming.w) ||
                                float.IsNaN(outgoing.x) || float.IsInfinity(outgoing.x) ||
                                float.IsNaN(outgoing.y) || float.IsInfinity(outgoing.y) ||
                                float.IsNaN(outgoing.z) || float.IsInfinity(outgoing.z) ||
                                float.IsNaN(outgoing.w) || float.IsInfinity(outgoing.w))
                            {
                                throw new InvalidDataException(
                                    "CUBICSPLINE contains an invalid tangent.");
                            }
                        }
                    }

                    duration = Mathf.Max(duration, times[times.Length - 1]);

                    parsedChannels.Add(new ModelAnimationChannel
                    {
                        Target = nodeObjects[nodeIndex].transform,
                        Path = animationPath,
                        Interpolation = interpolation,
                        Times = times,
                        Values = values,
                        InTangents = inTangents,
                        OutTangents = outTangents
                    });
                }

                result.Add(new ModelAnimationClip(
                    name, duration, parsedChannels));

                animationIndex++;
            }

            return result;
        }

        private static float[] ReadAnimationTimes(
            byte[] binary,
            List<BufferView> views,
            Accessor accessor)
        {
            byte[] raw = ReadAccessorBytes(
                binary, views, accessor, 4);

            float[] result = new float[accessor.Count];

            for (int i = 0; i < result.Length; i++)
                result[i] = ReadFloat(raw, i * 4);

            return result;
        }

        private static Vector4[] ReadAnimationValues(
            byte[] binary,
            List<BufferView> views,
            Accessor accessor,
            int components)
        {
            int elementSize = components * 4;

            byte[] raw = ReadAccessorBytes(
                binary, views, accessor, elementSize);

            Vector4[] result = new Vector4[accessor.Count];

            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * elementSize;

                float x = ReadFloat(raw, offset);
                float y = ReadFloat(raw, offset + 4);
                float z = ReadFloat(raw, offset + 8);
                float w = components == 4
                    ? ReadFloat(raw, offset + 12)
                    : 0f;

                result[i] = new Vector4(x, y, z, w);
            }

            return result;
        }

        private static void ReadGlb(
            string path,
            out string json,
            out byte[] binary)
        {
            using (FileStream stream = new FileStream(
                path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (stream.Length < 20)
                    throw new InvalidDataException("GLB file is too short.");

                if (reader.ReadUInt32() != GlbMagic)
                    throw new InvalidDataException("Invalid GLB magic.");

                if (reader.ReadUInt32() != 2)
                    throw new InvalidDataException(
                        "Only GLB version 2 is supported.");

                uint declaredLength = reader.ReadUInt32();

                if (declaredLength != stream.Length)
                    throw new InvalidDataException(
                        "GLB declared length does not match file length.");

                json = null;
                binary = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    uint length = reader.ReadUInt32();
                    uint type = reader.ReadUInt32();

                    if (length > stream.Length - stream.Position)
                        throw new InvalidDataException(
                            "Invalid GLB chunk length.");

                    if (length > int.MaxValue)
                        throw new InvalidDataException(
                            "GLB chunk is too large to load.");

                    byte[] chunk = reader.ReadBytes((int)length);

                    if (chunk.Length != (int)length)
                        throw new EndOfStreamException(
                            "Unexpected end of GLB file.");

                    if (type == JsonChunk)
                    {
                        json = Encoding.UTF8.GetString(chunk)
                            .TrimEnd('\0', ' ', '\t', '\r', '\n');
                    }
                    else if (type == BinChunk)
                    {
                        binary = chunk;
                    }
                }

                if (string.IsNullOrEmpty(json))
                    throw new InvalidDataException(
                        "GLB JSON chunk is missing.");

                if (binary == null)
                    throw new InvalidDataException(
                        "GLB binary chunk is missing.");
            }
        }

        private static List<BufferView> ReadBufferViews(JsonElement gltf)
        {
            List<BufferView> result = new List<BufferView>();
            JsonElement array = OptionalArray(gltf, "bufferViews");

            if (array.ValueKind != JsonValueKind.Array)
                return result;

            foreach (JsonElement item in array.EnumerateArray())
            {
                result.Add(new BufferView
                {
                    Offset = GetInt(item, "byteOffset", 0),
                    Length = GetInt(item, "byteLength", -1),
                    Stride = GetInt(item, "byteStride", 0)
                });
            }

            return result;
        }

        private static List<Accessor> ReadAccessors(JsonElement gltf)
        {
            List<Accessor> result = new List<Accessor>();
            JsonElement array = RequiredArray(gltf, "accessors");

            foreach (JsonElement item in array.EnumerateArray())
            {
                result.Add(new Accessor
                {
                    View = GetInt(item, "bufferView", -1),
                    Offset = GetInt(item, "byteOffset", 0),
                    Count = GetInt(item, "count", 0),
                    ComponentType = GetInt(item, "componentType", 0),
                    Type = GetString(item, "type", ""),
                    Normalized = GetBool(item, "normalized", false)
                });
            }

            return result;
        }

        private static void BuildMesh(
            JsonElement meshData,
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            List<GltfMaterial> materials,
            Transform parent,
            ModelLoadOptions options,
            Transform[] skinBones = null,
            Matrix4x4[] skinBindPoses = null,
            Transform skinRoot = null)
        {
            JsonElement primitives = RequiredArray(meshData, "primitives");
            string meshName = GetString(meshData, "name", "Mesh");

            List<PrimitiveBuildData> builtPrimitives =
                new List<PrimitiveBuildData>();

            List<Vector3> allVertices = new List<Vector3>();
            List<Vector3> allNormals = new List<Vector3>();
            List<Vector2>[] allUVChannels = new List<Vector2>[8];
            List<Color> allColors = new List<Color>();
            List<Vector4> allTangents = new List<Vector4>();
            List<BoneWeight> allBoneWeights = new List<BoneWeight>();

            for (int channel = 0; channel < 8; channel++)
                allUVChannels[channel] = new List<Vector2>();

            List<int[]> submeshTriangles = new List<int[]>();
            List<Material> submeshMaterials = new List<Material>();

            bool[] anyHaveUVChannel = new bool[8];
            bool anyHaveColors = false;
            bool anyHaveTangents = false;
            bool allHaveNormals = true;
            bool allHaveTangents = true;
            bool requiresTangents = false;
            int totalVertices = 0;

            Material defaultMaterial = null;

            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = GetInt(primitive, "mode", 4);

                if (mode != 4 && mode != 5 && mode != 6)
                {
                    throw new NotSupportedException(
                        "Unsupported glTF primitive mode: " + mode +
                        ". Supported modes are TRIANGLES, TRIANGLE_STRIP, and TRIANGLE_FAN.");
                }

                if (!primitive.TryGetProperty(
                        "attributes", out JsonElement attributes) ||
                    !attributes.TryGetProperty(
                        "POSITION", out JsonElement positionElement))
                {
                    throw new InvalidDataException(
                        "Mesh primitive has no POSITION attribute.");
                }

                int positionAccessor = positionElement.GetInt32();

                Vector3[] vertices = ReadVector3(
                    binary, views, accessors,
                    positionAccessor, false, false);

                if (vertices.Length == 0)
                    throw new InvalidDataException("Mesh has no vertices.");

                Vector3[] normals = null;
                Vector2[][] uvChannels = new Vector2[8][];
                Color[] colors = null;
                Vector4[] tangents = null;
                BoneWeight[] boneWeights = null;

                if (attributes.TryGetProperty(
                        "NORMAL", out JsonElement normalElement))
                {
                    normals = ReadVector3(
                        binary, views, accessors,
                        normalElement.GetInt32(), false, true);

                    if (normals.Length != vertices.Length)
                        normals = null;
                }

                for (int channel = 0; channel < 8; channel++)
                {
                    string attributeName = "TEXCOORD_" + channel;

                    if (!attributes.TryGetProperty(
                            attributeName, out JsonElement uvElement))
                        continue;

                    uvChannels[channel] = ReadVector2(
                        binary,
                        views,
                        accessors,
                        uvElement.GetInt32(),
                        options.FlipUvV);

                    if (uvChannels[channel].Length != vertices.Length)
                        uvChannels[channel] = null;

                    if (uvChannels[channel] != null)
                        anyHaveUVChannel[channel] = true;
                }

                if (attributes.TryGetProperty(
                        "COLOR_0", out JsonElement colorElement))
                {
                    colors = ReadColors(
                        binary,
                        views,
                        accessors,
                        colorElement.GetInt32());

                    if (colors.Length != vertices.Length)
                        colors = null;
                }

                if (attributes.TryGetProperty(
                        "TANGENT", out JsonElement tangentElement))
                {
                    tangents = ReadTangents(
                        binary,
                        views,
                        accessors,
                        tangentElement.GetInt32());

                    if (tangents.Length != vertices.Length)
                        tangents = null;
                }

                if (skinBones != null)
                {
                    if (!attributes.TryGetProperty(
                            "JOINTS_0", out JsonElement jointsElement) ||
                        !attributes.TryGetProperty(
                            "WEIGHTS_0", out JsonElement weightsElement))
                    {
                        throw new InvalidDataException(
                            "Skinned mesh primitive requires JOINTS_0 and WEIGHTS_0.");
                    }

                    boneWeights = ReadBoneWeights(
                        binary,
                        views,
                        accessors,
                        jointsElement.GetInt32(),
                        weightsElement.GetInt32(),
                        vertices.Length,
                        skinBones.Length);
                }

                int[] sourceIndices;

                if (primitive.TryGetProperty(
                        "indices", out JsonElement indexElement))
                {
                    sourceIndices = ReadIndices(
                        binary, views, accessors,
                        indexElement.GetInt32());
                }
                else
                {
                    sourceIndices = new int[vertices.Length];

                    for (int i = 0; i < vertices.Length; i++)
                        sourceIndices[i] = i;
                }

                for (int i = 0; i < sourceIndices.Length; i++)
                {
                    if (sourceIndices[i] < 0 ||
                        sourceIndices[i] >= vertices.Length)
                    {
                        throw new InvalidDataException(
                            "Mesh index is out of range.");
                    }
                }

                int[] triangles;

                if (mode == 4)
                {
                    if (sourceIndices.Length % 3 != 0)
                    {
                        throw new InvalidDataException(
                            "Triangle index count is not divisible by 3.");
                    }

                    triangles = sourceIndices;
                }
                else if (mode == 5)
                {
                    // TRIANGLE_STRIP
                    List<int> converted = new List<int>();

                    for (int i = 0; i + 2 < sourceIndices.Length; i++)
                    {
                        if ((i & 1) == 0)
                        {
                            converted.Add(sourceIndices[i]);
                            converted.Add(sourceIndices[i + 1]);
                        }
                        else
                        {
                            converted.Add(sourceIndices[i + 1]);
                            converted.Add(sourceIndices[i]);
                        }

                        converted.Add(sourceIndices[i + 2]);
                    }

                    triangles = converted.ToArray();
                }
                else
                {
                    // TRIANGLE_FAN
                    List<int> converted = new List<int>();

                    for (int i = 1; i + 1 < sourceIndices.Length; i++)
                    {
                        converted.Add(sourceIndices[0]);
                        converted.Add(sourceIndices[i]);
                        converted.Add(sourceIndices[i + 1]);
                    }

                    triangles = converted.ToArray();
                }

                int materialIndex = GetInt(primitive, "material", -1);

                Material primitiveMaterial;

                if (materialIndex >= 0 && materialIndex < materials.Count)
                {
                    primitiveMaterial = materials[materialIndex].Material;
                }
                else
                {
                    if (defaultMaterial == null)
                        defaultMaterial = CreateDefaultMaterial();

                    primitiveMaterial = defaultMaterial;
                }

                if (primitiveMaterial != null &&
                    primitiveMaterial.HasProperty("_BumpMap") &&
                    primitiveMaterial.GetTexture("_BumpMap") != null)
                {
                    requiresTangents = true;
                }

                PrimitiveBuildData data = new PrimitiveBuildData
                {
                    Vertices = vertices,
                    Normals = normals,
                    UVChannels = uvChannels,
                    Colors = colors,
                    Tangents = tangents,
                    BoneWeights = boneWeights,
                    Triangles = triangles,
                    Material = primitiveMaterial
                };

                builtPrimitives.Add(data);

                allHaveNormals &= normals != null;

                if (colors != null)
                    anyHaveColors = true;

                if (tangents != null)
                    anyHaveTangents = true;

                allHaveTangents &= tangents != null;

                totalVertices = checked(totalVertices + vertices.Length);
            }

            if (builtPrimitives.Count == 0)
                throw new InvalidDataException("Mesh contains no primitives.");

            // Combine primitive vertex data into one Unity mesh.
            foreach (PrimitiveBuildData primitive in builtPrimitives)
            {
                int vertexOffset = allVertices.Count;

                allVertices.AddRange(primitive.Vertices);

                if (skinBones != null)
                {
                    if (primitive.BoneWeights == null ||
                        primitive.BoneWeights.Length != primitive.Vertices.Length)
                    {
                        throw new InvalidDataException(
                            "Skinned primitive has invalid bone weights.");
                    }

                    allBoneWeights.AddRange(primitive.BoneWeights);
                }

                if (allHaveNormals)
                {
                    allNormals.AddRange(primitive.Normals);
                }

                for (int channel = 0; channel < 8; channel++)
                {
                    if (!anyHaveUVChannel[channel])
                        continue;

                    Vector2[] channelUVs = primitive.UVChannels[channel];

                    if (channelUVs != null)
                    {
                        allUVChannels[channel].AddRange(channelUVs);
                    }
                    else
                    {
                        for (int i = 0; i < primitive.Vertices.Length; i++)
                            allUVChannels[channel].Add(Vector2.zero);
                    }
                }

                if (anyHaveColors)
                {
                    if (primitive.Colors != null)
                    {
                        allColors.AddRange(primitive.Colors);
                    }
                    else
                    {
                        // Primitives without COLOR_0 retain material colors.
                        for (int i = 0; i < primitive.Vertices.Length; i++)
                            allColors.Add(Color.white);
                    }
                }

                if (anyHaveTangents && allHaveTangents)
                    allTangents.AddRange(primitive.Tangents);

                int[] adjustedTriangles =
                    new int[primitive.Triangles.Length];

                for (int i = 0; i < primitive.Triangles.Length; i++)
                {
                    adjustedTriangles[i] =
                        checked(primitive.Triangles[i] + vertexOffset);
                }

                submeshTriangles.Add(adjustedTriangles);
                submeshMaterials.Add(primitive.Material);
            }

            Mesh unityMesh = new Mesh();
            unityMesh.name = meshName;

            if (totalVertices > 65535)
                unityMesh.indexFormat = IndexFormat.UInt32;

            // Upload managed arrays directly instead of populating IL2CPP lists
            // one element at a time.
            unityMesh.vertices = allVertices.ToArray();
            unityMesh.subMeshCount = builtPrimitives.Count;

            for (int i = 0; i < submeshTriangles.Count; i++)
            {
                unityMesh.SetTriangles(submeshTriangles[i], i, false);
            }

            if (allHaveNormals && allNormals.Count == totalVertices)
            {
                unityMesh.normals = allNormals.ToArray();
            }
            else
            {
                unityMesh.RecalculateNormals();
            }

            for (int channel = 0; channel < 8; channel++)
            {
                if (!anyHaveUVChannel[channel] ||
                    allUVChannels[channel].Count != totalVertices)
                    continue;

                // Keep the existing IL2CPP list path for UVs until
                // the target Unity bindings are verified.
                Il2CppSystem.Collections.Generic.List<Vector2> il2cppUVs =
                    new Il2CppSystem.Collections.Generic.List<Vector2>();

                for (int i = 0; i < allUVChannels[channel].Count; i++)
                    il2cppUVs.Add(allUVChannels[channel][i]);

                unityMesh.SetUVs(channel, il2cppUVs);
            }

            if (anyHaveColors && allColors.Count == totalVertices)
            {
                unityMesh.colors = allColors.ToArray();
            }

            if (anyHaveTangents && allHaveTangents &&
                allTangents.Count == totalVertices)
            {
                unityMesh.tangents = allTangents.ToArray();
            }
            else if (requiresTangents &&
                     anyHaveUVChannel[0] &&
                     allUVChannels[0].Count == totalVertices)
            {
                unityMesh.RecalculateTangents();
            }

            if (skinBones != null)
            {
                if (allBoneWeights.Count != totalVertices)
                    throw new InvalidDataException(
                        "Combined bone weight count does not match vertex count.");

                Matrix4x4[] finalBindPoses = skinBindPoses;

                if (finalBindPoses == null)
                {
                    finalBindPoses = new Matrix4x4[skinBones.Length];

                    for (int i = 0; i < skinBones.Length; i++)
                    {
                        finalBindPoses[i] =
                            skinBones[i].worldToLocalMatrix *
                            parent.localToWorldMatrix;
                    }
                }

                if (finalBindPoses.Length != skinBones.Length)
                    throw new InvalidDataException(
                        "Bind pose count does not match skin joint count.");

                unityMesh.boneWeights = allBoneWeights.ToArray();
                unityMesh.bindposes = finalBindPoses;
            }

            unityMesh.RecalculateBounds();

            GameObject meshObject = new GameObject(meshName);
            meshObject.transform.SetParent(parent, false);

            if (skinBones != null)
            {
                SkinnedMeshRenderer renderer =
                    meshObject.AddComponent<SkinnedMeshRenderer>();

                renderer.sharedMesh = unityMesh;
                renderer.bones = skinBones;
                renderer.rootBone = skinRoot;
                renderer.sharedMaterials = submeshMaterials.ToArray();

                Debug.LogError(
                    "[ULTRADROID LIGHTING TEST] Reached skinned renderer.");

                LogRendererDiagnostics(renderer);

                Debug.LogError(
                    "[ULTRADROID LIGHTING TEST] Diagnostics finished.");

                renderer.updateWhenOffscreen = true;
            }
            else
            {
                MeshFilter filter = meshObject.AddComponent<MeshFilter>();
                filter.sharedMesh = unityMesh;

                MeshRenderer renderer =
                    meshObject.AddComponent<MeshRenderer>();

                renderer.sharedMaterials = submeshMaterials.ToArray();
                LogRendererDiagnostics(renderer);
            }

            if (options.CreateColliders && skinBones == null)
            {
                MeshCollider collider =
                    meshObject.AddComponent<MeshCollider>();

                collider.sharedMesh = unityMesh;
            }
        }

        private static BoneWeight[] ReadBoneWeights(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int jointsIndex,
            int weightsIndex,
            int vertexCount,
            int jointCount)
        {
            Accessor joints = GetAccessor(accessors, jointsIndex);
            Accessor weights = GetAccessor(accessors, weightsIndex);

            if (joints.Type != "VEC4" ||
                (joints.ComponentType != 5121 &&
                 joints.ComponentType != 5123) ||
                joints.Normalized)
            {
                throw new NotSupportedException(
                    "JOINTS_0 must be an unnormalized UNSIGNED_BYTE or " +
                    "UNSIGNED_SHORT VEC4.");
            }

            if (weights.Type != "VEC4")
                throw new InvalidDataException(
                    "WEIGHTS_0 must be VEC4.");

            int weightComponentSize;

            switch (weights.ComponentType)
            {
                case 5126: // FLOAT
                    if (weights.Normalized)
                        throw new InvalidDataException(
                            "FLOAT WEIGHTS_0 cannot be normalized.");
                    weightComponentSize = 4;
                    break;

                case 5121: // UNSIGNED_BYTE
                    if (!weights.Normalized)
                        throw new NotSupportedException(
                            "Integer WEIGHTS_0 must be normalized.");
                    weightComponentSize = 1;
                    break;

                case 5123: // UNSIGNED_SHORT
                    if (!weights.Normalized)
                        throw new NotSupportedException(
                            "Integer WEIGHTS_0 must be normalized.");
                    weightComponentSize = 2;
                    break;

                default:
                    throw new NotSupportedException(
                        "Unsupported WEIGHTS_0 component type.");
            }

            if (joints.Count != vertexCount ||
                weights.Count != vertexCount)
            {
                throw new InvalidDataException(
                    "Joint and weight counts must match the vertex count.");
            }

            int jointComponentSize =
                joints.ComponentType == 5121 ? 1 : 2;

            byte[] jointBytes = ReadAccessorBytes(
                binary, views, joints, jointComponentSize * 4);

            byte[] weightBytes = ReadAccessorBytes(
                binary, views, weights, weightComponentSize * 4);

            BoneWeight[] result = new BoneWeight[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                int jointOffset = i * jointComponentSize * 4;
                int weightOffset = i * weightComponentSize * 4;

                int[] boneIndices = new int[4];
                float[] boneWeights = new float[4];

                for (int k = 0; k < 4; k++)
                {
                    int jo = jointOffset + k * jointComponentSize;

                    int jointIndex;

                    if (jointComponentSize == 1)
                    {
                        jointIndex = jointBytes[jo];
                    }
                    else
                    {
                        jointIndex = jointBytes[jo] |
                            (jointBytes[jo + 1] << 8);
                    }

                    if (jointIndex < 0 || jointIndex >= jointCount)
                    {
                        throw new InvalidDataException(
                            "Vertex references a joint outside its skin.");
                    }

                    boneIndices[k] = jointIndex;

                    int wo = weightOffset + k * weightComponentSize;
                    float weight;

                    switch (weights.ComponentType)
                    {
                        case 5126:
                            weight = ReadFloat(weightBytes, wo);
                            break;

                        case 5121:
                            weight = weightBytes[wo] / 255f;
                            break;

                        case 5123:
                            weight = (
                                weightBytes[wo] |
                                (weightBytes[wo + 1] << 8)) / 65535f;
                            break;

                        default:
                            throw new NotSupportedException();
                    }

                    if (float.IsNaN(weight) ||
                        float.IsInfinity(weight) ||
                        weight < 0f)
                    {
                        throw new InvalidDataException(
                            "Vertex contains an invalid bone weight.");
                    }

                    boneWeights[k] = weight;
                }

                float sum =
                    boneWeights[0] + boneWeights[1] +
                    boneWeights[2] + boneWeights[3];

                if (sum <= 0.000001f)
                {
                    boneIndices[0] = 0;
                    boneWeights[0] = 1f;
                    boneWeights[1] = 0f;
                    boneWeights[2] = 0f;
                    boneWeights[3] = 0f;
                }
                else
                {
                    for (int k = 0; k < 4; k++)
                        boneWeights[k] /= sum;
                }

                result[i] = new BoneWeight
                {
                    boneIndex0 = boneIndices[0],
                    boneIndex1 = boneIndices[1],
                    boneIndex2 = boneIndices[2],
                    boneIndex3 = boneIndices[3],
                    weight0 = boneWeights[0],
                    weight1 = boneWeights[1],
                    weight2 = boneWeights[2],
                    weight3 = boneWeights[3]
                };
            }

            return result;
        }

        private static void LogRendererDiagnostics(Renderer renderer)
        {
            if (renderer == null)
                return;

            try
            {
                Debug.LogError(
                    "[ULTRADROID Lighting] Renderer: " +
                    renderer.name +
                    " | Receive Shadows: " +
                    renderer.receiveShadows +
                    " | Cast Shadows: " +
                    renderer.shadowCastingMode +
                    " | Light Probes: " +
                    renderer.lightProbeUsage +
                    " | Reflection Probes: " +
                    renderer.reflectionProbeUsage);

                Material[] materials = renderer.sharedMaterials;

                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];

                    if (material == null)
                        continue;

                    Shader shader = material.shader;

                    Debug.LogError(
                        "[ULTRADROID Lighting] Material " + i +
                        ": " + material.name +
                        " | Shader: " +
                        (shader != null ? shader.name : "NULL") +
                        " | Supported: " +
                        (shader != null && shader.isSupported));

                    string[] properties =
                    {
                        "_Metallic",
                        "_Glossiness",
                        "_Smoothness",
                        "_Mode",
                        "_EmissionColor",
                        "_Color",
                        "_BaseColor",
                        "_MainTex",
                        "_BumpMap",
                        "_MetallicGlossMap"
                    };

                                    foreach (string property in properties)
                {
                    if (!material.HasProperty(property))
                        continue;

                    if (property.Contains("Color"))
                    {
                        Debug.LogError(
                            "[ULTRADROID Lighting] " +
                            property + " = " +
                            material.GetColor(property));
                    }
                    else if (
                        property.EndsWith("Map") ||
                        property.EndsWith("Tex"))
                    {
                        Texture texture = material.GetTexture(property);

                        Debug.LogError(
                            "[ULTRADROID Lighting] " +
                            property + " = " +
                            (texture != null ? texture.name : "NULL"));
                    }
                    else
                    {
                        Debug.LogError(
                            "[ULTRADROID Lighting] " +
                            property + " = " +
                            material.GetFloat(property));
                    }
                }

                Debug.LogError(
                    "[ULTRADROID Lighting] Emission keyword: " +
                    material.IsKeywordEnabled("_EMISSION"));

                Debug.LogError(
                    "[ULTRADROID Lighting] Material keywords: " +
                    string.Join(", ", material.shaderKeywords));

                Texture mainTex = material.GetTexture("_MainTex");
                Texture bumpTex = material.GetTexture("_BumpMap");

                Debug.LogError(
                    "[ULTRADROID Lighting] MainTex instance: " +
                    (mainTex != null
                        ? mainTex.GetInstanceID() + " | " +
                          mainTex.width + "x" + mainTex.height
                        : "NULL"));

                Debug.LogError(
                    "[ULTRADROID Lighting] BumpMap instance: " +
                    (bumpTex != null
                        ? bumpTex.GetInstanceID() + " | " +
                          bumpTex.width + "x" + bumpTex.height
                        : "NULL"));
            }
        }
        catch (Exception ex)
            {
                Debug.LogError(
                    "[ULTRADROID Lighting] Diagnostics failed: " +
                    ex);
            }
        }

        private static Material CreateDefaultMaterial()
        {
            string[] preferredShaders =
            {
                "Standard",
                "Universal Render Pipeline/Lit",
                "HDRP/Lit",
                "Legacy Shaders/Diffuse",
                "Legacy Shaders/Specular"
            };

            foreach (string shaderName in preferredShaders)
            {
                try
                {
                    Shader candidate = Shader.Find(shaderName);

                    if (candidate == null || !candidate.isSupported)
                        continue;

                    Material material = new Material(candidate);

                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", Color.white);

                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", Color.white);

                    if (material.HasProperty("_Metallic"))
                        material.SetFloat("_Metallic", 0f);

                    if (material.HasProperty("_Smoothness"))
                        material.SetFloat("_Smoothness", 0.3f);

                    if (material.HasProperty("_Glossiness"))
                        material.SetFloat("_Glossiness", 0.3f);

                    return material;
                }
                catch
                {
                    // Try the next supported shader.
                }
            }

            throw new NotSupportedException(
                "No supported lit shader was found. " +
                "Check the Unity render pipeline and available game shaders.");
        }

        private static byte[][] ExtractImageBytes(
            JsonElement gltf,
            string glbPath,
            byte[] binary,
            List<BufferView> views,
            bool loadTextures)
        {
            JsonElement images = OptionalArray(gltf, "images");

            if (images.ValueKind != JsonValueKind.Array)
                return new byte[0][];

            byte[][] result = new byte[images.GetArrayLength()][];

            if (!loadTextures)
                return result;

            string directory = Path.GetDirectoryName(Path.GetFullPath(glbPath));
            string directoryPrefix = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            for (int i = 0; i < result.Length; i++)
            {
                JsonElement imageData = images[i];

                if (imageData.TryGetProperty(
                        "bufferView", out JsonElement viewElement))
                {
                    int viewIndex = viewElement.GetInt32();

                    if (viewIndex < 0 || viewIndex >= views.Count)
                        throw new InvalidDataException(
                            "Image bufferView index is out of range.");

                    BufferView view = views[viewIndex];
                    long viewEnd = (long)view.Offset + view.Length;

                    if (view.Offset < 0 || view.Length < 0 ||
                        viewEnd > binary.Length)
                    {
                        throw new InvalidDataException(
                            "Image bufferView exceeds the binary chunk.");
                    }

                    byte[] bytes = new byte[view.Length];
                    Buffer.BlockCopy(binary, view.Offset, bytes, 0, view.Length);
                    result[i] = bytes;
                }
                else if (imageData.TryGetProperty(
                             "uri", out JsonElement uriElement))
                {
                    string uri = uriElement.GetString();

                    if (string.IsNullOrWhiteSpace(uri))
                        throw new InvalidDataException("Image URI is empty.");

                    if (uri.StartsWith(
                            "data:", StringComparison.OrdinalIgnoreCase))
                    {
                        int comma = uri.IndexOf(',');

                        if (comma < 0 ||
                            uri.IndexOf(
                                ";base64", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            throw new NotSupportedException(
                                "Only base64 data URIs are supported for images.");
                        }

                        result[i] = Convert.FromBase64String(
                            uri.Substring(comma + 1));
                    }
                    else
                    {
                        if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                        {
                            Uri absoluteUri = new Uri(uri);

                            if (!absoluteUri.IsFile)
                                throw new NotSupportedException(
                                    "Remote image URLs are not supported.");

                            uri = absoluteUri.LocalPath;
                        }

                        string decoded = Uri.UnescapeDataString(uri);
                        string fullPath = Path.GetFullPath(
                            Path.Combine(directory, decoded));

                        StringComparison pathComparison =
                            Path.DirectorySeparatorChar == '\\'
                                ? StringComparison.OrdinalIgnoreCase
                                : StringComparison.Ordinal;

                        if (!fullPath.StartsWith(
                                directoryPrefix, pathComparison))
                        {
                            throw new InvalidDataException(
                                "Image URI points outside the model directory.");
                        }

                        result[i] = File.ReadAllBytes(fullPath);
                    }
                }
                else
                {
                    throw new InvalidDataException(
                        "Image has neither a URI nor a bufferView.");
                }
            }

            return result;
        }

        private static IEnumerator CreateImagesIncremental(
            JsonElement gltf,
            byte[][] imageBytes,
            Action<List<GltfImage>> onCompleted)
        {
            List<GltfImage> images = new List<GltfImage>();
            JsonElement imageArray = OptionalArray(gltf, "images");

            if (imageArray.ValueKind != JsonValueKind.Array)
            {
                onCompleted(images);
                yield break;
            }

            int count = imageArray.GetArrayLength();

            if (imageBytes == null || imageBytes.Length != count)
            {
                throw new InvalidDataException(
                    "Prepared image data does not match the glTF image array.");
            }

            for (int i = 0; i < count; i++)
            {
                GltfImage image = new GltfImage();
                byte[] bytes = imageBytes[i];

                if (bytes != null)
                {
                    if (bytes.Length == 0)
                        throw new InvalidDataException("Image data is empty.");

                    Texture2D texture = new Texture2D(
                        2, 2, TextureFormat.RGBA32, true);

                    texture.name = GetString(
                        imageArray[i], "name", "GLTF_Image");

                    try
                    {
                        if (!ImageConversion.LoadImage(texture, bytes, false))
                        {
                            throw new InvalidDataException(
                                "Unity could not decode a GLTF image.");
                        }

                        texture.wrapMode = TextureWrapMode.Repeat;
                        texture.filterMode = FilterMode.Bilinear;
                        texture.anisoLevel = 4;

                        image.Texture = texture;
                    }
                    catch
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw;
                    }
                }

                images.Add(image);
                yield return null;
            }

            onCompleted(images);
        }

        private static List<GltfImage> ReadImages(
            JsonElement gltf,
            string glbPath,
            byte[] binary,
            List<BufferView> views,
            bool loadTextures)
        {
            List<GltfImage> result = new List<GltfImage>();
            JsonElement images = OptionalArray(gltf, "images");

            if (images.ValueKind != JsonValueKind.Array)
                return result;

            string directory = Path.GetDirectoryName(Path.GetFullPath(glbPath));
            string directoryPrefix = Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            foreach (JsonElement imageData in images.EnumerateArray())
            {
                GltfImage image = new GltfImage();

                if (loadTextures)
                {
                    byte[] bytes;

                    if (imageData.TryGetProperty(
                            "bufferView", out JsonElement viewElement))
                    {
                        int viewIndex = viewElement.GetInt32();

                        if (viewIndex < 0 || viewIndex >= views.Count)
                            throw new InvalidDataException(
                                "Image bufferView index is out of range.");

                        BufferView view = views[viewIndex];
                        long viewEnd = (long)view.Offset + view.Length;

                        if (view.Offset < 0 || view.Length < 0 ||
                            viewEnd > binary.Length)
                        {
                            throw new InvalidDataException(
                                "Image bufferView exceeds the binary chunk.");
                        }

                        bytes = new byte[view.Length];
                        Buffer.BlockCopy(
                            binary, view.Offset, bytes, 0, view.Length);
                    }
                    else if (imageData.TryGetProperty(
                                 "uri", out JsonElement uriElement))
                    {
                        string uri = uriElement.GetString();

                        if (string.IsNullOrWhiteSpace(uri))
                            throw new InvalidDataException("Image URI is empty.");

                        if (uri.StartsWith(
                                "data:", StringComparison.OrdinalIgnoreCase))
                        {
                            int comma = uri.IndexOf(',');

                            if (comma < 0 ||
                                uri.IndexOf(
                                    ";base64", StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                throw new NotSupportedException(
                                    "Only base64 data URIs are supported for images.");
                            }

                            bytes = Convert.FromBase64String(uri.Substring(comma + 1));
                        }
                        else
                        {
                            if (Uri.IsWellFormedUriString(uri, UriKind.Absolute))
                            {
                                Uri absoluteUri = new Uri(uri);
                                if (!absoluteUri.IsFile)
                                    throw new NotSupportedException(
                                        "Remote image URLs are not supported.");

                                uri = absoluteUri.LocalPath;
                            }

                            string decoded = Uri.UnescapeDataString(uri);
                            string fullPath = Path.GetFullPath(
                                Path.Combine(directory, decoded));

                            StringComparison pathComparison =
                                Path.DirectorySeparatorChar == '\\'
                                    ? StringComparison.OrdinalIgnoreCase
                                    : StringComparison.Ordinal;

                            if (!fullPath.StartsWith(
                                    directoryPrefix, pathComparison))
                            {
                                throw new InvalidDataException(
                                    "Image URI points outside the model directory.");
                            }

                            bytes = File.ReadAllBytes(fullPath);
                        }
                    }
                    else
                    {
                        throw new InvalidDataException(
                            "Image has neither a URI nor a bufferView.");
                    }

                    if (bytes.Length == 0)
                        throw new InvalidDataException("Image data is empty.");

                    Texture2D texture = new Texture2D(
                        2, 2, TextureFormat.RGBA32, true);
                    texture.name = GetString(imageData, "name", "GLTF_Image");

                    if (!ImageConversion.LoadImage(texture, bytes, false))
                    {
                        UnityEngine.Object.Destroy(texture);
                        throw new InvalidDataException(
                            "Unity could not decode a GLTF image.");
                    }

                    texture.wrapMode = TextureWrapMode.Repeat;
                    texture.filterMode = FilterMode.Bilinear;
                    texture.anisoLevel = 4;
                    image.Texture = texture;
                }

                result.Add(image);
            }

            return result;
        }

        private static List<GltfTexture> ReadTextures(JsonElement gltf)
        {
            List<GltfTexture> result = new List<GltfTexture>();
            JsonElement textures = OptionalArray(gltf, "textures");

            if (textures.ValueKind != JsonValueKind.Array)
                return result;

            foreach (JsonElement textureData in textures.EnumerateArray())
            {
                result.Add(new GltfTexture
                {
                    Source = GetInt(textureData, "source", -1)
                });
            }

            return result;
        }

        private static Texture2D ResolveTexture(
            JsonElement textureInfo,
            List<GltfImage> images,
            List<GltfTexture> textures)
        {
            if (!textureInfo.TryGetProperty(
                    "index", out JsonElement indexElement))
                return null;

            int textureIndex = indexElement.GetInt32();
            if (textureIndex < 0 || textureIndex >= textures.Count)
                throw new InvalidDataException(
                    "Material texture index is out of range.");

            int imageIndex = textures[textureIndex].Source;
            if (imageIndex < 0 || imageIndex >= images.Count)
                throw new InvalidDataException(
                    "Texture source image index is out of range.");

            return images[imageIndex].Texture;
        }

        private static List<GltfMaterial> ReadMaterials(
            JsonElement gltf,
            List<GltfImage> images,
            List<GltfTexture> textures,
            bool loadTextures)
        {
            List<GltfMaterial> result = new List<GltfMaterial>();
            JsonElement materialArray = OptionalArray(gltf, "materials");

            if (materialArray.ValueKind != JsonValueKind.Array)
                return result;

            foreach (JsonElement materialData in materialArray.EnumerateArray())
            {
                Material material = CreateDefaultMaterial();
                material.name = GetString(
                    materialData, "name", "GLTF_Material");

                if (materialData.TryGetProperty(
                        "pbrMetallicRoughness", out JsonElement pbr))
                {
                    Color baseColor = Color.white;
                    if (pbr.TryGetProperty(
                            "baseColorFactor", out JsonElement colorFactor) &&
                        colorFactor.ValueKind == JsonValueKind.Array &&
                        colorFactor.GetArrayLength() == 4)
                    {
                        baseColor = new Color(
                            colorFactor[0].GetSingle(),
                            colorFactor[1].GetSingle(),
                            colorFactor[2].GetSingle(),
                            colorFactor[3].GetSingle());
                    }

                    SetMaterialColor(
                        material, "_BaseColor", "_Color", baseColor);

                    float metallic = Mathf.Clamp01(
                        GetFloat(pbr, "metallicFactor", 1f));

                    float roughness = Mathf.Clamp01(
                        GetFloat(pbr, "roughnessFactor", 1f));

                    bool packedMetallicRoughness = false;

                    if (loadTextures &&
                        pbr.TryGetProperty(
                            "metallicRoughnessTexture",
                            out JsonElement metallicRoughnessInfo))
                    {
                        Texture2D source = ResolveTexture(
                            metallicRoughnessInfo, images, textures);

                        if (source != null &&
                            material.HasProperty("_MetallicGlossMap"))
                        {
                            Texture2D packed = PackMetallicRoughness(
                                source, metallic, roughness);

                            material.SetTexture("_MetallicGlossMap", packed);

                            // URP Lit and the Built-in Standard shader use
                            // different keywords for metallic-gloss maps.
                            if (material.shader != null &&
                                material.shader.name.StartsWith(
                                    "Universal Render Pipeline/",
                                    StringComparison.Ordinal))
                            {
                                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                                material.DisableKeyword("_METALLICGLOSSMAP");
                            }
                            else
                            {
                                material.EnableKeyword("_METALLICGLOSSMAP");
                                material.DisableKeyword("_METALLICSPECGLOSSMAP");
                            }

                            // The scalar factors are already baked into the map.
                            // Avoid applying them a second time.
                            if (material.HasProperty("_Metallic"))
                                material.SetFloat("_Metallic", 1f);

                            if (material.HasProperty("_Smoothness"))
                                material.SetFloat("_Smoothness", 1f);

                            if (material.HasProperty("_Glossiness"))
                                material.SetFloat("_Glossiness", 1f);

                            packedMetallicRoughness = true;
                        }
                    }

                    // If the texture is absent or the shader cannot use it,
                    // preserve the glTF scalar factors as the fallback.
                    if (!packedMetallicRoughness)
                    {
                        if (material.HasProperty("_Metallic"))
                            material.SetFloat("_Metallic", metallic * 0.85f);

                        float smoothness = 1f - roughness;

                        // Slightly reduce excessive shine without flattening the textures.
                        smoothness *= 0.85f;

                        if (material.HasProperty("_Smoothness"))
                            material.SetFloat("_Smoothness", smoothness);

                        if (material.HasProperty("_Glossiness"))
                            material.SetFloat("_Glossiness", smoothness);
                    }

                    if (loadTextures && pbr.TryGetProperty(
                            "baseColorTexture", out JsonElement baseTextureInfo))
                    {
                        Texture2D baseTexture = ResolveTexture(
                            baseTextureInfo, images, textures);
                        SetMaterialTexture(
                            material, "_BaseMap", "_MainTex", baseTexture);
                    }
                }

                if (loadTextures && materialData.TryGetProperty(
                        "normalTexture", out JsonElement normalInfo))
                {
                    Texture2D normalTexture = ResolveTexture(
                        normalInfo, images, textures);
                    SetMaterialTexture(
                        material, "_BumpMap", "_BumpMap", normalTexture);

                    if (normalTexture != null &&
                        material.HasProperty("_BumpScale"))
                    {
                        material.SetFloat(
                            "_BumpScale", GetFloat(normalInfo, "scale", 1f));
                        material.EnableKeyword("_NORMALMAP");
                    }
                }

                if (loadTextures && materialData.TryGetProperty(
                        "emissiveTexture", out JsonElement emissiveInfo))
                {
                    Texture2D emissiveTexture = ResolveTexture(
                        emissiveInfo, images, textures);
                    if (emissiveTexture != null &&
                        material.HasProperty("_EmissionMap"))
                    {
                        material.SetTexture("_EmissionMap", emissiveTexture);
                        material.EnableKeyword("_EMISSION");
                    }
                }

                if (materialData.TryGetProperty(
                        "emissiveFactor", out JsonElement emissiveFactor) &&
                    emissiveFactor.ValueKind == JsonValueKind.Array &&
                    emissiveFactor.GetArrayLength() == 3)
                {
                    Color emissive = new Color(
                        emissiveFactor[0].GetSingle(),
                        emissiveFactor[1].GetSingle(),
                        emissiveFactor[2].GetSingle(),
                        1f);

                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.SetColor("_EmissionColor", emissive);
                        if (emissive.maxColorComponent > 0f)
                            material.EnableKeyword("_EMISSION");
                    }
                }

                string alphaMode = GetString(
                    materialData, "alphaMode", "OPAQUE");
                if (alphaMode == "BLEND")
                {
                    ConfigureTransparentMaterial(material);
                }
                else if (alphaMode == "MASK")
                {
                    if (material.HasProperty("_AlphaClip"))
                        material.SetFloat("_AlphaClip", 1f);
                    if (material.HasProperty("_Cutoff"))
                    {
                        material.SetFloat(
                            "_Cutoff", GetFloat(materialData, "alphaCutoff", 0.5f));
                    }
                    material.EnableKeyword("_ALPHATEST_ON");
                }

                if (GetBool(materialData, "doubleSided", false))
                {
                    if (material.HasProperty("_Cull"))
                        material.SetFloat("_Cull", 0f);
                    material.SetInt(
                        "_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                }

                result.Add(new GltfMaterial { Material = material });
            }

            return result;
        }

        private static void SetMaterialColor(
            Material material,
            string primaryProperty,
            string fallbackProperty,
            Color color)
        {
            if (material.HasProperty(primaryProperty))
                material.SetColor(primaryProperty, color);
            else if (material.HasProperty(fallbackProperty))
                material.SetColor(fallbackProperty, color);
        }

        private static void SetMaterialTexture(
            Material material,
            string primaryProperty,
            string fallbackProperty,
            Texture texture)
        {
            if (texture == null)
                return;

            if (material.HasProperty(primaryProperty))
                material.SetTexture(primaryProperty, texture);
            else if (material.HasProperty(fallbackProperty))
                material.SetTexture(fallbackProperty, texture);
        }

        private static Texture2D PackMetallicRoughness(
            Texture2D source,
            float metallicFactor,
            float roughnessFactor)
        {
            Color[] pixels = source.GetPixels();

            metallicFactor = Mathf.Clamp01(metallicFactor);
            roughnessFactor = Mathf.Clamp01(roughnessFactor);

            for (int i = 0; i < pixels.Length; i++)
            {
                Color pixel = pixels[i];

                // glTF metallic-roughness texture:
                //   Green = roughness
                //   Blue  = metallic
                //
                // Unity metallic-gloss map:
                //   Red   = metallic
                //   Alpha = smoothness

                float metallic = Mathf.Clamp01(
                    pixel.b * metallicFactor);

                float roughness = Mathf.Clamp01(
                    pixel.g * roughnessFactor);

                float smoothness = 1f - roughness;

                pixels[i] = new Color(
                    metallic,
                    0f,
                    0f,
                    smoothness);
            }

            Texture2D packed = new Texture2D(
                source.width,
                source.height,
                TextureFormat.RGBA32,
                true);

            packed.name = source.name + "_MetallicSmoothness";

            packed.SetPixels(pixels);
            packed.Apply(true, false);

            packed.wrapMode = source.wrapMode;
            packed.filterMode = source.filterMode;
            packed.anisoLevel = source.anisoLevel;

            return packed;
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Mode"))
                material.SetFloat("_Mode", 3f);

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetInt(
                "_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt(
                "_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue =
                (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static float GetFloat(
            JsonElement element,
            string name,
            float fallback)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.Number
                ? value.GetSingle()
                : fallback;
        }

        private static Vector3[] ReadVector3(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int accessorIndex,
            bool unusedFlip,
            bool isNormal)
        {
            Accessor accessor = GetAccessor(accessors, accessorIndex);

            if (accessor.Type != "VEC3" || accessor.ComponentType != 5126)
                throw new NotSupportedException(
                    "VEC3 attributes must currently use FLOAT components.");

            byte[] raw = ReadAccessorBytes(binary, views, accessor, 12);
            Vector3[] result = new Vector3[accessor.Count];

            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 12;
                float x = ReadFloat(raw, offset);
                float y = ReadFloat(raw, offset + 4);
                float z = ReadFloat(raw, offset + 8);
                result[i] = new Vector3(x, y, z);
            }

            return result;
        }

        private static Vector4[] ReadTangents(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int accessorIndex)
        {
            Accessor accessor = GetAccessor(accessors, accessorIndex);

            if (accessor.Type != "VEC4" ||
                accessor.ComponentType != 5126 ||
                accessor.Normalized)
            {
                throw new NotSupportedException(
                    "TANGENT attributes must use non-normalized FLOAT VEC4.");
            }

            byte[] raw = ReadAccessorBytes(
                binary, views, accessor, 16);

            Vector4[] result = new Vector4[accessor.Count];

            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 16;

                result[i] = new Vector4(
                    ReadFloat(raw, offset),
                    ReadFloat(raw, offset + 4),
                    ReadFloat(raw, offset + 8),
                    ReadFloat(raw, offset + 12));
            }

            return result;
        }

        private static Color[] ReadColors(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int accessorIndex)
        {
            Accessor accessor = GetAccessor(accessors, accessorIndex);

            if (accessor.Type != "VEC3" && accessor.Type != "VEC4")
                throw new InvalidDataException(
                    "COLOR_0 must use VEC3 or VEC4.");

            int componentCount = accessor.Type == "VEC3" ? 3 : 4;
            int componentSize;

            switch (accessor.ComponentType)
            {
                case 5126: // FLOAT
                    componentSize = 4;

                    if (accessor.Normalized)
                        throw new InvalidDataException(
                            "FLOAT COLOR_0 cannot be normalized.");
                    break;

                case 5121: // UNSIGNED_BYTE
                    componentSize = 1;
                    if (!accessor.Normalized)
                        throw new NotSupportedException(
                            "Integer COLOR_0 must be normalized.");
                    break;

                case 5123: // UNSIGNED_SHORT
                    componentSize = 2;
                    if (!accessor.Normalized)
                        throw new NotSupportedException(
                            "Integer COLOR_0 must be normalized.");
                    break;

                default:
                    throw new NotSupportedException(
                        "Unsupported COLOR_0 component type: " +
                        accessor.ComponentType);
            }

            int elementSize = componentCount * componentSize;

            byte[] raw = ReadAccessorBytes(
                binary, views, accessor, elementSize);

            Color[] result = new Color[accessor.Count];

            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * elementSize;

                float r = ReadColorComponent(
                    raw, offset, accessor.ComponentType);
                float g = ReadColorComponent(
                    raw, offset + componentSize, accessor.ComponentType);
                float b = ReadColorComponent(
                    raw, offset + componentSize * 2, accessor.ComponentType);

                float a = componentCount == 4
                    ? ReadColorComponent(
                        raw, offset + componentSize * 3,
                        accessor.ComponentType)
                    : 1f;

                result[i] = new Color(r, g, b, a);
            }

            return result;
        }

        private static float ReadColorComponent(
            byte[] raw,
            int offset,
            int componentType)
        {
            switch (componentType)
            {
                case 5126: // FLOAT
                    return ReadFloat(raw, offset);

                case 5121: // UNSIGNED_BYTE
                    return raw[offset] / 255f;

                case 5123: // UNSIGNED_SHORT
                    int value = raw[offset] | (raw[offset + 1] << 8);
                    return value / 65535f;

                default:
                    throw new NotSupportedException(
                        "Unsupported vertex color component type.");
            }
        }

        private static Vector2[] ReadVector2(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int accessorIndex,
            bool flipV)
        {
            Accessor accessor = GetAccessor(accessors, accessorIndex);

            if (accessor.Type != "VEC2" ||
                accessor.ComponentType != 5126 ||
                accessor.Normalized)
            {
                throw new NotSupportedException(
                    "Texture coordinates must use non-normalized FLOAT VEC2.");
            }

            byte[] raw = ReadAccessorBytes(binary, views, accessor, 8);
            Vector2[] result = new Vector2[accessor.Count];

            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * 8;
                float u = ReadFloat(raw, offset);
                float v = ReadFloat(raw, offset + 4);

                result[i] = new Vector2(u, flipV ? 1f - v : v);
            }

            return result;
        }

        private static int[] ReadIndices(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int accessorIndex)
        {
            Accessor accessor = GetAccessor(accessors, accessorIndex);

            if (accessor.Type != "SCALAR")
                throw new InvalidDataException("Index accessor must be SCALAR.");

            int componentSize;

            switch (accessor.ComponentType)
            {
                case 5121: componentSize = 1; break;
                case 5123: componentSize = 2; break;
                case 5125: componentSize = 4; break;
                default:
                    throw new NotSupportedException("Unsupported index component type.");
            }

            byte[] raw = ReadAccessorBytes(binary, views, accessor, componentSize);
            int[] result = new int[accessor.Count];

            for (int i = 0; i < result.Length; i++)
            {
                int offset = i * componentSize;

                if (componentSize == 1)
                    result[i] = raw[offset];
                else if (componentSize == 2)
                    result[i] = raw[offset] | (raw[offset + 1] << 8);
                else
                {
                    uint value = (uint)(
                        raw[offset] |
                        (raw[offset + 1] << 8) |
                        (raw[offset + 2] << 16) |
                        (raw[offset + 3] << 24));

                    if (value > int.MaxValue)
                        throw new InvalidDataException("Index value is too large.");

                    result[i] = (int)value;
                }
            }

            return result;
        }

        private static byte[] ReadAccessorBytes(
            byte[] binary,
            List<BufferView> views,
            Accessor accessor,
            int elementSize)
        {
            if (accessor.View < 0 || accessor.View >= views.Count)
                throw new NotSupportedException(
                    "Sparse or buffer-less accessors are not supported yet.");

            if (elementSize <= 0)
                throw new InvalidDataException("Invalid accessor element size.");

            BufferView view = views[accessor.View];

            if (view.Offset < 0 || view.Length < 0 ||
                accessor.Offset < 0 || accessor.Count < 0)
            {
                throw new InvalidDataException(
                    "Invalid accessor or bufferView values.");
            }

            int stride = view.Stride > 0 ? view.Stride : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException("BufferView stride is too small.");

            long start = (long)view.Offset + accessor.Offset;
            long end = accessor.Count == 0
                ? start
                : start + (long)(accessor.Count - 1) * stride + elementSize;

            long viewEnd = (long)view.Offset + view.Length;

            if (start < view.Offset ||
                start > viewEnd ||
                end > viewEnd ||
                end > binary.Length)
            {
                throw new InvalidDataException(
                    "Accessor exceeds its bufferView.");
            }

            int outputLength = checked(accessor.Count * elementSize);
            byte[] result = new byte[outputLength];

            if (accessor.Count == 0)
                return result;

            int sourceOffset = checked((int)start);

            // Fast path: copy tightly packed accessor data in one operation.
            if (stride == elementSize)
            {
                Buffer.BlockCopy(
                    binary,
                    sourceOffset,
                    result,
                    0,
                    outputLength);

                return result;
            }

            // Fallback: handle interleaved vertex attributes.
            for (int i = 0; i < accessor.Count; i++)
            {
                Buffer.BlockCopy(
                    binary,
                    checked(sourceOffset + i * stride),
                    result,
                    i * elementSize,
                    elementSize);
            }

            return result;
        }

        private static Accessor GetAccessor(List<Accessor> accessors, int index)
        {
            if (index < 0 || index >= accessors.Count)
                throw new InvalidDataException("Accessor index is out of range.");

            return accessors[index];
        }

        private static void ApplyNodeTransform(Transform transform, JsonElement node)
        {
            // glTF specifies either a matrix OR translation/rotation/scale.
            if (node.TryGetProperty("matrix", out JsonElement matrix))
            {
                if (matrix.ValueKind != JsonValueKind.Array ||
                    matrix.GetArrayLength() != 16)
                {
                    throw new InvalidDataException(
                        "Node matrix must contain exactly 16 values.");
                }

                float[] m = new float[16];
                for (int i = 0; i < 16; i++)
                    m[i] = matrix[i].GetSingle();

                // glTF matrices are column-major.
                // Extract translation from the fourth column.
                Vector3 position = new Vector3(m[12], m[13], m[14]);

                // Extract scale from the lengths of the basis vectors.
                Vector3 columnX = new Vector3(m[0], m[1], m[2]);
                Vector3 columnY = new Vector3(m[4], m[5], m[6]);
                Vector3 columnZ = new Vector3(m[8], m[9], m[10]);

                Vector3 scale = new Vector3(
                    columnX.magnitude,
                    columnY.magnitude,
                    columnZ.magnitude);

                if (scale.x < 0.000001f ||
                    scale.y < 0.000001f ||
                    scale.z < 0.000001f)
                {
                    throw new NotSupportedException(
                        "Node matrix contains a zero-scale axis.");
                }

                // Normalize the basis vectors before creating the rotation.
                columnX /= scale.x;
                columnY /= scale.y;
                columnZ /= scale.z;

                // Preserve a reflection by assigning its sign to the X scale.
                if (Vector3.Dot(Vector3.Cross(columnX, columnY), columnZ) < 0f)
                {
                    scale.x = -scale.x;
                    columnX = -columnX;
                }

                Matrix4x4 rotationMatrix = Matrix4x4.identity;

                rotationMatrix.m00 = columnX.x;
                rotationMatrix.m10 = columnX.y;
                rotationMatrix.m20 = columnX.z;

                rotationMatrix.m01 = columnY.x;
                rotationMatrix.m11 = columnY.y;
                rotationMatrix.m21 = columnY.z;

                rotationMatrix.m02 = columnZ.x;
                rotationMatrix.m12 = columnZ.y;
                rotationMatrix.m22 = columnZ.z;

                transform.localPosition = position;
                transform.localRotation = rotationMatrix.rotation;
                transform.localScale = scale;
                return;
            }

            if (node.TryGetProperty("translation", out JsonElement translation))
            {
                transform.localPosition = new Vector3(
                    translation[0].GetSingle(),
                    translation[1].GetSingle(),
                    translation[2].GetSingle());
            }

            if (node.TryGetProperty("rotation", out JsonElement rotation))
            {
                transform.localRotation = new Quaternion(
                    rotation[0].GetSingle(),
                    rotation[1].GetSingle(),
                    rotation[2].GetSingle(),
                    rotation[3].GetSingle());
            }

            if (node.TryGetProperty("scale", out JsonElement scaleElement))
            {
                transform.localScale = new Vector3(
                    scaleElement[0].GetSingle(),
                    scaleElement[1].GetSingle(),
                    scaleElement[2].GetSingle());
            }
        }

        private static JsonElement RequiredArray(JsonElement element, string name)
        {
            if (!element.TryGetProperty(name, out JsonElement array) ||
                array.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Required glTF array is missing: " + name);

            return array;
        }

        private static JsonElement OptionalArray(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out JsonElement array) &&
                array.ValueKind == JsonValueKind.Array)
                return array;

            return default;
        }

        private static int GetInt(JsonElement element, string name, int fallback)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                   value.TryGetInt32(out int result)
                ? result
                : fallback;
        }

        private static bool GetBool(JsonElement element, string name, bool fallback)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                   (value.ValueKind == JsonValueKind.True ||
                    value.ValueKind == JsonValueKind.False)
                ? value.GetBoolean()
                : fallback;
        }

        private static string GetString(
            JsonElement element,
            string name,
            string fallback)
        {
            return element.TryGetProperty(name, out JsonElement value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : fallback;
        }

        private static float ReadFloat(byte[] bytes, int offset)
        {
            if (BitConverter.IsLittleEndian)
                return BitConverter.ToSingle(bytes, offset);

            byte[] copy = new byte[4];
            copy[0] = bytes[offset + 3];
            copy[1] = bytes[offset + 2];
            copy[2] = bytes[offset + 1];
            copy[3] = bytes[offset];
            return BitConverter.ToSingle(copy, 0);
        }
    }
}
