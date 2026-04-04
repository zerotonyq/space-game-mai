using UnityEngine;

namespace App.Entities
{
    public sealed class EntityHeroTag : MonoBehaviour
    {
        [Header("Hero Flight Settings")]
        [SerializeField] [Min(0f)] private float orbitAltitudeFromSurface = 2f;
        [SerializeField] [Min(0.01f)] private float angularSpeedDegPerSecond = 25f;
        [SerializeField] private OrbitRotationDirection rotationDirection = OrbitRotationDirection.Clockwise;
        [SerializeField] [Range(0f, 360f)] private float startAngleDeg = 0f;
        [SerializeField] private bool randomizeStartAngle = true;
        
        [Header("Planet Transfer")]
        [SerializeField] [Min(0.01f)] private float transferSpeedUnitsPerSecond = 22f;
        [SerializeField] [Min(0.01f)] private float transferArrivalThresholdUnits = 0.25f;

        public float OrbitAltitudeFromSurface => orbitAltitudeFromSurface;
        public float AngularSpeedDegPerSecond => angularSpeedDegPerSecond;
        public OrbitRotationDirection RotationDirection => rotationDirection;
        public float TransferSpeedUnitsPerSecond => transferSpeedUnitsPerSecond;
        public float TransferArrivalThresholdUnits => transferArrivalThresholdUnits;

        public float ResolveStartAngleDeg()
        {
            return randomizeStartAngle
                ? Random.Range(0f, 360f)
                : Mathf.Repeat(startAngleDeg, 360f);
        }
    }
}
