using UnityEngine;
using UnityEngine.UI;
namespace Boot.UI
{
    /// <summary>独立临时启动预览字体；不复制参考项目美术，正式视觉待确认。</summary>
    [DefaultExecutionOrder(-1200)]
    public sealed class TemporaryBootFont : MonoBehaviour
    {
        private Font font;
        private void Awake(){font=Font.CreateDynamicFontFromOSFont("Microsoft YaHei",18);foreach(var text in GetComponentsInChildren<Text>(true))text.font=font;}
        private void OnDestroy(){if(font!=null)Destroy(font);}
    }
}
