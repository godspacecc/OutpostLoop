using UnityEngine;

namespace OutpostLoop
{
    public sealed partial class ScavengeRun
    {
        public const int StorageCapacity = 4;
        public const float WorkshopSeconds = 6f;

        // 工作台占用仓库的一格作为产物预留位，离开据点也继续加工。
        public ScavengeItem Stored { get; private set; }
        public float WorkshopRemaining { get; private set; }
        public bool WorkshopUsed { get; private set; }
        public int StorageUsed => CountItems(Stored) + (WorkshopRemaining > 0 ? 1 : 0);
        public bool CanUseStorage => Outcome == ScavengeOutcome.Exploring && NearShelter && ShelterRemaining <= 0;
        public bool CanStartWorkshop => CanUseStorage && !WorkshopUsed && (Stored & ScavengeItem.RepairMaterials) != 0;
        public bool HasRepairKit => ((Stored | Inventory) & ScavengeItem.RepairKit) != 0;
        public bool CanRepairShelter => CanUseStorage && HasRepairKit && ShelterHealth < ShelterMaxHealth;

        /// <summary>两边都有空位校验，转移只在完工据点旁生效。</summary>
        public bool TransferStorage(ScavengeItem item, bool take)
        {
            if (!CanUseStorage || !IsSingleItem(item)) return false;
            if (take)
            {
                if ((Stored & item) == 0 || Count >= Capacity) return false;
                Stored &= ~item;
                Inventory |= item;
            }
            else
            {
                if ((Inventory & item) == 0 || StorageUsed >= StorageCapacity) return false;
                Inventory &= ~item;
                Stored |= item;
            }
            return true;
        }

        /// <summary>消耗仓库内的维修材料，并用它腾出的格子预留产物位置。</summary>
        public bool StartWorkshop()
        {
            if (!CanStartWorkshop) return false;
            Stored &= ~ScavengeItem.RepairMaterials;
            WorkshopUsed = true;
            WorkshopRemaining = WorkshopSeconds;
            return true;
        }

        /// <summary>优先使用仓库中的维修件，满耐久时保留道具。</summary>
        public bool RepairShelter()
        {
            if (!CanRepairShelter) return false;
            if ((Stored & ScavengeItem.RepairKit) != 0) Stored &= ~ScavengeItem.RepairKit;
            else Inventory &= ~ScavengeItem.RepairKit;
            ShelterHealth = Mathf.Min(ShelterMaxHealth, ShelterHealth + 2);
            return true;
        }

        private void TickWorkshop(float dt)
        {
            if (WorkshopRemaining <= 0) return;
            WorkshopRemaining = Mathf.Max(0, WorkshopRemaining - dt);
            if (WorkshopRemaining <= 0) Stored |= ScavengeItem.RepairKit;
        }

        private void ClearShelterStorage()
        {
            Stored = ScavengeItem.None;
            WorkshopRemaining = 0;
        }
    }
}
