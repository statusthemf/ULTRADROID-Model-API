using System;
using System.Collections;
using System.Threading.Tasks;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ULTRADROID.Models
{
    public sealed class ModelLoadRunner : MonoBehaviour
    {
        private static bool _registered;

        private Task<GlbImporter.PreparedGlb> _prepareTask;
        private GlbImporter.PreparedGlb _prepared;
        private IEnumerator _routine;
        private Action<ModelInstance> _onCompleted;
        private Action<Exception> _onError;
        private ModelInstance _loadedModel;

        public ModelLoadRunner(IntPtr ptr)
            : base(ptr)
        {
        }

        internal static void Register()
        {
            if (_registered)
                return;

            ClassInjector.RegisterTypeInIl2Cpp<ModelLoadRunner>();
            _registered = true;
        }

        internal static void Begin(
            string path,
            ModelLoadOptions options,
            Action<ModelInstance> onCompleted,
            Action<Exception> onError)
        {
            Register();

            GameObject host = new GameObject(
                "ULTRADROID_ModelLoadRunner");

            UnityEngine.Object.DontDestroyOnLoad(host);

            ModelLoadRunner runner =
                host.AddComponent<ModelLoadRunner>();

            runner.Initialize(
                path,
                options,
                onCompleted,
                onError);
        }

        private void Initialize(
            string path,
            ModelLoadOptions options,
            Action<ModelInstance> onCompleted,
            Action<Exception> onError)
        {
            _onCompleted = onCompleted;
            _onError = onError;

            _prepareTask = Task.Run(
                () => GlbImporter.Prepare(path, options));
        }

        private void Update()
        {
            if (_routine == null)
            {
                if (_prepareTask == null || !_prepareTask.IsCompleted)
                    return;

                Task<GlbImporter.PreparedGlb> completedTask = _prepareTask;
                _prepareTask = null;

                if (completedTask.IsCanceled)
                {
                    Fail(new OperationCanceledException(
                        "GLB preparation was canceled."));
                    return;
                }

                if (completedTask.IsFaulted)
                {
                    Fail(completedTask.Exception?.GetBaseException() ??
                        new InvalidOperationException(
                            "GLB preparation failed."));
                    return;
                }

                try
                {
                    _prepared = completedTask.GetAwaiter().GetResult();
                    _routine = GlbImporter.LoadIncremental(
                        _prepared,
                        instance => _loadedModel = instance);
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }

                return;
            }

            bool hasNext;

            try
            {
                // Advance exactly one loading step per frame.
                hasNext = _routine.MoveNext();
            }
            catch (Exception exception)
            {
                Fail(exception);
                return;
            }

            if (hasNext)
                return;

            Complete();
        }

        private void Complete()
        {
            DisposeRoutine();
            DisposePrepared();

            ModelInstance model = _loadedModel;
            Action<ModelInstance> callback = _onCompleted;

            ClearCallbacks();
            UnityEngine.Object.Destroy(gameObject);

            callback?.Invoke(model);
        }

        private void Fail(Exception exception)
        {
            DisposeRoutine();
            DisposePrepared();

            Action<Exception> callback = _onError;
            ClearCallbacks();

            if (callback != null)
            {
                callback(exception);
            }
            else
            {
                Debug.LogError(
                    "Asynchronous GLB loading failed: " + exception);
            }

            UnityEngine.Object.Destroy(gameObject);
        }

        private void DisposeRoutine()
        {
            if (_routine == null)
                return;

            try
            {
                (_routine as IDisposable)?.Dispose();
            }
            catch (Exception exception)
            {
                Debug.LogError(
                    "Error disposing GLB loading routine: " + exception);
            }

            _routine = null;
        }

        private void DisposePrepared()
        {
            _prepared?.Dispose();
            _prepared = null;
        }

        private void ClearCallbacks()
        {
            _onCompleted = null;
            _onError = null;
            _loadedModel = null;
        }

        private void OnDestroy()
        {
            DisposeRoutine();
            DisposePrepared();
            ClearCallbacks();

            Task<GlbImporter.PreparedGlb> pending = _prepareTask;
            _prepareTask = null;

            if (pending == null)
                return;

            if (pending.Status == TaskStatus.RanToCompletion)
            {
                pending.Result.Dispose();
            }
            else if (!pending.IsCompleted)
            {
                pending.ContinueWith(
                    task =>
                    {
                        if (task.Status == TaskStatus.RanToCompletion)
                            task.Result.Dispose();
                    },
                    TaskScheduler.Default);
            }
        }
    }
}
