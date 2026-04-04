using UnityEngine;

namespace App.Planets.GfxGen
{
    public class PlanetSegmentSpawnRule : MonoBehaviour
    {
        [Header("1) Total layers gate: must be > N")]
        [SerializeField] [Min(0)] private int minTotalLayersExclusive = 0;

        [Header("2) Core radius gate: must be > K")]
        [SerializeField] [Min(0f)] private float minCoreRadiusExclusive = 0f;

        [Header("3) Placement percentile window [L..P]")]
        [SerializeField] [Range(0f, 100f)] private float minLayerPercentile = 0f;
        [SerializeField] [Range(0f, 100f)] private float maxLayerPercentile = 100f;

        public float GetWeight(PlanetPlacementContext context)
        {
            if (context.TotalLayers <= minTotalLayersExclusive)
                return 0f;

            if (context.CoreRadiusUnits <= minCoreRadiusExclusive)
                return 0f;

            var percentile = context.LayerPercentile;
            if (percentile < minLayerPercentile || percentile > maxLayerPercentile)
                return 0f;

            var layersFactor = GetThresholdSatisfactionFactor(context.TotalLayers, minTotalLayersExclusive);
            var radiusFactor = GetThresholdSatisfactionFactor(context.CoreRadiusUnits, minCoreRadiusExclusive);
            var percentileFactor = GetPercentileCenterFactor(percentile, minLayerPercentile, maxLayerPercentile);

            return layersFactor * radiusFactor * percentileFactor;
        }

        private void OnValidate()
        {
            if (maxLayerPercentile < minLayerPercentile)
                maxLayerPercentile = minLayerPercentile;
        }

        private static float GetThresholdSatisfactionFactor(float currentValue, float thresholdExclusive)
        {
            var denominator = Mathf.Max(0.0001f, currentValue);
            var normalizedExcess = Mathf.Clamp01((currentValue - thresholdExclusive) / denominator);
            return 1f + normalizedExcess;
        }

        private static float GetPercentileCenterFactor(float percentile, float minPercentile, float maxPercentile)
        {
            if (Mathf.Approximately(minPercentile, maxPercentile))
                return 2f;

            var center = (minPercentile + maxPercentile) * 0.5f;
            var halfWindow = (maxPercentile - minPercentile) * 0.5f;
            var distanceFromCenter = Mathf.Abs(percentile - center);
            var closeness = Mathf.Clamp01(1f - distanceFromCenter / Mathf.Max(0.0001f, halfWindow));
            return 1f + closeness;
        }
    }
}
