using System;
using System.Collections.Generic;

namespace ULTRADROID.Models
{
    internal static class ModelRegistry
    {
        private static readonly Dictionary<string, ModelInstance> Models =
            new Dictionary<string, ModelInstance>();

        private static readonly Dictionary<string, ModelOwner> Owners =
            new Dictionary<string, ModelOwner>();

        internal static void RegisterOwner(ModelOwner owner)
        {
            if (owner == null)
                return;

            Owners[owner.Id] = owner;
        }

        internal static void UnregisterOwner(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                return;

            Owners.Remove(ownerId);
        }

        internal static void Register(ModelInstance instance)
        {
            if (instance == null)
                return;

            Models[instance.Id] = instance;
        }

        internal static void Unregister(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                return;

            Models.Remove(modelId);
        }

        internal static ModelInstance GetModel(string modelId)
        {
            PruneInvalidModels();

            if (string.IsNullOrEmpty(modelId))
                return null;

            Models.TryGetValue(modelId, out ModelInstance instance);
            return instance;
        }

        internal static ModelInstance GetModel(
            string modelId,
            string ownerId)
        {
            ModelInstance instance = GetModel(modelId);

            if (instance == null ||
                !string.Equals(
                    instance.OwnerId,
                    ownerId,
                    StringComparison.Ordinal))
                return null;

            return instance;
        }

        internal static IReadOnlyList<ModelInstance> GetModels(
            string ownerId = null)
        {
            PruneInvalidModels();

            List<ModelInstance> result =
                new List<ModelInstance>();

            foreach (ModelInstance instance in Models.Values)
            {
                if (instance == null)
                    continue;

                if (ownerId != null &&
                    !string.Equals(
                        instance.OwnerId,
                        ownerId,
                        StringComparison.Ordinal))
                    continue;

                result.Add(instance);
            }

            return result;
        }

        internal static void DestroyOwnedModels(string ownerId)
        {
            if (string.IsNullOrEmpty(ownerId))
                return;

            List<ModelInstance> owned = new List<ModelInstance>();

            foreach (ModelInstance instance in Models.Values)
            {
                if (instance != null &&
                    string.Equals(
                        instance.OwnerId,
                        ownerId,
                        StringComparison.Ordinal))
                {
                    owned.Add(instance);
                }
            }

            foreach (ModelInstance instance in owned)
                instance.Destroy();
        }

        private static void PruneInvalidModels()
        {
            List<string> invalidIds = new List<string>();

            foreach (KeyValuePair<string, ModelInstance> pair in Models)
            {
                if (pair.Value == null || !pair.Value.IsValid)
                    invalidIds.Add(pair.Key);
            }

            foreach (string id in invalidIds)
            {
                Models.Remove(id);
            }
        }
    }
}
