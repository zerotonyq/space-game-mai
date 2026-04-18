using UnityEngine;

namespace App.Entities
{
    public class EntityProjectileStock : MonoBehaviour
    {
        [Header("Crafted Ammo")]
        [SerializeField] [Min(0)] private int rockets;
        [SerializeField] [Min(0)] private int drillType1;
        [SerializeField] [Min(0)] private int drillType2;
        [SerializeField] [Min(0)] private int drillType3;

        public int Rockets => rockets;
        public int DrillType1 => drillType1;
        public int DrillType2 => drillType2;
        public int DrillType3 => drillType3;

        public void SetCounts(int rocketCount, int drill1Count, int drill2Count, int drill3Count)
        {
            rockets = Mathf.Max(0, rocketCount);
            drillType1 = Mathf.Max(0, drill1Count);
            drillType2 = Mathf.Max(0, drill2Count);
            drillType3 = Mathf.Max(0, drill3Count);
        }

        public bool TryCreateRocket(EntityMaterialInventory inventory)
        {
            if (!inventory || !inventory.TrySpend(Planets.Core.PlanetSegmentMaterial.TitaniumOre, 1))
                return false;

            rockets++;
            return true;
        }

        public bool TryCreateDrillType1()
        {
            drillType1++;
            return true;
        }

        public bool TryCreateDrillType2(EntityMaterialInventory inventory)
        {
            if (!inventory || !inventory.TrySpend(Planets.Core.PlanetSegmentMaterial.IronOre, 1))
                return false;

            drillType2++;
            return true;
        }

        public bool TryCreateDrillType3(EntityMaterialInventory inventory)
        {
            if (!inventory || !inventory.TrySpend(Planets.Core.PlanetSegmentMaterial.CobaltOre, 1))
                return false;

            drillType3++;
            return true;
        }

        public bool TryConsumeRocket()
        {
            return TryConsume(ref rockets);
        }

        public bool TryConsumeDrillType1()
        {
            return TryConsume(ref drillType1);
        }

        public bool TryConsumeDrillType2()
        {
            return TryConsume(ref drillType2);
        }

        public bool TryConsumeDrillType3()
        {
            return TryConsume(ref drillType3);
        }

        private static bool TryConsume(ref int value)
        {
            if (value <= 0)
                return false;

            value--;
            return true;
        }
    }
}
