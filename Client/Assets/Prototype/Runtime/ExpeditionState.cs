using System;
using System.IO;
using Game;
using QFramework;
using UnityEngine;

namespace OutpostLoop
{
    public enum ExpeditionMap { Wilderness, Industrial, Beacon }
    public enum ExpeditionTech { Fortifications, Survey, Reinforcement, Handcart, Pulley, Bracing }
    public enum ExpeditionEnd { None, Returned, Surveyed, Died, Abandoned }

    /// <summary>探索地图的任务、开放门槛与敌情。</summary>
    public readonly struct ExpeditionMapRules
    {
        public readonly string Name, Era, BasicObjective, Objective, EntryRequirement;
        public readonly int SurvivalSeconds, SurveySeconds, BasicFunds, SurveyFunds, EnemyInterval, EnemyCap;
        public readonly int StandardEnemies, FastEnemies;

        private ExpeditionMapRules(string name, string era, string basic, string objective, string requirement,
            int survivalSeconds, int surveySeconds, int basicFunds, int surveyFunds, int enemyInterval, int enemyCap,
            int standard, int fast)
        {
            Name = name; Era = era; BasicObjective = basic; Objective = objective; EntryRequirement = requirement;
            SurvivalSeconds = survivalSeconds; SurveySeconds = surveySeconds;
            BasicFunds = basicFunds; SurveyFunds = surveyFunds; EnemyInterval = enemyInterval;
            EnemyCap = enemyCap; StandardEnemies = standard; FastEnemies = fast;
        }

        public static ExpeditionMapRules For(ExpeditionMap map) => map switch
        {
            ExpeditionMap.Industrial => new ExpeditionMapRules("旧工坊", "手工修造", "找齐木轴和粗绳，拉起闸门",
                "临摹工坊图纸", "需野外测绘", 0, 5, 50, 100, 60, 24, 2, 1),
            ExpeditionMap.Beacon => new ExpeditionMapRules("山口烽台", "山口守备", "照料烽火并击退三批来敌",
                "在高台升起旗号", "需从旧工坊开闸回归", 0, 6, 80, 140, 60, 32, 3, 1),
            _ => new ExpeditionMapRules("荒野", "野外求生", "存活 4 分钟", "勘察荒野遗迹",
                "", 240, 3, 25, 50, 75, 12, 2, 0)
        };
    }

    /// <summary>本次出发的任务、研究和交互状态，局外档案在活着回归时才更新。</summary>
    public sealed class ExpeditionState
    {
        internal static readonly ExpeditionTech[] TechTree =
            { ExpeditionTech.Fortifications, ExpeditionTech.Survey, ExpeditionTech.Reinforcement,
                ExpeditionTech.Handcart, ExpeditionTech.Pulley, ExpeditionTech.Bracing };
        public ExpeditionMap Map { get; }
        public ExpeditionMapRules Rules { get; }
        public int StartingTech { get; }
        public int CompletedTech { get; internal set; }
        public int NewTech => CompletedTech & ~StartingTech;
        public int MaxHealth { get; }
        public int CommanderSight { get; }
        public Vector2 CluePosition { get; internal set; }
        public Vector2 SurveyPosition { get; internal set; }
        public bool FoundClue { get; internal set; }
        public bool BasicComplete { get; internal set; }
        public float BasicCompletedAt { get; internal set; }
        public int ThreatLevel(float elapsed) => BasicComplete ? Mathf.Min(2, Mathf.FloorToInt((elapsed - BasicCompletedAt) / 60)) : 0;
        public Vector2 WorkshopAxle { get; internal set; }
        public Vector2 WorkshopRope { get; internal set; }
        public Vector2 ObjectivePosition { get; internal set; }
        public bool HasAxle { get; internal set; }
        public bool HasRope { get; internal set; }
        public float GateProgress { get; internal set; }
        public bool BeaconLit { get; internal set; }
        public float BeaconCare { get; internal set; }
        public int BeaconWaves { get; internal set; }
        public int BeaconQueued { get; internal set; }
        public ExpeditionTech? Researching { get; internal set; }
        public float ResearchLeft { get; internal set; }
        public float SurveyProgress { get; internal set; }
        public ExpeditionEnd End { get; internal set; }
        public int Reward => End == ExpeditionEnd.Surveyed ? Rules.BasicFunds + Rules.SurveyFunds
            : End == ExpeditionEnd.Returned ? Rules.BasicFunds : 0;

