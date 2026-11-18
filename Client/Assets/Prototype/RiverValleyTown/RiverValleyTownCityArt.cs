using UnityEngine;

namespace OutpostLoop.RiverValley
{
    public sealed partial class RiverValleyTownPreview
    {
        // 旧小街区外景继续使用原表现；新整图和独立河谷预览采用已确认 City 目录。
        private RiverValleyCityArtCatalog cityArt;
        private RiverValleyCityBatch cityEnvironment;
        public int CityBuildingCount { get; private set; }
        public int CityTreeCount { get; private set; }
        public int CityVehicleCount { get; private set; }
        public int CityRoadSegmentCount { get; private set; }

        /// <summary>按原占地拼模块，不把 5 米店面整体拉伸到几十米。</summary>
        private void BuildCityBuilding(Building building, Vector2 center, Quaternion rotation)
        {
            var batch = new RiverValleyCityBatch(Section(fullSurvivalWorld ? "封闭建筑" : "City建筑"), cityArt);
            bool shop = building.style == "shop", publicBuilding = building.style == "public";
            bool generic = building.style == "work" || building.style == "farm" || building.style == "glass";
            int levels = shop || building.style == "farm" || building.style == "glass" ? 1 : publicBuilding || building.style == "work" ? 2 : 2;
            float width = building.width, depth = building.depth, height = levels * 3f;
            var origin = At(center, NaturalHeight(center) - .14f);
            Vector3 Local(float x, float y, float z) => origin + rotation * new Vector3(x, y, z);
            int bays = Mathf.Max(1, Mathf.RoundToInt(width / 5));
            for (int level = 0; level < levels; level++)
            for (int side = -1; side <= 1; side += 2)
            {
                if (!generic)
                {
                    // 前后 5 米为原件店面/住宅，中央区域由侧墙与顶面补齐。
                    float moduleDepth = Mathf.Min(depth / 2, 5f);
                    for (int bay = 0; bay < bays; bay++)
                        batch.Add(shop && level == 0 ? "shop" : "residential", Local(-width / 2 + width * (bay + .5f) / bays, level * 3 + 1.5f, side * (depth - moduleDepth) / 2),
                            new Vector3(width / bays, 3, moduleDepth), rotation * Quaternion.Euler(0, side < 0 ? 180 : 0, 0));
                }
                else
                {
                    int count = Mathf.Max(1, Mathf.CeilToInt(width / 2.5f));
                    for (int bay = 0; bay < count; bay++)
                        batch.Add(building.style == "work" ? "wall" : "window", Local(-width / 2 + width * (bay + .5f) / count, level * 3 + 1.5f, side * (depth / 2 - .13f)),
                            new Vector3(width / count, 3, .26f), rotation * Quaternion.Euler(0, side < 0 ? 180 : 0, 0));
                }
                int sideBays = Mathf.Max(1, Mathf.CeilToInt(depth / 2.5f));
                for (int bay = 0; bay < sideBays; bay++)
                    batch.Add("wall", Local(side * (width / 2 - .12f), level * 3 + 1.5f, -depth / 2 + depth * (bay + .5f) / sideBays),
                        new Vector3(depth / sideBays, 3, .24f), rotation * Quaternion.Euler(0, 90, 0));
            }
            // 屋顶采用 City 专用屋顶原件，避免把室内地板当作外露屋面。
            batch.Add("roof", Local(0,height + .12f,0), new Vector3(width,.24f,depth),rotation);
            if (publicBuilding && Vector2.Distance(center, Map(2280,1308)) < 65)
                batch.Add("hospitalSign",Local(0,2.4f,-depth/2-.08f),new Vector3(3.34f,.66f,.14f),rotation);
            batch.Finish();
            Solid("City封闭建筑",Local(0,height/2,0),new Vector3(width,height,depth),rotation,buildingColliders);
            footprints.Add(new Footprint(center,Mathf.Sqrt(width*width+depth*depth)/2));
            CityBuildingCount++;
        }
        /// <summary>沿原路面的两端高度放置短路段，河道与桥面的通行数据保持原样。</summary>
        private void AddCityRoad(Vector2 a, Vector2 b, float width, float ha, float hb)
        {
            var axis = At(b,hb) - At(a,ha);
            cityEnvironment.Add("road",(At(a,ha)+At(b,hb))/2+Vector3.up*.012f,new Vector3(width,1,axis.magnitude+.015f),Quaternion.LookRotation(axis));
            CityRoadSegmentCount++;
        }
    }
}
