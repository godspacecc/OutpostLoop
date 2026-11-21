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
            RiverValleyShopVisuals.Create(survivalInteriors[0].Root,survivalWorldGeometry.Shop,survivalCityArt,
                point=>survivalWorldGeometry.Height(point),RegisterScavengeOccluder,palette,
                (fixture, owner) => survivalSight.AddBox(SurvivalPosition(fixture.Area.center, fixture.Height * .5f),
                    Quaternion.identity, new Vector3(fixture.Area.width, fixture.Height, fixture.Area.height), owner));
        }
    }
}
