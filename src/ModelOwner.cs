using System;
using System.Collections.Generic;

namespace ULTRADROID.Models
{
    public sealed class ModelOwner : IDisposable
    {
        private readonly HashSet<string> _modelIds =
            new HashSet<string>();

        private bool _disposed;

        public string Id { get; }
        public bool IsDisposed => _disposed;
        public int ModelCount => _modelIds.Count;

        internal ModelOwner(string id)
        {
            Id = id;
        }

        public ModelInstance LoadGLB(
            string path,
            ModelLoadOptions options = null)
        {
            ThrowIfDisposed();

            return ModelAPI.LoadGLB(path, options, this);
        }

        public void LoadGLBAsync(
            string path,
            Action<ModelInstance> onCompleted,
            Action<Exception> onError = null,
            ModelLoadOptions options = null)
        {
            ThrowIfDisposed();

            ModelAPI.LoadGLBAsync(
                path,
                onCompleted,
                onError,
                options,
                this);
        }

        public ModelInstance GetModel(string modelId)
        {
            if (_disposed)
                return null;

            return ModelRegistry.GetModel(modelId, Id);
        }

        public IReadOnlyList<ModelInstance> GetModels()
        {
            return ModelRegistry.GetModels(Id);
        }

        public void UnloadAll()
        {
            ModelRegistry.DestroyOwnedModels(Id);
            _modelIds.Clear();
        }

        internal void Register(ModelInstance instance)
        {
            if (instance == null)
                return;

            if (_disposed)
            {
                instance.Destroy();
                return;
            }

            _modelIds.Add(instance.Id);
        }

        internal void Unregister(string modelId)
        {
            _modelIds.Remove(modelId);
        }

        internal void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(
                    nameof(ModelOwner));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            UnloadAll();
            ModelRegistry.UnregisterOwner(Id);
        }
    }
}
