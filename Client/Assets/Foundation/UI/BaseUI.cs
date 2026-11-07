using QFramework;
using UnityEngine;

namespace Game
{
    public class BaseUI : MonoBehaviour
    {
        public readonly EasyEvent OnTriggerDestroyEvent = new EasyEvent();

        protected virtual void Awake()
        {
            // 基础视图不注入主题；视觉样式由新项目确认后的资源持有。
        }

        private void OnDestroy()
        {
            OnTriggerDestroyEvent.Trigger();
        }
    }
}
