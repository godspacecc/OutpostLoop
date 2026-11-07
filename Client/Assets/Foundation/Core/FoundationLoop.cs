using UnityEngine;
namespace Game
{
    /// <summary>唯一通用帧驱动，转发成熟生命周期；玩法系统自行注册并配对解绑。</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class FoundationLoop : MonoBehaviour
    {
        // 场景内重复入口不重复推进全局生命周期。
        private static FoundationLoop owner;
        private void Awake() { if(owner!=null&&owner!=this){Destroy(this);return;}owner=this; }
        private void Update() { if(owner==this)LifeCycle.Update(); }
        private void FixedUpdate() { if(owner==this)LifeCycle.FixedUpdate(); }
        private void LateUpdate() { if(owner==this)LifeCycle.LateUpdate(); }
        private void OnApplicationPause(bool paused) { if(owner==this)LifeCycle.ApplicationPause(paused); }
        private void OnApplicationFocus(bool focused) { if(owner==this)LifeCycle.ApplicationFocus(focused); }
        private void OnDestroy()
        {
            if(owner!=this)return;
            TimerMgr.Instance.ClearForTests();LifeCycle.Quit();Game.Interface.Deinit();owner=null;
        }
    }
}
