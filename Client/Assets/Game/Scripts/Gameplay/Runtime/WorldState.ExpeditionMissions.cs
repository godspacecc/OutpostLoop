using System.Collections.Generic;
using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class WorldState
    {
        // 专属来敌排队入场，容量释放时优先于常规巡袭。
        private readonly Queue<(EnemyKind Kind, int Wave, int Lane)> beaconRaids =
            new Queue<(EnemyKind Kind, int Wave, int Lane)>();
        private bool interactionPressed;

        /// <summary>离散交互和持续按住分开，长按不会连续拾取或重复点火。</summary>
        public void PressInteraction()
        {
            if (Expedition != null && Active) interactionPressed = true;
        }

        public string MissionStatus
        {
            get
            {
                if (Expedition == null) return "";
                if (Expedition.BasicComplete) return "基础任务完成 · 可以回归";
                switch (Expedition.Map)
                {
                    case ExpeditionMap.Industrial:
                        return $"开闸 · 木轴 {(Expedition.HasAxle ? "已取" : "未取")} / 粗绳 {(Expedition.HasRope ? "已取" : "未取")}";
                    case ExpeditionMap.Beacon:
                        if (!Expedition.BeaconLit) return "前往烽台点火";
                        return $"照料 {Mathf.FloorToInt(Expedition.BeaconCare)}/90 秒 · 来敌 {RemainingRaiders()}";
                    default:
                        int left = Mathf.CeilToInt(Mathf.Max(0, Expedition.Rules.SurvivalSeconds - Run.Elapsed));
                        return $"存活 1 天 · 剩余 {left / 60}:{left % 60:00}";
                }
            }
        }

        private string MissionInteractionHint()
        {
            if (Expedition.Map == ExpeditionMap.Industrial && !Expedition.BasicComplete)
            {
                if (!Expedition.HasAxle && CanReach(Expedition.WorkshopAxle)) return "E 取木轴";
                if (!Expedition.HasRope && CanReach(Expedition.WorkshopRope)) return "E 取粗绳";
                if (CanReach(Expedition.ObjectivePosition)) return Expedition.HasAxle && Expedition.HasRope
                    ? $"按住 E 拉起闸门 {Expedition.GateProgress:0.0}/4 秒" : "绞盘需要木轴和粗绳";
            }
            if (Expedition.Map == ExpeditionMap.Beacon && !Expedition.BeaconLit && CanReach(Expedition.ObjectivePosition))
                return "E 点起烽火";
            if (!CanReach(Expedition.SurveyPosition)) return "";
            return Expedition.BasicComplete
                ? $"E {Expedition.Rules.Objective} {Expedition.SurveyProgress:0.0}/{Expedition.Rules.SurveySeconds}"
                : "终极目标需先完成基础任务";
        }

        private void StepMissionPress()
        {
            if (Expedition.Map == ExpeditionMap.Industrial && !Expedition.BasicComplete)
            {
                if (CanReach(Expedition.WorkshopAxle)) Expedition.HasAxle = true;
                if (CanReach(Expedition.WorkshopRope)) Expedition.HasRope = true;
            }
            if (Expedition.Map == ExpeditionMap.Beacon && !Expedition.BeaconLit && CanReach(Expedition.ObjectivePosition))
            {
                Expedition.BeaconLit = true;
                QueueBeaconRaid(3, 0, 0);
                SpawnBeaconRaiders();
            }
        }

        private bool StepMissionHold(float dt)
        {
            if (Expedition.BasicComplete && CanReach(Expedition.SurveyPosition))
            {
                gatheringNode = null;
                miningWork = 0;
                Expedition.SurveyProgress += dt;
                if (Expedition.SurveyProgress + .00001f >= Expedition.Rules.SurveySeconds)
                    FinishExpedition(ExpeditionEnd.Surveyed);
                return true;
            }
            Expedition.SurveyProgress = 0;
            if (Expedition.Map == ExpeditionMap.Industrial && !Expedition.BasicComplete &&
                Expedition.HasAxle && Expedition.HasRope && CanReach(Expedition.ObjectivePosition))
            {
                gatheringNode = null;
                miningWork = 0;
                Expedition.GateProgress += dt;
                if (Expedition.GateProgress + .00001f >= 4)
                {
                    Expedition.HasAxle = Expedition.HasRope = false;
                    Expedition.GateProgress = 0;
                    CompleteBasicMission();
                    RefreshNavigation();
                }
                return true;
            }
            Expedition.GateProgress = 0;
            return false;
        }

        /// <summary>战斗先于任务更新，保证同一步死亡时没有可保存的回归成果。</summary>
        private void StepMission(float dt)
        {
            if (Expedition.BasicComplete) return;
            if (Expedition.Map == ExpeditionMap.Wilderness && Run.Elapsed >= Expedition.Rules.SurvivalSeconds)
                CompleteBasicMission();
            if (Expedition.Map != ExpeditionMap.Beacon || !Expedition.BeaconLit) return;
            if ((CommanderPosition - Expedition.ObjectivePosition).sqrMagnitude <= 64)
                Expedition.BeaconCare = Mathf.Min(90, Expedition.BeaconCare + dt);
            if (Expedition.BeaconWaves == 1 && Expedition.BeaconCare + .001f >= 30) QueueBeaconRaid(4, 2, 0);
            if (Expedition.BeaconWaves == 2 && Expedition.BeaconCare + .001f >= 60) QueueBeaconRaid(5, 2, 1);
            if (Expedition.BeaconCare + .001f >= 90 && RemainingRaiders() == 0) CompleteBasicMission();
        }

        private void CompleteBasicMission()
        {
            Expedition.BasicComplete = true;
            Expedition.BasicCompletedAt = Run.Elapsed;
        }

        private void QueueBeaconRaid(int standard, int fast, int siege)
        {
            int wave = ++Expedition.BeaconWaves;
            int lane = (wave - 1) % 2;
            int count = standard + fast + siege;
            for (int index = 0; index < count; index++)
            {
                var kind = index < standard ? EnemyKind.Standard
                    : index < standard + fast ? EnemyKind.Fast : EnemyKind.Siege;
                beaconRaids.Enqueue((kind, wave, wave > 1 && index == count - 1 ? 1 - lane : lane));
            }
            Expedition.BeaconQueued = beaconRaids.Count;
        }

        private void SpawnBeaconRaiders()
        {
            while (beaconRaids.Count > 0 && Enemies.Count < Expedition.Rules.EnemyCap)
            {
                var raid = beaconRaids.Dequeue();
                AddExpeditionEnemy(raid.Kind, BattleGrid.Center(beaconEntrances[raid.Lane]), raid.Wave,
                    int.MaxValue - raid.Wave * 2 - raid.Lane, true);
            }
            Expedition.BeaconQueued = beaconRaids.Count;
        }

        private int RemainingRaiders()
        {
            int remaining = beaconRaids.Count;
            foreach (var enemy in Enemies)
                if (enemy.IsBeaconRaider && enemy.Health > 0) remaining++;
            return remaining;
        }

        private void AddExpeditionEnemy(EnemyKind kind, Vector2 position, int wave, int group, bool beacon)
        {
            Enemies.Add(new EnemyState(kind, wave, group)
            {
                Position = position,
                Health = kind switch
                {
                    EnemyKind.Fast => 12,
                    EnemyKind.Siege => 36 + 4 * wave,
                    _ => Rules.EnemyHealthBase
                },
                IsBeaconRaider = beacon
            });
        }
    }
}
