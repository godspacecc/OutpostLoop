using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OutpostLoop
{
    /// <summary>河谷镇物品页的预制体引用，供控制器绑定局内状态。</summary>
    public sealed class SurvivalInventoryView : MonoBehaviour
    {
        // 面板及两列的位置、字形和颜色在预制体中编辑。
        public RectTransform Panel;
        public RawImage Frame;
        public TMP_Text Title, SourceHeading, Capacity, SourceStatus;
        public Button Equipment, Close, ReplaceBattery;
        public ScrollRect Source, Pack;
        public RawImage[] CapacityFills;
        public RectTransform Note;
        public GameObject EmptyPack, EmptyWeapon, EmptyTorch;
        public SurvivalInventoryRowView SourceRow, PackRow;
    }
}
