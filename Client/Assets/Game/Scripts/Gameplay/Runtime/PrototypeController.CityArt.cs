using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        // City 仅属于表现层；旧档地图、门状态与容器数据不持有美术引用。
        private RiverValleyCityArtCatalog survivalCityArt;
        /// <summary>保留现有交互对象、包围尺寸与 Renderer，避免开门和遮挡逻辑串到资源包实现。</summary>
        private void ApplyRiverValleyCityVisual(Transform target, string name, Vector3 size)
        {
            if (!survivalMode || survivalLayoutVersion < 1 || survivalCityArt == null) return;
            if (name == "现有外墙" || name == "重点建筑入口墙" || name == "住宅二楼外墙")
            {
                survivalCityArt.ReplaceWall(target,size.z > size.x);
                return;
            }
            string key = name.StartsWith("可搜箱 ") ? "crate" :
                name == "房间地面" || name == "重点建筑室内地面" || name == "住宅二楼楼板" ? "floor" :
                name == "屋顶" || name == "重点建筑屋顶" ? "roof" :
                name == "现有房门" ? "door" : name == "储物柜" || name == "仓库货架" ? "shelf" :
                name == "室内服务设施" ? "desk" : name == "医院标志" ? "hospitalSign" : null;
            if (key != null) survivalCityArt.Replace(target,key);
        }
    }
}
