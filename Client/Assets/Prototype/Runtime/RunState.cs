namespace OutpostLoop
{
    /// <summary>独立可玩预览的纯规则状态；不引用旧项目资源或玩家数据。</summary>
    public sealed class RunState
    {
        // 状态只通过本类的事务方法修改；表现层不直接写资源。
        public int Scrap { get; private set; }
        public int Energy { get; private set; }
        public int CargoScrap { get; private set; }
        public int CargoEnergy { get; private set; }
        public int Capacity => 12 + Levels[0] * 4;
        public float Elapsed { get; private set; }
        public bool Paused { get; set; }
        public int Health { get; private set; } = 100;
        public int Outcome { get; private set; }
        public int[] Levels { get; } = new int[3];
        /// <summary>尝试装入一单位资源；暂停、终局与满载时拒绝。</summary>
        public bool Gather(bool energy)
        {
            if (Paused || Outcome != 0 || CargoScrap + CargoEnergy >= Capacity) return false;
            if (energy) CargoEnergy++; else CargoScrap++;
            return true;
        }
        /// <summary>回到基地后一次性入库，重复调用不会重复入账。</summary>
        public void Deposit()
        {
            if (Paused || Outcome != 0) return;
            Scrap += CargoScrap; Energy += CargoEnergy;
            CargoScrap = CargoEnergy = 0;
        }
        /// <summary>查询下一等级价格；首级只消耗废料，之后需要能源。</summary>
        public int ScrapCost(int route) => route >= 0 && route < 3 ? 8 + Levels[route] * 8 : int.MaxValue;
        public int EnergyCost(int route) => route >= 0 && route < 3 ? Levels[route] * 3 : int.MaxValue;
        /// <summary>校验全部条件后原子扣费并升级；耐久路线同时修复基地。</summary>
        public bool Upgrade(int route)
        {
            if (route < 0 || route >= 3 || Paused || Outcome != 0 || Levels[route] >= 3) return false;
            int scrap = ScrapCost(route), energy = EnergyCost(route);
            if (Scrap < scrap || Energy < energy) return false;
            Scrap -= scrap; Energy -= energy; Levels[route]++;
            if (route == 2) Health = System.Math.Min(100 + Levels[2] * 50, Health + 75);
            return true;
        }
        /// <summary>推进有界局时；到期不无限拖延，仍有敌人时判防守失败。</summary>
        public void Tick(float seconds, int enemies)
        {
            if (Paused || Outcome != 0 || seconds < 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Elapsed = System.Math.Min(720, Elapsed + seconds);
            if (Elapsed >= 720) Outcome = Health > 0 && enemies == 0 ? 1 : -1;
        }
        /// <summary>扣除非负基地伤害，死亡后只结算一次。</summary>
        public void Damage(int amount)
        {
            if (Paused || Outcome != 0 || amount <= 0) return;
            Health = System.Math.Max(0, Health - amount);
            if (Health == 0) Outcome = -1;
        }
    }
}
