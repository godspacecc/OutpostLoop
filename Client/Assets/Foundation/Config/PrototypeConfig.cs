using System;
using System.Collections.Generic;
using Luban;
namespace OutpostLoop.Config
{
    /// <summary>独立原型配置快照；通过调用方提供的字节加载器读取，不持有资源句柄。</summary>
    public sealed class PrototypeConfig
    {
        /// <summary>原型规则参数：Duration；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float Duration { get; }
        /// <summary>原型规则参数：InitialHealth；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int InitialHealth { get; }
        /// <summary>原型规则参数：CapacityBase；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int CapacityBase { get; }
        /// <summary>原型规则参数：CapacityPerLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int CapacityPerLevel { get; }
        /// <summary>原型规则参数：ScrapCostBase；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int ScrapCostBase { get; }
        /// <summary>原型规则参数：ScrapCostPerLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int ScrapCostPerLevel { get; }
        /// <summary>原型规则参数：EnergyCostPerLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int EnergyCostPerLevel { get; }
        /// <summary>原型规则参数：MaxLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int MaxLevel { get; }
        /// <summary>原型规则参数：HealthPerLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int HealthPerLevel { get; }
        /// <summary>原型规则参数：UpgradeRepair；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int UpgradeRepair { get; }
        /// <summary>原型规则参数：ResourceNodeCount；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int ResourceNodeCount { get; }
        /// <summary>原型规则参数：NearNodeCount；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int NearNodeCount { get; }
        /// <summary>原型规则参数：NearRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float NearRadius { get; }
        /// <summary>原型规则参数：NearRadiusStep；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float NearRadiusStep { get; }
        /// <summary>原型规则参数：FarRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float FarRadius { get; }
        /// <summary>原型规则参数：FarRadiusRange；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float FarRadiusRange { get; }
        /// <summary>原型规则参数：EnergyEvery；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int EnergyEvery { get; }
        /// <summary>原型规则参数：EnergyAmount；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int EnergyAmount { get; }
        /// <summary>原型规则参数：ScrapAmount；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int ScrapAmount { get; }
        /// <summary>原型规则参数：MaxStep；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float MaxStep { get; }
        /// <summary>原型规则参数：MoveSpeed；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float MoveSpeed { get; }
        /// <summary>原型规则参数：MapRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float MapRadius { get; }
        /// <summary>原型规则参数：DepositRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float DepositRadius { get; }
        /// <summary>原型规则参数：GatherInterval；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float GatherInterval { get; }
        /// <summary>原型规则参数：GatherLevelFactor；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float GatherLevelFactor { get; }
        /// <summary>原型规则参数：GatherRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float GatherRadius { get; }
        /// <summary>原型规则参数：MaxEnemies；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int MaxEnemies { get; }
        /// <summary>原型规则参数：WaveCountBase；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int WaveCountBase { get; }
        /// <summary>原型规则参数：WaveCountStep；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int WaveCountStep { get; }
        /// <summary>原型规则参数：SpawnSpread；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float SpawnSpread { get; }
        /// <summary>原型规则参数：SpawnRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float SpawnRadius { get; }
        /// <summary>原型规则参数：SpawnRadiusRange；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float SpawnRadiusRange { get; }
        /// <summary>原型规则参数：EnemyHealthBase；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float EnemyHealthBase { get; }
        /// <summary>原型规则参数：EnemyHealthStep；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float EnemyHealthStep { get; }
        /// <summary>原型规则参数：ShotInterval；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float ShotInterval { get; }
        /// <summary>原型规则参数：ShotRange；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float ShotRange { get; }
        /// <summary>原型规则参数：ShotRangePerLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float ShotRangePerLevel { get; }
        /// <summary>原型规则参数：ShotDamage；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float ShotDamage { get; }
        /// <summary>原型规则参数：ShotDamagePerLevel；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float ShotDamagePerLevel { get; }
        /// <summary>原型规则参数：EnemySpeed；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float EnemySpeed { get; }
        /// <summary>原型规则参数：EnemySpeedPerWave；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float EnemySpeedPerWave { get; }
        /// <summary>原型规则参数：AttackRadius；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float AttackRadius { get; }
        /// <summary>原型规则参数：AttackInterval；权威数值在 Config/DataCsv/rules.csv。</summary>
        public float AttackInterval { get; }
        /// <summary>原型规则参数：AttackDamage；权威数值在 Config/DataCsv/rules.csv。</summary>
        public int AttackDamage { get; }
        /// <summary>按时间递增的敌波时刻。</summary>
        public IReadOnlyList<float> WaveTimes { get; }
        /// <summary>完整读取并校验后才返回快照；缺失或无效数据直接报错。</summary>
        public static PrototypeConfig Load(Func<string, byte[]> loader)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            var tables = new OutpostLoop.Generated.Tables(name => new ByteBuf(loader(name + ".bytes")));
            if (tables.TbRule.DataList.Count != 1) throw new InvalidOperationException("规则表必须且只能有一行");
            return new PrototypeConfig(tables.TbRule.Get(1));
        }
        /// <summary>复制生成数据，避免业务修改配置原表。</summary>
        private PrototypeConfig(OutpostLoop.Generated.Rule row)
        {
            if (!(row.Duration > 0) || float.IsInfinity(row.Duration)) throw new InvalidOperationException("配置参数无效：Duration");
            Duration = row.Duration;
            if (!(row.InitialHealth > 0)) throw new InvalidOperationException("配置参数无效：InitialHealth");
            InitialHealth = row.InitialHealth;
            if (!(row.CapacityBase > 0)) throw new InvalidOperationException("配置参数无效：CapacityBase");
            CapacityBase = row.CapacityBase;
            if (!(row.CapacityPerLevel > 0)) throw new InvalidOperationException("配置参数无效：CapacityPerLevel");
            CapacityPerLevel = row.CapacityPerLevel;
            if (!(row.ScrapCostBase > 0)) throw new InvalidOperationException("配置参数无效：ScrapCostBase");
            ScrapCostBase = row.ScrapCostBase;
            if (!(row.ScrapCostPerLevel > 0)) throw new InvalidOperationException("配置参数无效：ScrapCostPerLevel");
            ScrapCostPerLevel = row.ScrapCostPerLevel;
            if (!(row.EnergyCostPerLevel > 0)) throw new InvalidOperationException("配置参数无效：EnergyCostPerLevel");
            EnergyCostPerLevel = row.EnergyCostPerLevel;
            if (!(row.MaxLevel > 0)) throw new InvalidOperationException("配置参数无效：MaxLevel");
            MaxLevel = row.MaxLevel;
            if (!(row.HealthPerLevel > 0)) throw new InvalidOperationException("配置参数无效：HealthPerLevel");
            HealthPerLevel = row.HealthPerLevel;
            if (!(row.UpgradeRepair > 0)) throw new InvalidOperationException("配置参数无效：UpgradeRepair");
            UpgradeRepair = row.UpgradeRepair;
            if (!(row.ResourceNodeCount > 0)) throw new InvalidOperationException("配置参数无效：ResourceNodeCount");
            ResourceNodeCount = row.ResourceNodeCount;
            if (!(row.NearNodeCount > 0)) throw new InvalidOperationException("配置参数无效：NearNodeCount");
            NearNodeCount = row.NearNodeCount;
            if (!(row.NearRadius > 0) || float.IsInfinity(row.NearRadius)) throw new InvalidOperationException("配置参数无效：NearRadius");
            NearRadius = row.NearRadius;
            if (!(row.NearRadiusStep > 0) || float.IsInfinity(row.NearRadiusStep)) throw new InvalidOperationException("配置参数无效：NearRadiusStep");
            NearRadiusStep = row.NearRadiusStep;
            if (!(row.FarRadius > 0) || float.IsInfinity(row.FarRadius)) throw new InvalidOperationException("配置参数无效：FarRadius");
            FarRadius = row.FarRadius;
            if (!(row.FarRadiusRange > 0) || float.IsInfinity(row.FarRadiusRange)) throw new InvalidOperationException("配置参数无效：FarRadiusRange");
            FarRadiusRange = row.FarRadiusRange;
            if (!(row.EnergyEvery > 0)) throw new InvalidOperationException("配置参数无效：EnergyEvery");
            EnergyEvery = row.EnergyEvery;
            if (!(row.EnergyAmount > 0)) throw new InvalidOperationException("配置参数无效：EnergyAmount");
            EnergyAmount = row.EnergyAmount;
            if (!(row.ScrapAmount > 0)) throw new InvalidOperationException("配置参数无效：ScrapAmount");
            ScrapAmount = row.ScrapAmount;
            if (!(row.MaxStep > 0) || float.IsInfinity(row.MaxStep)) throw new InvalidOperationException("配置参数无效：MaxStep");
            MaxStep = row.MaxStep;
            if (!(row.MoveSpeed > 0) || float.IsInfinity(row.MoveSpeed)) throw new InvalidOperationException("配置参数无效：MoveSpeed");
            MoveSpeed = row.MoveSpeed;
            if (!(row.MapRadius > 0) || float.IsInfinity(row.MapRadius)) throw new InvalidOperationException("配置参数无效：MapRadius");
            MapRadius = row.MapRadius;
            if (!(row.DepositRadius > 0) || float.IsInfinity(row.DepositRadius)) throw new InvalidOperationException("配置参数无效：DepositRadius");
            DepositRadius = row.DepositRadius;
            if (!(row.GatherInterval > 0) || float.IsInfinity(row.GatherInterval)) throw new InvalidOperationException("配置参数无效：GatherInterval");
            GatherInterval = row.GatherInterval;
            if (!(row.GatherLevelFactor > 0) || float.IsInfinity(row.GatherLevelFactor)) throw new InvalidOperationException("配置参数无效：GatherLevelFactor");
            GatherLevelFactor = row.GatherLevelFactor;
            if (!(row.GatherRadius > 0) || float.IsInfinity(row.GatherRadius)) throw new InvalidOperationException("配置参数无效：GatherRadius");
            GatherRadius = row.GatherRadius;
            if (!(row.MaxEnemies > 0)) throw new InvalidOperationException("配置参数无效：MaxEnemies");
            MaxEnemies = row.MaxEnemies;
            if (!(row.WaveCountBase > 0)) throw new InvalidOperationException("配置参数无效：WaveCountBase");
            WaveCountBase = row.WaveCountBase;
            if (!(row.WaveCountStep > 0)) throw new InvalidOperationException("配置参数无效：WaveCountStep");
            WaveCountStep = row.WaveCountStep;
            if (!(row.SpawnSpread > 0) || float.IsInfinity(row.SpawnSpread)) throw new InvalidOperationException("配置参数无效：SpawnSpread");
            SpawnSpread = row.SpawnSpread;
            if (!(row.SpawnRadius > 0) || float.IsInfinity(row.SpawnRadius)) throw new InvalidOperationException("配置参数无效：SpawnRadius");
            SpawnRadius = row.SpawnRadius;
            if (!(row.SpawnRadiusRange > 0) || float.IsInfinity(row.SpawnRadiusRange)) throw new InvalidOperationException("配置参数无效：SpawnRadiusRange");
            SpawnRadiusRange = row.SpawnRadiusRange;
            if (!(row.EnemyHealthBase > 0) || float.IsInfinity(row.EnemyHealthBase)) throw new InvalidOperationException("配置参数无效：EnemyHealthBase");
            EnemyHealthBase = row.EnemyHealthBase;
            if (!(row.EnemyHealthStep > 0) || float.IsInfinity(row.EnemyHealthStep)) throw new InvalidOperationException("配置参数无效：EnemyHealthStep");
            EnemyHealthStep = row.EnemyHealthStep;
            if (!(row.ShotInterval > 0) || float.IsInfinity(row.ShotInterval)) throw new InvalidOperationException("配置参数无效：ShotInterval");
            ShotInterval = row.ShotInterval;
            if (!(row.ShotRange > 0) || float.IsInfinity(row.ShotRange)) throw new InvalidOperationException("配置参数无效：ShotRange");
            ShotRange = row.ShotRange;
            if (!(row.ShotRangePerLevel > 0) || float.IsInfinity(row.ShotRangePerLevel)) throw new InvalidOperationException("配置参数无效：ShotRangePerLevel");
            ShotRangePerLevel = row.ShotRangePerLevel;
            if (!(row.ShotDamage > 0) || float.IsInfinity(row.ShotDamage)) throw new InvalidOperationException("配置参数无效：ShotDamage");
            ShotDamage = row.ShotDamage;
            if (!(row.ShotDamagePerLevel > 0) || float.IsInfinity(row.ShotDamagePerLevel)) throw new InvalidOperationException("配置参数无效：ShotDamagePerLevel");
            ShotDamagePerLevel = row.ShotDamagePerLevel;
            if (!(row.EnemySpeed > 0) || float.IsInfinity(row.EnemySpeed)) throw new InvalidOperationException("配置参数无效：EnemySpeed");
            EnemySpeed = row.EnemySpeed;
            if (!(row.EnemySpeedPerWave > 0) || float.IsInfinity(row.EnemySpeedPerWave)) throw new InvalidOperationException("配置参数无效：EnemySpeedPerWave");
            EnemySpeedPerWave = row.EnemySpeedPerWave;
            if (!(row.AttackRadius > 0) || float.IsInfinity(row.AttackRadius)) throw new InvalidOperationException("配置参数无效：AttackRadius");
            AttackRadius = row.AttackRadius;
            if (!(row.AttackInterval > 0) || float.IsInfinity(row.AttackInterval)) throw new InvalidOperationException("配置参数无效：AttackInterval");
            AttackInterval = row.AttackInterval;
            if (!(row.AttackDamage > 0)) throw new InvalidOperationException("配置参数无效：AttackDamage");
            AttackDamage = row.AttackDamage;
            // 限制实体和等级规模，拒绝会造成无界循环或整数溢出的异常配置。
            if (ResourceNodeCount > 4096 || MaxEnemies > 1024 || MaxLevel > 100 || NearNodeCount > ResourceNodeCount)
                throw new InvalidOperationException("资源数量、敌人数或升级等级超出原型安全范围");
            // 最远资源点和出生点必须位于可达地图中，近区按最后一个节点计算。
            if (NearRadius + (NearNodeCount - 1) * NearRadiusStep > MapRadius ||
                FarRadius + FarRadiusRange > MapRadius || SpawnRadius + SpawnRadiusRange > MapRadius ||
                DepositRadius > MapRadius || GatherRadius > MapRadius || AttackRadius > MapRadius)
                throw new InvalidOperationException("资源点、出生点或交互范围超出地图边界");
            if ((long)CapacityBase + (long)MaxLevel * CapacityPerLevel > int.MaxValue ||
                (long)ScrapCostBase + (long)MaxLevel * ScrapCostPerLevel > int.MaxValue ||
                (long)MaxLevel * EnergyCostPerLevel > int.MaxValue ||
                (long)InitialHealth + (long)MaxLevel * HealthPerLevel + UpgradeRepair > int.MaxValue)
                throw new InvalidOperationException("等级参数组合存在整数溢出风险");
            var waves = row.WaveTimes.ToArray();
            if (waves.Length == 0) throw new InvalidOperationException("敌波不能为空");
            for (int i = 0; i < waves.Length; i++)
                if (!(waves[i] > (i == 0 ? 0 : waves[i - 1])) || waves[i] >= Duration)
                    throw new InvalidOperationException("敌波时刻必须递增且早于局时上限");
            WaveTimes = Array.AsReadOnly(waves);
        }
    }
}
