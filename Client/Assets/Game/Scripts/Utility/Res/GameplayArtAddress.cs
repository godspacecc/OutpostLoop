using System;

namespace Game
{
    /// <summary>正式玩法通过 YooAsset 加载的美术入口，网格和贴图由预制体或目录资产引用。</summary>
    public static class GameplayArtAddress
    {
        public const string Player = "Art/Player/PrototypePlayer";
        public const string BattleFog = "Art/Battle/BattleFogPreview";
        public const string RiverValley = "Art/RiverValley/RiverValleyGameplayContent";
        public const string CityCatalog = "Art/CityTown/RiverValleyCityArt";
        public const string Space = "Art/Cosmic/Space";
        public const string Crossing = "Art/Cosmic/Crossing";
        public const string Infected = "Art/RiverValleyInfected/Content/";
        private const string InfectedAnimationFolder = "Assets/ThirdParty/Pxltiger/Zombie Draft/Animations/";

        public static readonly string[] Required =
        {
            Player, BattleFog, RiverValley, CityCatalog, Space, Crossing, Infected + "InfectedMatte"
        };

        public static readonly string[] InfectedOptional =
        {
            Infected + "Zombie2", Infected + "Zombie3", Infected + "Z_Idle",
            Infected + "Z_Walk_InPlace", Infected + "Z_Run_InPlace"
        };

        public static bool IsEntry(string address) =>
            Array.IndexOf(Required, address) >= 0 || Array.IndexOf(InfectedOptional, address) >= 0;

        /// <summary>感染者原始动画可移动目录，编辑器和 YooAsset 继续使用原逻辑地址。</summary>
        public static string InfectedAnimationAssetPath(string address) => address switch
        {
            Infected + "Z_Idle" => InfectedAnimationFolder + "Z_Idle.anim",
            Infected + "Z_Walk_InPlace" => InfectedAnimationFolder + "Z_Walk_InPlace.anim",
            Infected + "Z_Run_InPlace" => InfectedAnimationFolder + "Z_Run_InPlace.anim",
            _ => null
        };
    }
}
