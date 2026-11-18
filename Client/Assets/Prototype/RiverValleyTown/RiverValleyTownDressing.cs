using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    public sealed partial class RiverValleyTownPreview
    {
        // 陈设只占屋顶、头顶或可跨过的低矮地表，不向领域地图添加障碍。
        private readonly List<DressingLot> dressingLots = new List<DressingLot>();
        internal readonly List<Vector2> DressingGroundSamples = new List<Vector2>();
        // 关卡评审定位各前场；不是新增搜点或交互位置。
        internal readonly Vector2[] DressingDistrictCenters = new Vector2[6];
        // 场景验收计数，不进入存档或任务规则。
        public int CityDressingFixtureCount { get; private set; }
        public int CityDressingSurfaceCount { get; private set; }
        public int CityDressingGroundCount { get; private set; }
        public int CityDistrictPadCount { get; private set; }
        private readonly struct DressingLot
        {
            internal readonly Vector2 Center, Size;
            internal readonly Quaternion Rotation;
            internal readonly string Style;
            internal DressingLot(Vector2 center, Vector2 size, Quaternion rotation, string style)
            { Center=center; Size=size; Rotation=rotation; Style=style; }
        }

        /// <summary>建筑附属设备使用真实原件比例；底边高于人头，避免产生无碰撞的地面障碍。</summary>
        private void AddCityBuildingDressing(RiverValleyCityBatch batch, Building building, Vector2 center, Quaternion rotation, float height)
        {
            dressingLots.Add(new DressingLot(center,new Vector2(building.width,building.depth),rotation,building.style));
            var origin=At(center,NaturalHeight(center)-.14f);
            Vector3 Local(float x,float y,float z)=>origin+rotation*new Vector3(x,y,z);
            int hash=Mathf.Abs(Mathf.RoundToInt(center.x*17+center.y*31));
            bool industrial=building.style=="work" || building.style=="public";
            var equipment=industrial?"roofVent":"roofAircon";
            var equipmentSize=cityArt.Find(equipment).sourceSize;
            batch.Add(equipment,Local(-building.width*.22f,height+equipmentSize.y/2+.25f,0),equipmentSize,rotation);
            CityDressingFixtureCount++;
            if(industrial)
            {
                var size=cityArt.Find("roofAircon").sourceSize*1.4f;
                batch.Add("roofAircon",Local(building.width*.2f,height+size.y/2+.25f,building.depth*.12f),size,rotation);
                CityDressingFixtureCount++;
            }
            foreach(int side in new[]{-1,1})
            {
                var facing=rotation*Quaternion.Euler(0,side<0?180:0,0);
                if(building.style=="shop")
                {
                    string key=hash%3==0?"coffeeSign":"shopSign";
                    var size=cityArt.Find(key).sourceSize*.65f;
                    batch.Add(key,Local(0,2.85f,side*(building.depth/2+size.z/2+.04f)),size,facing);
                    CityDressingFixtureCount++;
                }
                else if(industrial)
                {
                    batch.Add("warningSign",Local(0,2.5f,side*(building.depth/2+.04f)),cityArt.Find("warningSign").sourceSize,facing);
                    CityDressingFixtureCount++;
                }
                if(hash%2==0 && building.style!="glass")
                {
                    var size=cityArt.Find("aircon").sourceSize*.7f;
                    batch.Add("aircon",Local(building.width*.25f,2.65f,side*(building.depth/2+size.z/2)),size,facing);
                    CityDressingFixtureCount++;
                }
            }
        }

        /// <summary>在通行几何就绪后铺院前地坪和区域标线；房间和主路净空保持原样。</summary>
        private void BuildCityDressing()
        {
            foreach(var lot in dressingLots)
            {
                Edge nearest=default;float distance=float.PositiveInfinity;
                foreach(var road in roads) {float d=road.Distance(lot.Center);if(d<distance){distance=d;nearest=road;}}
                if(distance>70 || distance<.1f)continue;
                var toward=(lot.Center-nearest.Nearest(lot.Center)).normalized;
                var right=Flat(lot.Rotation*Vector3.right);var forward=Flat(lot.Rotation*Vector3.forward);
                float extent=Mathf.Min(lot.Size.x/2/Mathf.Max(.001f,Mathf.Abs(Vector2.Dot(toward,right))),lot.Size.y/2/Mathf.Max(.001f,Mathf.Abs(Vector2.Dot(toward,forward))));
                float room=distance-extent-nearest.Width/2-.5f;
                var doorstep=lot.Center-toward*extent;
                if(room>1.05f)
                {
                    float depth=Mathf.Min(room-.6f,4.5f),width=Mathf.Min(lot.Size.x*.7f,14);
                    var center=doorstep-toward*(depth/2+.6f);
                    TryPaintCityPatch(center,width,depth,Quaternion.LookRotation(At(toward,0)),lot.Style=="farm"?Tint("#827359"):Tint("#8b8b7e"),false);
                    AddLowGround("leaves",center+new Vector2(-toward.y,toward.x)*(width*.36f),new Vector3(1.6f,.09f,1.2f));
                }
                if(lot.Style=="house" || lot.Style=="farm")
                {
                    var edge=doorstep-toward*1.2f;
                    AddLowGround("bush",edge+new Vector2(-toward.y,toward.x)*lot.Size.x*.4f,new Vector3(1.2f,.33f,1.2f));
                }
            }
            if(!fullSurvivalWorld)return;
            // 六处重点区域使用各自既有建筑外的空地；失败的候选不强行覆盖道路或入口。
            for(int i=0;i<WorldGeometry.Interiors.Length;i++)
            {
                var area=WorldGeometry.Interiors[i];var size=i==3?new Vector2(18,10):i==4?new Vector2(16,8):new Vector2(10,6);
                var candidates=new[]{new Vector2(area.center.x,area.yMax+size.y/2+2),new Vector2(area.center.x,area.yMin-size.y/2-2),new Vector2(area.xMax+size.x/2+2,area.center.y)};
                foreach(var candidate in candidates)
                    if(TryPaintCityPatch(candidate+RiverValleyWorldGeometry.Anchor,size.x,size.y,Quaternion.identity,i==1?Tint("#8b8976"):Tint("#696d68"),i!=1))
                    {DressingDistrictCenters[i]=candidate;CityDistrictPadCount++;break;}
            }
        }

        /// <summary>地表陈设不覆盖河道、道路、建筑实体和可进入房间。</summary>
        private bool CityGroundAllowed(Vector2 point)
        {
            if(Mathf.Abs(point.x)>995 || Mathf.Abs(point.y)>995 || WaterMargin(point)<2)return false;
            foreach(var edge in roads)if(edge.Distance(point)<edge.Width/2+.22f)return false;
            if(fullSurvivalWorld)
            {
                var local=point-RiverValleyWorldGeometry.Anchor;
                if(!WorldGeometry.Walkable(local,.42f))return false;
                foreach(var area in WorldGeometry.Interiors)
                    if(new Rect(area.position-Vector2.one*.8f,area.size+Vector2.one*1.6f).Contains(local))return false;
            }
            else foreach(var footprint in footprints)if((footprint.Center-point).sqrMagnitude<(footprint.Radius+.5f)*(footprint.Radius+.5f))return false;
            return true;
        }

        /// <summary>按两米网格贴地铺装，逐点验证净空；标线是表现而非新增停车交互。</summary>
        private bool TryPaintCityPatch(Vector2 center,float width,float depth,Quaternion rotation,Color32 color,bool parking)
        {
            var right=Flat(rotation*Vector3.right);var forward=Flat(rotation*Vector3.forward);
            int nx=Mathf.Max(1,Mathf.CeilToInt(width/2)),nz=Mathf.Max(1,Mathf.CeilToInt(depth/2));
            Vector2 Point(int x,int z)=>center+right*(width*((float)x/nx-.5f))+forward*(depth*((float)z/nz-.5f));
            for(int x=0;x<=nx;x++)for(int z=0;z<=nz;z++)if(!CityGroundAllowed(Point(x,z)))return false;
            Vector3 Ground(Vector2 point)=>At(point,NaturalHeight(point)+.03f);
            for(int x=0;x<nx;x++)for(int z=0;z<nz;z++)
                scenery.Quad(Ground(Point(x,z)),Ground(Point(x,z+1)),Ground(Point(x+1,z+1)),Ground(Point(x+1,z)),color);
            if(fullSurvivalWorld)for(int x=0;x<=nx;x++)for(int z=0;z<=nz;z++)DressingGroundSamples.Add(Point(x,z)-RiverValleyWorldGeometry.Anchor);
            if(parking)
            {
                // 原包车位原件带整块道路；这里沿实际地形画细线，避免大面积覆盖或悬浮。
                int slots=Mathf.Max(1,Mathf.FloorToInt((width-1.2f)/2.5f));
                float start=-slots*2.5f/2,near=-depth/2+.6f,far=near+Mathf.Min(5.5f,depth-1.2f);
                void Stripe(Vector2 a,Vector2 b)
                {
                    var normal=new Vector2(-(b-a).y,(b-a).x).normalized*.06f;
                    Vector3 OnGround(Vector2 point)=>At(point,NaturalHeight(point)+.045f);
                    scenery.Quad(OnGround(a-normal),OnGround(a+normal),OnGround(b+normal),OnGround(b-normal),Tint("#c4c0aa"));
                }
                for(int slot=0;slot<=slots;slot++)
                {
                    float x=start+slot*2.5f;
                    Stripe(center+right*x+forward*near,center+right*x+forward*far);
                }
                Stripe(center+right*start+forward*far,center+right*(start+slots*2.5f)+forward*far);
            }
            CityDressingSurfaceCount++;
            return true;
        }

        /// <summary>落叶和低植被可跨过；不把箱桶等高物体无碰撞地摆到行走区域。</summary>
        private void AddLowGround(string key,Vector2 center,Vector3 size)
        {
            foreach(var offset in new[]{Vector2.zero,new Vector2(-size.x/2,-size.z/2),new Vector2(size.x/2,size.z/2),new Vector2(-size.x/2,size.z/2),new Vector2(size.x/2,-size.z/2)})
                if(!CityGroundAllowed(center+offset))return;
            cityEnvironment.Add(key,At(center,NaturalHeight(center)+size.y/2+.025f),size,Quaternion.identity);
            if(fullSurvivalWorld)DressingGroundSamples.Add(center-RiverValleyWorldGeometry.Anchor);
            CityDressingGroundCount++;
        }
    }
}
