using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Boot.UI
{
    /// <summary>
    /// APK 自带的资源服务器编辑界面，显示地址草稿并向配置存储和 Launcher 提交意图；
    /// 不连接服务器、不切换资源清单，也不依赖热更程序集或游戏 UI 框架。
    /// </summary>
    public sealed class BootResourceServerView : MonoBehaviour
    {
        [SerializeField] private Launcher _launcher;
        [SerializeField] private Button _entryButton;
        [SerializeField] private GameObject _editorPanel;
        [SerializeField] private Text _currentAddressText;
        [SerializeField] private InputField _addressInput;
        [SerializeField] private Text _feedbackText;
        [SerializeField] private Button _saveButton;
        [SerializeField] private Button _resetButton;
        [SerializeField] private Button _cancelButton;

        private bool _initialized;
        private bool _opened;
        /// <summary>界面请求代次；关闭、销毁或重新打开后，异步读取不得回写旧界面。</summary>
        private int _generation;

#if UNITY_INCLUDE_TESTS
        public static string TestPersistenceRoot { get; set; }
        public static Func<UniTask<string>> TestDefaultAddress { get; set; }
        public static void ResetTestPorts() { TestPersistenceRoot = null; TestDefaultAddress = null; }
        public void InitializeForTests() => InitializeOnce();
#endif

        private void Awake() => InitializeOnce();

        private void InitializeOnce()
        {
            if (_initialized) return;
            Require(_launcher, nameof(_launcher));
            Require(_entryButton, nameof(_entryButton));
            Require(_editorPanel, nameof(_editorPanel));
            Require(_currentAddressText, nameof(_currentAddressText));
            Require(_addressInput, nameof(_addressInput));
            Require(_feedbackText, nameof(_feedbackText));
            Require(_saveButton, nameof(_saveButton));
            Require(_resetButton, nameof(_resetButton));
            Require(_cancelButton, nameof(_cancelButton));
            _entryButton.onClick.AddListener(OpenClicked);
            _saveButton.onClick.AddListener(SaveClicked);
            _resetButton.onClick.AddListener(ResetClicked);
            _cancelButton.onClick.AddListener(CancelClicked);
            _launcher.ResourceServerAvailabilityChanged += SetAvailability;
            _editorPanel.SetActive(false);
            SetAvailability(_launcher.CanEditResourceServer);
            _initialized = true;
        }

        private void OpenClicked() => OpenEditorAsync().Forget();
        private void ResetClicked() => RestoreDefaultAsync().Forget();
        private void CancelClicked() => CloseEditor(false);

        /// <summary>打开时读取真实覆盖地址或包内默认地址；读取失败仍保留手动输入和取消入口。</summary>
        public async UniTask OpenEditorAsync()
        {
            if (_opened || !_launcher.TryBeginResourceServerEdit()) return;
            _opened = true;
            int generation = ++_generation;
            _editorPanel.SetActive(true);
            _addressInput.text = string.Empty;
            _currentAddressText.text = "当前地址：读取中";
            _feedbackText.text = "正在读取地址...";
            SetReading(true);
            try
            {
                string address = ResourceServerPreference.TryLoad(PersistenceRoot, out string saved, out _)
                    ? saved : await ReadDefaultAsync();
                if (!IsCurrent(generation)) return;
                _addressInput.text = address;
                _currentAddressText.text = $"当前地址：{address}";
                _feedbackText.text = "请输入完整 HTTP/HTTPS 地址和端口，例如 http://192.168.2.100:8000";
            }
            catch (Exception)
            {
                if (!IsCurrent(generation)) return;
                _currentAddressText.text = "当前地址：无法读取";
                _feedbackText.text = "默认地址读取失败，请手动输入资源服务器地址。";
            }
            finally
            {
                if (IsCurrent(generation)) SetReading(false);
            }
        }

        /// <summary>完整校验并原子保存后才请求重启；无效输入或写入失败保留旧地址和编辑会话。</summary>
        private void SaveClicked()
        {
            if (!_opened || !_saveButton.interactable) return;
            if (!ResourceServerPreference.TrySave(_addressInput.text, PersistenceRoot, out _, out string diagnostic))
            {
                _feedbackText.text = diagnostic == "resource-server"
                    ? "地址无效，请以 http:// 或 https:// 开头，并填写正确的服务器和端口。"
                    : "地址保存失败，请检查设备存储空间后重试。";
                return;
            }
            CloseEditor(true);
        }

        /// <summary>先确认包内默认值可用，再删除覆盖配置并重试；读取失败不删除上次有效地址。</summary>
        public async UniTask RestoreDefaultAsync()
        {
            if (!_opened || !_resetButton.interactable) return;
            int generation = _generation;
            SetReading(true);
            _feedbackText.text = "正在恢复包内默认地址...";
            try
            {
                string address = await ReadDefaultAsync();
                if (!IsCurrent(generation)) return;
                if (!ResourceServerPreference.TryNormalize(address, out _) ||
                    !ResourceServerPreference.TryReset(PersistenceRoot, out _))
                {
                    _feedbackText.text = "恢复默认地址失败，已保留当前设置。";
                    return;
                }
                CloseEditor(true);
            }
            catch (Exception)
            {
                if (IsCurrent(generation)) _feedbackText.text = "默认地址读取失败，已保留当前设置。";
            }
            finally
            {
                if (IsCurrent(generation)) SetReading(false);
            }
        }

        private static async UniTask<string> ReadDefaultAsync()
        {
#if UNITY_INCLUDE_TESTS
            if (TestDefaultAddress != null) return await TestDefaultAddress();
#endif
            return (await BootstrapConfig.LoadDefaultAsync()).ResourceServer;
        }

        private static string PersistenceRoot
        {
            get
            {
#if UNITY_INCLUDE_TESTS
                if (TestPersistenceRoot != null) return TestPersistenceRoot;
#endif
                return Application.persistentDataPath;
            }
        }

        private bool IsCurrent(int generation) => this && _opened && generation == _generation;
        /// <summary>只有资源启动失败且可编辑时显示入口；正常加载、重试和纯离线模式隐藏。</summary>
        private void SetAvailability(bool available)
        {
            _entryButton.gameObject.SetActive(available && _launcher.ShouldShowResourceServer);
            _entryButton.interactable = available;
        }
        private void SetReading(bool reading)
        {
            _addressInput.interactable = !reading;
            _saveButton.interactable = !reading;
            _resetButton.interactable = !reading;
        }

        private void CloseEditor(bool restart)
        {
            if (!_opened) return;
            _opened = false;
            ++_generation;
            _addressInput.DeactivateInputField();
            if (EventSystem.current && EventSystem.current.currentSelectedGameObject &&
                EventSystem.current.currentSelectedGameObject.transform.IsChildOf(_editorPanel.transform))
                EventSystem.current.SetSelectedGameObject(null);
            _editorPanel.SetActive(false);
            if (_launcher) _launcher.FinishResourceServerEdit(restart);
        }

        private void OnDisable() { if (_initialized) CloseEditor(false); }
        private void OnDestroy()
        {
            ++_generation;
            if (!_initialized) return;
            CloseEditor(false);
            if (_launcher) _launcher.ResourceServerAvailabilityChanged -= SetAvailability;
            _entryButton.onClick.RemoveListener(OpenClicked);
            _saveButton.onClick.RemoveListener(SaveClicked);
            _resetButton.onClick.RemoveListener(ResetClicked);
            _cancelButton.onClick.RemoveListener(CancelClicked);
        }

        private void Require(UnityEngine.Object value, string field)
        {
            if (!value) throw new InvalidOperationException($"Boot 地址界面必需引用缺失：{name}.{field}");
        }
    }
}
