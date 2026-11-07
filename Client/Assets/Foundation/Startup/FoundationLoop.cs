using UnityEngine;
namespace Game
{
    /// <summary>唯一场景基础入口，先初始化通用服务，再转发帧事件；拥有者离开时统一收尾。</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class FoundationLoop : MonoBehaviour
    {
        // 重复入口不推进帧、不清理主入口的会话。
        private static FoundationLoop owner;
        /// <summary>Main 的独立基础根跨场景保留；普通测试入口仍可按对象生命周期退出。</summary>
        [SerializeField] private bool persistAcrossScenes;
        /// <summary>进入新 Play 时兼容禁用域重载的编辑器设置，不携带上一会话静态状态。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            FoundationRuntime.Shutdown();
            owner = null;
        }
        private void Awake()
        {
            if (owner != null && owner != this)
            {
                if (Application.isPlaying) { if (persistAcrossScenes) Destroy(gameObject); else Destroy(this); } else DestroyImmediate(this);
                return;
            }
            FoundationRuntime.Initialize();
            owner = this;
            if (persistAcrossScenes && Application.isPlaying) DontDestroyOnLoad(gameObject);
        }
        private void Update() { if (owner == this) LifeCycle.Update(); }
        private void FixedUpdate() { if (owner == this) LifeCycle.FixedUpdate(); }
        private void LateUpdate() { if (owner == this) LifeCycle.LateUpdate(); }
        private void OnApplicationPause(bool paused) { if (owner == this) LifeCycle.ApplicationPause(paused); }
        private void OnApplicationFocus(bool focused) { if (owner == this) LifeCycle.ApplicationFocus(focused); }
        private void OnApplicationQuit() { Close(); }
        private void OnDestroy() { Close(); }
        /// <summary>只由会话拥有者关闭基础，避免重复对象销毁时中断当前场景。</summary>
        private void Close()
        {
            if (owner != this) return;
            owner = null;
            FoundationRuntime.Shutdown();
        }
    }
}
