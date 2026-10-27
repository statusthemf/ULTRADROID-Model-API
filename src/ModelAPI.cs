
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;

namespace ULTRADROID.Models
{
    [BepInPlugin(
        "somerandommaid.ultradroid.models",
        "ULTRADROID Models API",
        "0.2.0")]
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

    public sealed class ModelInstance
    {
        public GameObject Root { get; }
        public string SourcePath { get; }
        public bool IsValid => Root != null;

        internal ModelInstance(GameObject root, string path)
        {
            Root = root;
            SourcePath = path;
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
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A GLB path is required.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("GLB file not found.", path);

            return GlbImporter.Load(path, options ?? new ModelLoadOptions());
        }
    }

    internal static class GlbImporter
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinChunk = 0x004E4942;

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

        internal static ModelInstance Load(string path, ModelLoadOptions options)
        {
            GameObject rootObject = null;

            try
            {
                ReadGlb(path, out string json, out byte[] binary);

                using (JsonDocument doc = JsonDocument.Parse(json))
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
                    JsonElement meshes = RequiredArray(gltf, "meshes");
                    JsonElement nodes = OptionalArray(gltf, "nodes");

                    rootObject = new GameObject(
                        string.IsNullOrWhiteSpace(options.Name)
                            ? "CustomModel"
                            : options.Name);

                    // Parent first, then apply the requested transform.
                    // When Parent is supplied, Position and RotationEuler are local.
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

                    GameObject[] nodeObjects = new GameObject[
                        nodes.ValueKind == JsonValueKind.Array ? nodes.GetArrayLength() : 0];

                    // Create node objects first, so children can be parented safely.
                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        JsonElement node = nodes[i];
                        string nodeName = GetString(node, "name", "Node_" + i);
                        nodeObjects[i] = new GameObject(nodeName);
                        ApplyNodeTransform(nodeObjects[i].transform, node);
                    }

                    bool[] hasParent = new bool[nodeObjects.Length];

                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        JsonElement children;

                        if (!nodes[i].TryGetProperty("children", out children) ||
                            children.ValueKind != JsonValueKind.Array)
                            continue;

                        foreach (JsonElement childElement in children.EnumerateArray())
                        {
                            int child = childElement.GetInt32();
                            if (child < 0 || child >= nodeObjects.Length)
                                throw new InvalidDataException("Node child index is out of range.");

                            if (hasParent[child])
                                throw new InvalidDataException("A node has multiple parents.");

                            nodeObjects[child].transform.SetParent(nodeObjects[i].transform, false);
                            hasParent[child] = true;
                        }
                    }

                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        if (!hasParent[i])
                            nodeObjects[i].transform.SetParent(rootObject.transform, false);
                    }

                    int meshCount = 0;

                    // Meshes may be referenced by nodes. Build each referenced primitive.
                    for (int i = 0; i < nodeObjects.Length; i++)
                    {
                        JsonElement node = nodes[i];

                        if (!node.TryGetProperty("mesh", out JsonElement meshElement))
                            continue;

                        int meshIndex = meshElement.GetInt32();
                        if (meshIndex < 0 || meshIndex >= meshes.GetArrayLength())
                            throw new InvalidDataException("Node mesh index is out of range.");

                        BuildMesh(
                            meshes[meshIndex],
                            binary,
                            views,
                            accessors,
                            nodeObjects[i].transform,
                            options);

                        meshCount++;
                    }

                    // Some valid files have meshes but no nodes. Render those directly
                    // under the model root rather than returning an empty model.
                    if (nodeObjects.Length == 0)
                    {
                        for (int i = 0; i < meshes.GetArrayLength(); i++)
                        {
                            BuildMesh(
                                meshes[i],
                                binary,
                                views,
                                accessors,
                                rootObject.transform,
                                options);
                            meshCount++;
                        }
                    }

                    if (meshCount == 0)
                        throw new NotSupportedException("No node-referenced meshes were found.");

                    return new ModelInstance(rootObject, path);
                }
            }
            catch
            {
                if (rootObject != null)
                    UnityEngine.Object.Destroy(rootObject);

                throw;
            }
        }

        private static void ReadGlb(string path, out string json, out byte[] binary)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length < 20)
                throw new InvalidDataException("GLB file is too short.");

            using (MemoryStream stream = new MemoryStream(bytes))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (reader.ReadUInt32() != GlbMagic)
                    throw new InvalidDataException("Invalid GLB magic.");

                if (reader.ReadUInt32() != 2)
                    throw new InvalidDataException("Only GLB version 2 is supported.");

                uint declaredLength = reader.ReadUInt32();
                if (declaredLength != bytes.Length)
                    throw new InvalidDataException("GLB declared length does not match file length.");

                json = null;
                binary = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    uint length = reader.ReadUInt32();
                    uint type = reader.ReadUInt32();

                    if (length > stream.Length - stream.Position)
                        throw new InvalidDataException("Invalid GLB chunk length.");

                    byte[] chunk = reader.ReadBytes((int)length);

                    if (type == JsonChunk)
                        json = Encoding.UTF8.GetString(chunk).TrimEnd('\0', ' ', '\t', '\r', '\n');
                    else if (type == BinChunk)
                        binary = chunk;
                }

                if (string.IsNullOrEmpty(json))
                    throw new InvalidDataException("GLB JSON chunk is missing.");

                if (binary == null)
                    throw new InvalidDataException("GLB binary chunk is missing.");
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
            Transform parent,
            ModelLoadOptions options)
        {
            JsonElement primitives = RequiredArray(meshData, "primitives");
            string meshName = GetString(meshData, "name", "Mesh");

            foreach (JsonElement primitive in primitives.EnumerateArray())
            {
                int mode = GetInt(primitive, "mode", 4);

                if (mode != 4)
                    throw new NotSupportedException(
                        "Only TRIANGLES primitives are currently supported.");

                if (!primitive.TryGetProperty("attributes", out JsonElement attributes) ||
                    !attributes.TryGetProperty("POSITION", out JsonElement positionElement))
                    throw new InvalidDataException("Mesh primitive has no POSITION attribute.");

                int positionAccessor = positionElement.GetInt32();
                Vector3[] vertices = ReadVector3(
                    binary, views, accessors, positionAccessor, options.FlipUvV, false);

                if (vertices.Length == 0)
                    throw new InvalidDataException("Mesh has no vertices.");

                Vector3[] normals = null;
                Vector2[] uvs = null;

                if (attributes.TryGetProperty("NORMAL", out JsonElement normalElement))
                    normals = ReadVector3(
                        binary, views, accessors, normalElement.GetInt32(), false, true);

                if (attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvElement))
                    uvs = ReadVector2(
                        binary, views, accessors, uvElement.GetInt32(), options.FlipUvV);

                int[] triangles;

                if (primitive.TryGetProperty("indices", out JsonElement indexElement))
                {
                    triangles = ReadIndices(
                        binary, views, accessors, indexElement.GetInt32());
                }
                else
                {
                    triangles = new int[vertices.Length];
                    for (int i = 0; i < vertices.Length; i++)
                        triangles[i] = i;
                }

                if (triangles.Length % 3 != 0)
                    throw new InvalidDataException("Triangle index count is not divisible by 3.");

                for (int i = 0; i < triangles.Length; i++)
                {
                    if (triangles[i] < 0 || triangles[i] >= vertices.Length)
                        throw new InvalidDataException("Mesh index is out of range.");
                }

                Mesh unityMesh = new Mesh();
                unityMesh.name = meshName;

                if (vertices.Length > 65535)
                    unityMesh.indexFormat = IndexFormat.UInt32;

                unityMesh.vertices = vertices;
                unityMesh.triangles = triangles;

                if (normals != null && normals.Length == vertices.Length)
                    unityMesh.normals = normals;
                else
                    unityMesh.RecalculateNormals();

                if (uvs != null && uvs.Length == vertices.Length)
                    unityMesh.uv = uvs;

                unityMesh.RecalculateBounds();

                GameObject primitiveObject = new GameObject(meshName);
                primitiveObject.transform.SetParent(parent, false);

                MeshFilter filter = primitiveObject.AddComponent<MeshFilter>();
                filter.sharedMesh = unityMesh;

                MeshRenderer renderer = primitiveObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = CreateDefaultMaterial();

                if (options.CreateColliders)
                {
                    MeshCollider collider = primitiveObject.AddComponent<MeshCollider>();
                    collider.sharedMesh = unityMesh;
                }
            }
        }

        private static Material CreateDefaultMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");

            if (shader == null)
                throw new NotSupportedException("No compatible Unity shader was found.");

            return new Material(shader);
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

        private static Vector2[] ReadVector2(
            byte[] binary,
            List<BufferView> views,
            List<Accessor> accessors,
            int accessorIndex,
            bool flipV)
        {
            Accessor accessor = GetAccessor(accessors, accessorIndex);

            if (accessor.Type != "VEC2" || accessor.ComponentType != 5126)
                throw new NotSupportedException(
                    "TEXCOORD_0 must currently use FLOAT components.");

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

            BufferView view = views[accessor.View];

            if (view.Offset < 0 || view.Length < 0 || accessor.Offset < 0 ||
                accessor.Count < 0)
                throw new InvalidDataException("Invalid accessor or bufferView values.");

            int stride = view.Stride > 0 ? view.Stride : elementSize;

            if (stride < elementSize)
                throw new InvalidDataException("BufferView stride is too small.");

            long start = (long)view.Offset + accessor.Offset;
            long end = accessor.Count == 0
                ? start
                : start + (long)(accessor.Count - 1) * stride + elementSize;

            if (start < view.Offset ||
                end > (long)view.Offset + view.Length ||
                end > binary.Length)
                throw new InvalidDataException("Accessor exceeds its bufferView.");

            byte[] result = new byte[checked(accessor.Count * elementSize)];

            for (int i = 0; i < accessor.Count; i++)
                Buffer.BlockCopy(
                    binary,
                    checked((int)(start + (long)i * stride)),
                    result,
                    i * elementSize,
                    elementSize);

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
