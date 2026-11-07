namespace Game
{
    /// <summary>独立 UI 的固定绘制层级；玩法 HUD 位于窗口下方，既有窗口层数值保持不变。</summary>
    public enum UILayer
    {
        HUD = 0,
        Dialog = 1,
        Tips = 2,
        Top = 3,
        Popup = 4
    }

    /// <summary>固定绘制顺序；枚举身份兼容旧值，不直接用于比较层级高低。</summary>
    public static class UILayerOrder
    {
        public static int Get(UILayer layer)
        {
            switch (layer)
            {
                case UILayer.HUD: return 0;
                case UILayer.Dialog: return 1;
                case UILayer.Popup: return 2;
                case UILayer.Tips: return 3;
                case UILayer.Top: return 4;
                default: throw new System.ArgumentOutOfRangeException(nameof(layer), layer, "未知 UI 层级");
            }
        }
    }
}
