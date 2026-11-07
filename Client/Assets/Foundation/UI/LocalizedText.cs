using QFramework;
using UnityEngine;
using UnityEngine.UI;

namespace Game
{
    /// <summary>将普通 UI Text 绑定语言键，语言切换后刷新并在禁用时解除订阅。</summary>
    [RequireComponent(typeof(Text))]
    public sealed class LocalizedText : MonoBehaviour
    {
        public string Key;
        private IUnRegister subscription;

        private void OnEnable()
        {
            subscription = Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Register(_ => Refresh());
            Refresh();
        }

        private void OnDisable()
        {
            subscription?.UnRegister();
            subscription = null;
        }

        public void Refresh()
        {
            if (!string.IsNullOrWhiteSpace(Key)) GetComponent<Text>().text = Loc.T(Key);
        }
    }
}
