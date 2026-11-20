using QFramework;
using TMPro;
using UnityEngine;

namespace Game
{
    /// <summary>将预制体上的 TMP 固定标签绑定到语言键。</summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public sealed class LocalizedTmpText : MonoBehaviour
    {
        // 标签的源文案由本地化表提供。
        [SerializeField] private string key;
        private IUnRegister subscription;
        private TextMeshProUGUI label;

        private void OnEnable()
        {
            label = GetComponent<TextMeshProUGUI>();
            subscription = Game.Interface.GetModel<LocalizationModel>().CurrentLanguageCode.Register(_ => Refresh());
            Refresh();
        }

        private void OnDisable()
        {
            subscription?.UnRegister();
            subscription = null;
        }

        private void Refresh()
        {
            if (!string.IsNullOrWhiteSpace(key)) label.text = Loc.T(key);
        }
    }
}