        internal ExpeditionState(ExpeditionMap map, int tech, int health, int sight)
        {
            Map = map;
            Rules = ExpeditionMapRules.For(map);
            StartingTech = CompletedTech = tech;
            MaxHealth = health;
            CommanderSight = sight;
        }

        internal static int Bit(ExpeditionTech tech) => 1 << (int)tech;
        public bool Has(ExpeditionTech tech) => (CompletedTech & Bit(tech)) != 0;
        public static string TechName(ExpeditionTech tech) => tech switch
        {
            ExpeditionTech.Fortifications => "简易工事",
            ExpeditionTech.Survey => "野外测绘",
            ExpeditionTech.Reinforcement => "弩台加固",
            ExpeditionTech.Handcart => "手推车",
            ExpeditionTech.Pulley => "滑轮吊运",
            ExpeditionTech.Bracing => "斜撑木工",
            _ => ""
        };
        public static int TechCost(ExpeditionTech tech) => tech switch
        {
            ExpeditionTech.Survey or ExpeditionTech.Pulley => 30,
            ExpeditionTech.Reinforcement => 40,
            ExpeditionTech.Bracing => 25,
            _ => 20
        };
        public static int TechSeconds(ExpeditionTech tech) => tech switch
        {
            ExpeditionTech.Survey or ExpeditionTech.Pulley or ExpeditionTech.Bracing => 15,
            ExpeditionTech.Reinforcement => 20,
            _ => 10
        };
        public static string Requirement(ExpeditionTech tech) => tech switch
        {
            ExpeditionTech.Fortifications or ExpeditionTech.Handcart => "完工庇护所",
            ExpeditionTech.Survey => "简易工事与荒野线索",
            ExpeditionTech.Reinforcement => "野外测绘与旧工坊线索",
            ExpeditionTech.Pulley => "手推车与野外测绘",
            _ => "简易工事与旧工坊线索"
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
        public const int ScoutTrainingCost = 75;
        private const string FileName = "expedition-profile.json";

        [Serializable]
        private sealed class ProfileData
        {
            public int funds;
            public int tech;
            public bool trained;
            public bool scoutTrained;
            public bool workshopReturned;
            public string[] returns = Array.Empty<string>();
        }

        private readonly JsonFileStore<ProfileData> store;
        private ProfileData data;
        public int Funds => data?.funds ?? 0;
        public int Tech => data?.tech ?? 0;
        public bool Trained => data?.trained == true;
        public bool CanTrain => !Trained && Funds >= TrainingCost;
        public int MaxHealth => Trained ? 120 : 100;
        public bool ScoutTrained => data?.scoutTrained == true;
        public bool CanTrainScout => !ScoutTrained && Funds >= ScoutTrainingCost;
        public int CommanderSight => ScoutTrained ? 18 : 16;

        public ExpeditionProfile(string directory = null)
        {
            store = new JsonFileStore<ProfileData>(directory);
            var read = store.Read(FileName);
            if (read.Status == SaveFileStatus.Missing) data = new ProfileData();
            else if (read.Status == SaveFileStatus.Valid && read.Data.funds >= 0 &&
                (read.Data.tech & ~63) == 0 && read.Data.returns != null) data = read.Data;
        }

        public bool CanEnter(ExpeditionMap map) => map switch
        {
            ExpeditionMap.Wilderness => true,
            ExpeditionMap.Industrial => (Tech & ExpeditionState.Bit(ExpeditionTech.Survey)) != 0,
            ExpeditionMap.Beacon => data?.workshopReturned == true,
            _ => false
        };

        public bool Train() => CanTrain && Save(new ProfileData
        {
            funds = Funds - TrainingCost, tech = Tech, trained = true, scoutTrained = ScoutTrained,
            workshopReturned = data.workshopReturned, returns = data.returns
        });

        public bool TrainScout() => CanTrainScout && Save(new ProfileData
        {
            funds = Funds - ScoutTrainingCost, tech = Tech, trained = Trained, scoutTrained = true,
            workshopReturned = data.workshopReturned, returns = data.returns
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
                tech = Tech | expedition.CompletedTech, trained = Trained, scoutTrained = ScoutTrained, returns = history,
                workshopReturned = data.workshopReturned || expedition.Map == ExpeditionMap.Industrial
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
