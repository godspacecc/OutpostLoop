using System;
using System.IO;
using Game;
using QFramework;
using UnityEngine;

namespace OutpostLoop
{
    public enum ExpeditionMap { Wilderness, Industrial }
    public enum ExpeditionTech { Fortifications, Survey, Reinforcement }
    public enum ExpeditionEnd { None, Returned, Surveyed, Died, Abandoned }

    /// <summary>探索地图的任务、开放门槛与敌情。</summary>
    public readonly struct ExpeditionMapRules
    {
        public readonly string Name, Era, Objective;
        public readonly int Days, BasicFunds, SurveyFunds, EnemyInterval, EnemyCap;
        public readonly bool HasFastEnemy;
        public int SurvivalSeconds => Days * 240;

        private ExpeditionMapRules(string name, string era, string objective, int days, int basicFunds, int surveyFunds,
            int enemyInterval, int enemyCap, bool hasFastEnemy)
        {
            Name = name; Era = era; Objective = objective; Days = days;
            BasicFunds = basicFunds; SurveyFunds = surveyFunds; EnemyInterval = enemyInterval;
            EnemyCap = enemyCap; HasFastEnemy = hasFastEnemy;
        }

        public static ExpeditionMapRules For(ExpeditionMap map) => map == ExpeditionMap.Industrial
            ? new ExpeditionMapRules("旧工业区", "工事时代", "勘察工业遗址", 2, 50, 100, 60, 24, true)
            : new ExpeditionMapRules("荒野", "生存时代", "勘察荒野遗迹", 1, 25, 50, 75, 12, false);
    }

    /// <summary>本次出发的任务、研究和交互状态，局外档案在活着回归时才更新。</summary>
    public sealed class ExpeditionState
    {
        internal static readonly ExpeditionTech[] TechTree =
            { ExpeditionTech.Fortifications, ExpeditionTech.Survey, ExpeditionTech.Reinforcement };
        public ExpeditionMap Map { get; }
        public ExpeditionMapRules Rules { get; }
        public int StartingTech { get; }
        public int CompletedTech { get; internal set; }
        public int NewTech => CompletedTech & ~StartingTech;
        public int MaxHealth { get; }
        public Vector2 CluePosition { get; internal set; }
        public Vector2 SurveyPosition { get; internal set; }
        public bool FoundClue { get; internal set; }
        public bool BasicComplete { get; internal set; }
        public ExpeditionTech? Researching { get; internal set; }
        public float ResearchLeft { get; internal set; }
        public float SurveyProgress { get; internal set; }
        public ExpeditionEnd End { get; internal set; }
        public int Reward => End == ExpeditionEnd.Surveyed ? Rules.BasicFunds + Rules.SurveyFunds
            : End == ExpeditionEnd.Returned ? Rules.BasicFunds : 0;

        internal ExpeditionState(ExpeditionMap map, int tech, int health)
        {
            Map = map;
            Rules = ExpeditionMapRules.For(map);
            StartingTech = CompletedTech = tech;
            MaxHealth = health;
        }

        internal static int Bit(ExpeditionTech tech) => 1 << (int)tech;
        public bool Has(ExpeditionTech tech) => (CompletedTech & Bit(tech)) != 0;
        public static string TechName(ExpeditionTech tech) => tech switch
        {
            ExpeditionTech.Fortifications => "简易工事",
            ExpeditionTech.Survey => "野外测绘",
            ExpeditionTech.Reinforcement => "炮塔加固",
            _ => ""
        };
        public static int TechCost(ExpeditionTech tech) => 20 + (int)tech * 10;
        public static int TechSeconds(ExpeditionTech tech) => 10 + (int)tech * 5;
        public static string Requirement(ExpeditionTech tech) => tech switch
        {
            ExpeditionTech.Fortifications => "完工庇护所",
            ExpeditionTech.Survey => "简易工事与荒野线索",
            _ => "野外测绘与旧工业区线索"
        };

        public static string DescribeTech(int mask)
        {
            var names = new System.Collections.Generic.List<string>();
            foreach (var tech in TechTree)
                if ((mask & Bit(tech)) != 0) names.Add(TechName(tech));
            return names.Count == 0 ? "无" : string.Join("、", names);
        }
    }

    /// <summary>独立探索档案，任务资金和科技随同一个局号原子保存。</summary>
    public sealed class ExpeditionProfile : IUtility
    {
        public const int TrainingCost = 50;
        private const string FileName = "expedition-profile.json";

        [Serializable]
        private sealed class ProfileData
        {
            public int funds;
            public int tech;
            public bool trained;
            public string[] returns = Array.Empty<string>();
        }

        private readonly JsonFileStore<ProfileData> store;
        private ProfileData data;
        public int Funds => data?.funds ?? 0;
        public int Tech => data?.tech ?? 0;
        public bool Trained => data?.trained == true;
        public bool CanTrain => !Trained && Funds >= TrainingCost;
        public int MaxHealth => Trained ? 120 : 100;

        public ExpeditionProfile(string directory = null)
        {
            store = new JsonFileStore<ProfileData>(directory);
            var read = store.Read(FileName);
            if (read.Status == SaveFileStatus.Missing) data = new ProfileData();
            else if (read.Status == SaveFileStatus.Valid && read.Data.funds >= 0 &&
                (read.Data.tech & ~7) == 0 && read.Data.returns != null) data = read.Data;
        }

        public bool CanEnter(ExpeditionMap map) => map == ExpeditionMap.Wilderness ||
            map == ExpeditionMap.Industrial && (Tech & ExpeditionState.Bit(ExpeditionTech.Survey)) != 0;

        public bool Train() => CanTrain && Save(new ProfileData
        {
            funds = Funds - TrainingCost, tech = Tech, trained = true, returns = data.returns
        });

        /// <summary>即使在后续回归之后重试旧局，也不再重复领取。</summary>
        public bool RecordReturn(WorldState world)
        {
            var expedition = world?.Expedition;
            if (data == null || expedition == null || expedition.Reward <= 0) return false;
            string id = world.Run.RewardId;
            if (Array.IndexOf(data.returns, id) >= 0) return true;
            var history = new string[data.returns.Length + 1];
            data.returns.CopyTo(history, 0);
            history[history.Length - 1] = id;
            return Save(new ProfileData
            {
                funds = (int)Math.Min(int.MaxValue, (long)Funds + expedition.Reward),
                tech = Tech | expedition.CompletedTech, trained = Trained, returns = history
            });
        }

        private bool Save(ProfileData next)
        {
            try
            {
                store.Save(FileName, next);
                data = next;
                return true;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.Warning("探索档案保存失败：" + error.GetType().Name);
                return false;
            }
        }
    }
}
