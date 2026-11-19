using UnityEngine;
using OutpostLoop.RiverValley;

namespace OutpostLoop
{
    public sealed partial class PrototypeController
    {
        /// <summary>使用已确认 City 货架和柜台，分区与碰撞均由同一布局提供。</summary>
        private void CreateMainStreetShopInterior()
        {
            var colors=new[]{new Color(.57f,.6f,.58f),new Color(.66f,.61f,.49f),new Color(.58f,.51f,.43f),new Color(.58f,.51f,.43f),new Color(.49f,.54f,.56f)};
            var palette=new Material[colors.Length];
            for(int i=0;i<palette.Length;i++)
            {
                palette[i]=new Material(Shader.Find("Standard")){color=colors[i]};
                scavengeMaterials.Add(palette[i]);
            }
            RiverValleyShopVisuals.Create(scavengeRoot,survivalWorldGeometry.Shop,survivalCityArt,
                point=>survivalWorldGeometry.Height(point),RegisterScavengeOccluder,palette);
        }
        /// <summary>只在店内给出区域名称，不新增任务、可搜索容器或操作。</summary>
        private void DrawMainStreetShopZones()
        {
            var shop=survivalWorldGeometry?.Shop;
            if(shop==null || !shop.Room.Contains(scavengeRun.Player))return;
            var points=new[]{shop.CheckoutTarget,shop.Room.position+new Vector2(15,6),shop.StockTarget};
            var names=new[]{"收银区","货架区","后仓 · 休息与储物"};
            for(int i=0;i<points.Length;i++)
            {
                if(Vector2.Distance(points[i],scavengeRun.Player)>15)continue;
                var screen=gameCamera.WorldToScreenPoint(SurvivalPosition(points[i],3f));
                if(screen.z<=0)continue;
                var p=new Vector2(screen.x,Screen.height-screen.y)/HudScale;
                if(p.y<180 || p.y>Screen.height/HudScale-140)continue;
                GUI.Box(new Rect(p.x-85,p.y-12,170,25),names[i]);
            }
        }
    }
}
