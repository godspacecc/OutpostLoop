using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>生存入口使用的外景源引用，确保编辑器与本地运行版读取同一布局和着色器。</summary>
    public sealed class RiverValleyGameplayContent : ScriptableObject
    {
        // 保留原始河谷镇图纸，不复制另一份布局数据。
        public TextAsset layout;
        // 显式引用表面着色器，使构建时保留外景所需资源。
        public Shader surface;
    }
}
