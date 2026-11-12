using QFramework;
using UnityEngine;

namespace Game
{
    public abstract class BasePageCtrl : IController
    {
        protected BaseUI BaseUI;
        private bool _initialized;
        private bool _isActive;
        private IUnRegister _destroyUnregister;
        private BoundViewDestroyWatcher _destroyWatcher;

        public bool IsInitialized => !UnityObjectState.IsMissing(BaseUI);

        public void SetUI(BaseUI ui)
        {
            if (ReferenceEquals(BaseUI, ui))
            {
                return;
            }

            ClearBinding(true);
            if (UnityObjectState.IsMissing(ui))
            {
                return;
            }

            BaseUI = ui;
            _destroyUnregister = ui.OnTriggerDestroyEvent.Register(() => OnBoundViewDestroyed(ui));
            _destroyWatcher = ui.gameObject.AddComponent<BoundViewDestroyWatcher>();
            _destroyWatcher.Bind(ui, OnBoundViewDestroyed);
        }

        protected abstract void OnInit();
        protected abstract void OnShow();
        protected abstract void OnHide();

        protected bool IsShow()
        {
            return !UnityObjectState.IsMissing(BaseUI) && _isActive;
        }

        public void Show()
        {
            if (UnityObjectState.IsMissing(BaseUI) || _isActive)
            {
                return;
            }

            if (!_initialized)
            {
                _initialized = true;
                OnInit();
            }

            BaseUI.gameObject.SetActive(true);
            _isActive = true;
            OnShow();
        }

        public void Hide()
        {
            if (UnityObjectState.IsMissing(BaseUI) || !_isActive)
            {
                return;
            }

            OnHide();
            BaseUI.gameObject.SetActive(false);
            _isActive = false;
        }

        private void ClearBinding(bool deactivateView)
        {
            var current = BaseUI;
            if (_isActive)
            {
                OnHide();
            }

            if (deactivateView && !UnityObjectState.IsMissing(current))
            {
                current.gameObject.SetActive(false);
            }

            ResetBindingState(true);
        }

        private void ResetBindingState(bool destroyWatcher)
        {
            _destroyUnregister?.UnRegister();
            _destroyUnregister = null;
            if (destroyWatcher)
            {
                DestroyWatcher();
            }
            else if (!UnityObjectState.IsMissing(_destroyWatcher))
            {
                _destroyWatcher.Release();
            }

            _destroyWatcher = null;
            BaseUI = null;
            _initialized = false;
            _isActive = false;
        }

        private void OnBoundViewDestroyed(BaseUI destroyedUI)
        {
            if (!ReferenceEquals(BaseUI, destroyedUI))
            {
                return;
            }

            if (_isActive)
            {
                OnHide();
            }

            ResetBindingState(false);
        }

        private void DestroyWatcher()
        {
            if (UnityObjectState.IsMissing(_destroyWatcher))
            {
                return;
            }

            _destroyWatcher.Release();
            if (Application.isPlaying)
            {
                Object.Destroy(_destroyWatcher);
            }
            else
            {
                Object.DestroyImmediate(_destroyWatcher);
            }
        }

        public IArchitecture GetArchitecture()
        {
            return Game.Interface;
        }

        [ExecuteAlways]
        private sealed class BoundViewDestroyWatcher : MonoBehaviour
        {
            private BaseUI target;
            private System.Action<BaseUI> onDestroyed;

            public void Bind(BaseUI ui, System.Action<BaseUI> callback)
            {
                target = ui;
                onDestroyed = callback;
            }

            public void Release()
            {
                target = null;
                onDestroyed = null;
            }

            private void OnDestroy()
            {
                onDestroyed?.Invoke(target);
            }
        }
    }
}
