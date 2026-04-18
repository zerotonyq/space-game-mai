using App.Planets.Core;
using UnityEngine;

namespace App.Entities
{
    public class EntityMaterialInventory : MonoBehaviour
    {
        [Header("Stored Material Points")]
        [SerializeField] [Min(0)] private int magmaPoints;
        [SerializeField] [Min(0)] private int metal1Points;
        [SerializeField] [Min(0)] private int metal2Points;
        [SerializeField] [Min(0)] private int metal3Points;

        public int MagmaPoints => magmaPoints;
        public int Metal1Points => metal1Points;
        public int Metal2Points => metal2Points;
        public int Metal3Points => metal3Points;

        public void SetPoints(int magma, int metal1, int metal2, int metal3)
        {
            magmaPoints = Mathf.Max(0, magma);
            metal1Points = Mathf.Max(0, metal1);
            metal2Points = Mathf.Max(0, metal2);
            metal3Points = Mathf.Max(0, metal3);
        }

        public bool TrySpend(PlanetSegmentMaterial material, int amount)
        {
            if (amount <= 0)
                return true;

            switch (material)
            {
                case PlanetSegmentMaterial.Magma:
                    return TrySpend(ref magmaPoints, amount);
                case PlanetSegmentMaterial.IronOre:
                    return TrySpend(ref metal1Points, amount);
                case PlanetSegmentMaterial.CobaltOre:
                    return TrySpend(ref metal2Points, amount);
                case PlanetSegmentMaterial.TitaniumOre:
                    return TrySpend(ref metal3Points, amount);
                default:
                    return false;
            }
        }

        public void Add(PlanetSegmentMaterial material, int amount)
        {
            if (amount <= 0)
                return;

            switch (material)
            {
                case PlanetSegmentMaterial.Magma:
                    magmaPoints += amount;
                    break;
                case PlanetSegmentMaterial.IronOre:
                    metal1Points += amount;
                    break;
                case PlanetSegmentMaterial.CobaltOre:
                    metal2Points += amount;
                    break;
                case PlanetSegmentMaterial.TitaniumOre:
                    metal3Points += amount;
                    break;
            }
        }

        private static bool TrySpend(ref int source, int amount)
        {
            if (source < amount)
                return false;

            source -= amount;
            return true;
        }
    }
}
