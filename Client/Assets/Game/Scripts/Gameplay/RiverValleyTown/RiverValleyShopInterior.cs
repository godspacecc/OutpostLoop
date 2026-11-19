using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop.RiverValley
{
    /// <summary>版本 2 杂货店的固定陈设。渲染、碰撞和绕行共用占地，不随种子或读档变化。</summary>
    internal sealed class RiverValleyShopInterior
    {
        internal const int Version = 2;
        internal enum Kind { Shelf, Stock, Counter, Partition, ExistingBed, ExistingCabinet }
        internal readonly struct Fixture
        {
            internal readonly Kind Type;
            internal readonly Rect Area;
            internal readonly float Height;
            internal Fixture(Kind type,Rect area,float height){Type=type;Area=area;Height=height;}
        }
        // 房间、陈设与交互点均使用整图领域坐标。
        internal readonly Rect Room;
        internal readonly Fixture[] Fixtures;
        internal readonly Vector2 CheckoutTarget, CounterCrate, StockTarget, BedApproach, CabinetApproach;
        internal readonly Rect[] FloorZones;
        internal Vector2 Entry => new Vector2(Room.xMin+1.1f,Room.center.y);
        internal Vector2 RearPassage => new Vector2(Room.xMin+24.1f,Room.center.y);
        internal RiverValleyShopInterior(Rect room,Vector2 bed,Vector2 cabinet)
        {
            Room=room;
            if(room.width<31 || room.height<24)throw new System.InvalidOperationException("杂货店版本 2 需要已确认的 32×25 米房间。");
            Rect At(float x,float y,float w,float h)=>new Rect(room.xMin+x,room.yMin+y,w,h);
            Vector2 Point(float x,float y)=>new Vector2(room.xMin+x,room.yMin+y);
            var list=new List<Fixture>();
            foreach(float x in new[]{3f,7.5f,14f,20f})list.Add(new Fixture(Kind.Shelf,At(x,2.5f,1.8f,6.5f),1.8f));
            foreach(float x in new[]{5f,11.5f,18f})list.Add(new Fixture(Kind.Shelf,At(x,19f,1.8f,4.5f),1.8f));
            list.Add(new Fixture(Kind.Counter,At(3,15.2f,6,.8f),.95f));
            list.Add(new Fixture(Kind.Partition,At(24, .28f,.24f,10.62f),2.84f));
            list.Add(new Fixture(Kind.Partition,At(24,14.1f,.24f,room.height-14.38f),2.84f));
            list.Add(new Fixture(Kind.Stock,At(25.5f,4f,1.8f,4.5f),1.8f));
            list.Add(new Fixture(Kind.Stock,At(25.5f,16.5f,1.8f,4.5f),1.8f));
            list.Add(new Fixture(Kind.Stock,At(29.2f,4f,1.2f,4.5f),1.8f));
            list.Add(new Fixture(Kind.Stock,At(29.2f,16.5f,1.2f,4.5f),1.8f));
            list.Add(new Fixture(Kind.ExistingBed,new Rect(bed-new Vector2(.6f,1),new Vector2(1.2f,2)),.6f));
            list.Add(new Fixture(Kind.ExistingCabinet,new Rect(cabinet-new Vector2(.55f,.275f),new Vector2(1.1f,.55f)),1.04f));
            Fixtures=list.ToArray();
            CheckoutTarget=Point(5.5f,14.25f);CounterCrate=Point(5.5f,15.6f);
            StockTarget=Point(28.6f,12.5f);
            BedApproach=bed+Vector2.left*1.4f;CabinetApproach=cabinet+Vector2.left*1.4f;
            FloorZones=new[]{At(.4f,10.6f,23.6f,3.8f),At(2.4f,14.6f,7.2f,3.3f),At(4f,1.5f,19f,8.4f),At(3.5f,18.3f,19.5f,6f),At(24.3f,.4f,room.width-24.7f,room.height-.8f)};
        }
    }
}
