using UnityEngine;

namespace App.Entities
{
    public sealed class EntityPeacefulTag : MonoBehaviour
    {
        [Header("Mining AI")]
        [SerializeField] [Min(0.1f)] private float mineShotIntervalSeconds = 2f;
        [SerializeField] [Min(1f)] private float resourceProbeDistance = 64f;
        [SerializeField] private LayerMask resourceProbeMask = Physics2D.DefaultRaycastLayers;
        [SerializeField] private bool autoCraftDrillAmmo = true;
        
        [Header("Planet Transfer AI")]
        [SerializeField] [Min(1f)] private float transferCheckIntervalSeconds = 10f;
        [SerializeField] [Range(0f, 1f)] private float transferChancePerCheck = 0.2f;
        [SerializeField] [Min(0.1f)] private float transferSpeedUnitsPerSecond = 8f;
        [SerializeField] [Min(0.05f)] private float transferArrivalThresholdUnits = 0.35f;

        public float MineShotIntervalSeconds => mineShotIntervalSeconds;
        public float ResourceProbeDistance => resourceProbeDistance;
        public LayerMask ResourceProbeMask => resourceProbeMask;
        public bool AutoCraftDrillAmmo => autoCraftDrillAmmo;
        public float TransferCheckIntervalSeconds => transferCheckIntervalSeconds;
        public float TransferChancePerCheck => transferChancePerCheck;
        public float TransferSpeedUnitsPerSecond => transferSpeedUnitsPerSecond;
        public float TransferArrivalThresholdUnits => transferArrivalThresholdUnits;
    }
}
