using UnityEngine;

namespace App.Planets.Core
{
    public static class PlanetSegmentPointsCalculator
    {
        private const float BasePointsPerAreaUnit = 24f;

        public static float CalculateCircleAreaUnits(float radiusUnits)
        {
            var safeRadius = Mathf.Max(0f, radiusUnits);
            return Mathf.PI * safeRadius * safeRadius;
        }

        public static float CalculateRingSegmentAreaUnits(float innerRadiusUnits, float outerRadiusUnits, float angleDeg)
        {
            var safeInner = Mathf.Max(0f, innerRadiusUnits);
            var safeOuter = Mathf.Max(safeInner, outerRadiusUnits);
            var safeAngle = Mathf.Clamp(angleDeg, 0f, 360f);
            var fullRingArea = Mathf.PI * (safeOuter * safeOuter - safeInner * safeInner);
            return fullRingArea * (safeAngle / 360f);
        }

        public static int CalculatePoints(float areaUnits, PlanetSegmentMaterial material)
        {
            var safeArea = Mathf.Max(0.0001f, areaUnits);
            var weightedValue = safeArea * BasePointsPerAreaUnit;
            return Mathf.Max(1, Mathf.CeilToInt(weightedValue));
        }
    }
}
