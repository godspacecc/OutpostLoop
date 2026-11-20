using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>物品行模板引用，供控制器填充名称、数量和可用操作。</summary>
    public sealed class SurvivalInventoryRowView : MonoBehaviour
    {
        // 左右列可分别摆放图标、名称和操作按钮。
        public RawImage Icon;
        public TMP_Text Name, ItemType;
        public Button Transfer, Use, Drop;
    }
}
