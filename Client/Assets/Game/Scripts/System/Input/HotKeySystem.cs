using System;
using System.Collections.Generic;
using System.Linq;
using QFramework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace Game
{
    /// <summary>
    /// 表示热键重绑操作结果，成功返回绑定路径，失败返回可记录的中文原因。
    /// </summary>
    public readonly struct HotKeyRebindResult
    {
        public HotKeyRebindResult(bool success, string error, bool needsConfirmation, string conflictingActionId)
        {
            Success = success;
            Error = error ?? string.Empty;
            NeedsConfirmation = needsConfirmation;
            ConflictingActionId = conflictingActionId ?? string.Empty;
        }

        public bool Success { get; }
        public string Error { get; }
        public bool NeedsConfirmation { get; }
        public string ConflictingActionId { get; }

        public static HotKeyRebindResult Ok()
        {
            return new HotKeyRebindResult(true, string.Empty, false, string.Empty);
        }

        public static HotKeyRebindResult Fail(string error)
        {
            return new HotKeyRebindResult(false, error, false, string.Empty);
        }

        public static HotKeyRebindResult Conflict(string actionId)
        {
            return new HotKeyRebindResult(false, string.Empty, true, actionId);
        }
    }

    /// <summary>
    /// 交互式重绑捕获到新路径后的通知，供设置界面刷新并触发保存。
    /// </summary>
    public readonly struct HotKeyRebindCapturedEvent
    {
        public HotKeyRebindCapturedEvent(HotKeyRebindResult result)
        {
            Result = result;
        }

        public HotKeyRebindResult Result { get; }
    }

    /// <summary>
    /// 维护动作仲裁和交互式重绑；具体动作回调由项目通过 HotKeyActionRegistry 注册。
    /// </summary>
    public sealed class HotKeySystem : AbstractSystem
    {
        /// <summary>按绑定路径复用输入动作，避免同键重复采集。</summary>
        private readonly Dictionary<string, InputAction> actionsByPath = new Dictionary<string, InputAction>();
        /// <summary>正在捕获或等待冲突确认的绑定快照。</summary>
        private HotKeyBinding pendingBinding;
        /// <summary>是否处于交互重绑状态。</summary>
        private bool isCapturing;

        protected override void OnInit()
        {
            RebuildActions();
            LifeCycle.RegisterUpdate(Update);
        }

        protected override void OnDeinit()
        {
            LifeCycle.UnregisterUpdate(Update);
            foreach (var action in actionsByPath.Values)
            {
                action.Disable();
                action.Dispose();
            }

            actionsByPath.Clear();
            CancelRebind();
        }

        /// <summary>释放旧输入动作并按当前绑定重新创建。</summary>
        public void RebuildActions()
        {
            foreach (var action in actionsByPath.Values)
            {
                action.Disable();
                action.Dispose();
            }

            actionsByPath.Clear();
            foreach (var path in this.GetModel<HotKeyModel>().Bindings.Select(item => item.BindingPath).Distinct())
            {
                var inputAction = new InputAction(
                    $"HotKey:{path}",
                    InputActionType.Button,
                    binding: path);
                inputAction.Enable();
                actionsByPath[path] = inputAction;
            }
        }

        public bool IsTextInputFocused()
        {
            return EventSystem.current != null &&
                   EventSystem.current.currentSelectedGameObject != null &&
                   EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>() != null;
        }

        /// <summary>由拥有模态顺序的玩法读取当前绑定，沿用统一的键盘输入动作。</summary>
        internal bool WasPressedThisFrame(string actionId) =>
            !isCapturing && !IsTextInputFocused() &&
            this.GetModel<HotKeyModel>().TryGet(actionId, out var binding) &&
            actionsByPath.TryGetValue(binding.BindingPath, out var action) &&
            (action.WasPressedThisFrame() || WasPressedInEditor(binding.BindingPath));

        /// <summary>编辑器 Game 视图有时只向旧输入系统转发键盘事件，按同一绑定读取一次。</summary>
        private static bool WasPressedInEditor(string bindingPath)
        {
#if UNITY_EDITOR && ENABLE_LEGACY_INPUT_MANAGER
            const string keyboardPrefix = "<Keyboard>/";
            if (!bindingPath.StartsWith(keyboardPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            string keyName = bindingPath.Substring(keyboardPrefix.Length).ToLowerInvariant();
            if (keyName.Length == 1 && keyName[0] >= '0' && keyName[0] <= '9')
                keyName = "Alpha" + keyName;
            else if (keyName.StartsWith("numpad", StringComparison.Ordinal))
                keyName = "Keypad" + keyName.Substring("numpad".Length);
            else
                keyName = keyName switch
                {
                    "enter" => nameof(KeyCode.Return),
                    "leftctrl" => nameof(KeyCode.LeftControl),
                    "rightctrl" => nameof(KeyCode.RightControl),
                    "contextmenu" => nameof(KeyCode.Menu),
                    _ => keyName
                };
            return Enum.TryParse<KeyCode>(keyName, true, out var key) && Input.GetKeyDown(key);
#else
            return false;
#endif
        }

        public bool IsCapturingRebind => isCapturing;
        public string CapturingActionId => pendingBinding?.ActionId;

        /// <summary>复制指定动作的绑定，进入键位捕获。</summary>
        public HotKeyRebindResult BeginRebind(string actionId)
        {
            if (!this.GetModel<HotKeyModel>().TryGet(actionId, out var binding))
            {
                return HotKeyRebindResult.Fail("未知热键动作");
            }

            pendingBinding = binding.Clone();
            isCapturing = true;
            return HotKeyRebindResult.Ok();
        }

        /// <summary>接收候选键位，处理取消与同上下文冲突。</summary>
        public HotKeyRebindResult ReceiveBindingPath(string bindingPath)
        {
            if (!isCapturing || pendingBinding == null)
            {
                return HotKeyRebindResult.Fail("热键捕获未开始");
            }

            if (string.IsNullOrWhiteSpace(bindingPath))
            {
                return HotKeyRebindResult.Fail("热键绑定路径为空");
            }

            if (bindingPath == HotKeyDefaults.Escape)
            {
                CancelRebind();
                return HotKeyRebindResult.Fail("热键捕获已取消");
            }

            pendingBinding.BindingPath = bindingPath.Trim();
            if (this.GetModel<HotKeyModel>().TryGetConflict(pendingBinding, out var conflict))
            {
                return HotKeyRebindResult.Conflict(conflict.ActionId);
            }

            return CommitPendingBinding(false);
        }

        public HotKeyRebindResult ConfirmPendingBinding(bool replaceConflict)
        {
            if (!isCapturing || pendingBinding == null)
            {
                return HotKeyRebindResult.Fail("热键捕获未开始");
            }

            return CommitPendingBinding(replaceConflict);
        }

        /// <summary>清空捕获状态，不改变已提交绑定。</summary>
        public void CancelRebind()
        {
            pendingBinding = null;
            isCapturing = false;
        }

        private void Update()
        {
            if (IsTextInputFocused())
            {
                return;
            }

            if (isCapturing)
            {
                CapturePressedKey();
                return;
            }

            foreach (var pair in actionsByPath)
            {
                if (!pair.Value.WasPressedThisFrame())
                {
                    continue;
                }

                ExecuteBinding(pair.Key);
            }
        }

        private void CapturePressedKey()
        {
            var keyboard = global::UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            foreach (KeyControl key in keyboard.allKeys)
            {
                if (!key.wasPressedThisFrame)
                {
                    continue;
                }

                var result = ReceiveBindingPath($"<Keyboard>/{key.name}");
                this.SendEvent(new HotKeyRebindCapturedEvent(result));
                return;
            }
        }

        private HotKeyRebindResult CommitPendingBinding(bool replaceConflict)
        {
            var model = this.GetModel<HotKeyModel>();
            if (!model.TryReplace(pendingBinding, replaceConflict))
            {
                if (model.TryGetConflict(pendingBinding, out var conflict))
                {
                    return HotKeyRebindResult.Conflict(conflict.ActionId);
                }

                return HotKeyRebindResult.Fail("替换热键绑定失败");
            }

            this.GetSystem<SettingsSystem>().MarkDirty();
            RebuildActions();
            CancelRebind();
            return HotKeyRebindResult.Ok();
        }

        /// <summary>按绑定查找已注册动作，由注册表负责优先级仲裁。</summary>
        public bool ExecuteBinding(string bindingPath)
        {
            var candidates = this.GetModel<HotKeyModel>().Bindings
                .Where(item => item.BindingPath == bindingPath)
                .Select(item => item.ActionId);
            return this.GetUtility<HotKeyActionRegistry>().TryExecute(candidates);
        }

    }
}
