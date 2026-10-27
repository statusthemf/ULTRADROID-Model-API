
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEngine;

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
        public const string Version = "0.1.0";

        public static ModelInstance LoadGLB(
            string path,
            ModelLoadOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A GLB path is required.", nameof(path));

            if (!File.Exists(path))
                throw new FileNotFoundException("GLB file not found.", path);

            options ??= new ModelLoadOptions();

            return GlbImporter.Load(path, options);
        }
    }

    internal static class GlbImporter
    {
        private const uint GlbMagic = 0x46546C67;
        private const uint JsonChunk = 0x4E4F534A;
        private const uint BinChunk = 0x004E4942;

        internal static ModelInstance Load(
            string path,
            ModelLoadOptions options)
        {
            byte[] bytes = File.ReadAllBytes(path);

            if (bytes.Length < 20)
                throw new InvalidDataException("GLB file is too short.");

            using (var stream = new MemoryStream(bytes))
            using (var reader = new BinaryReader(stream))
            {
                uint magic = reader.ReadUInt32();
                uint version = reader.ReadUInt32();
                uint declaredLength = reader.ReadUInt32();

                if (magic != GlbMagic || version != 2)
                    throw new InvalidDataException(
                        "Only binary glTF 2.0 (.glb) is supported.");

                if (declaredLength != bytes.Length)
                    throw new InvalidDataException(
                        "GLB declared length does not match file length.");

                string json = null;
                byte[] binary = null;

                while (stream.Position + 8 <= stream.Length)
                {
                    uint chunkLength = reader.ReadUInt32();
                    uint chunkType = reader.ReadUInt32();

                    if (chunkLength > stream.Length - stream.Position)
                        throw new InvalidDataException("Invalid GLB chunk length.");

                    byte[] chunk = reader.ReadBytes((int)chunkLength);

                    if (chunkType == JsonChunk)
                        json = Encoding.UTF8.GetString(chunk).TrimEnd('\0', ' ', '\t', '\r', '\n');
                    else if (chunkType == BinChunk)
                        binary = chunk;
                }

                if (string.IsNullOrEmpty(json))
                    throw new InvalidDataException("GLB JSON chunk is missing.");

                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;

                    // Validate the core structure before constructing objects.
                    if (!root.TryGetProperty("asset", out JsonElement asset) ||
                        !asset.TryGetProperty("version", out JsonElement assetVersion) ||
                        !assetVersion.GetString().StartsWith("2.", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Invalid glTF 2.x asset metadata.");
                    }

                    // This initial milestone validates the container and metadata.
                    // Geometry, textures, materials and animations are implemented
                    // in subsequent importer work; don't report an empty root as loaded.
                    if (!root.TryGetProperty("meshes", out JsonElement meshes) ||
                        meshes.ValueKind != JsonValueKind.Array ||
                        meshes.GetArrayLength() == 0)
                    {
                        throw new NotSupportedException(
                            "This GLB contains no meshes supported by the initial importer.");
                    }

                    if (binary == null)
                        throw new NotSupportedException(
                            "GLB has no binary chunk; geometry import is not implemented yet.");

                    throw new NotSupportedException(
                        "GLB container validation is implemented. Geometry import is the next milestone.");
                }
            }
        }
    }
}
