using System;
using UnityEngine;

namespace App.Entities
{
    public enum OrbitRotationDirection
    {
        Clockwise = -1,
        CounterClockwise = 1
    }

    public class PlanetOrbitMovement : MonoBehaviour
    {
        [SerializeField] [Min(0.01f)] private float angularSpeedDegPerSecond = 25f;
        [SerializeField] [Min(0f)] private float altitudeFromSurface = 2f;
        [SerializeField] private OrbitRotationDirection rotationDirection = OrbitRotationDirection.Clockwise;
        [SerializeField] private bool keepCurrentAngleWhenCenterChanges = true;

        private Transform _orbitCenter;
        private float _surfaceRadiusUnits;
        private float _currentAngleDeg;

        public event Action<Transform> OrbitCenterChanged;

        public Transform OrbitCenter => _orbitCenter;
        public float SurfaceRadiusUnits => _surfaceRadiusUnits;
        public float AltitudeFromSurface => altitudeFromSurface;
        public float AngularSpeedDegPerSecond => angularSpeedDegPerSecond;
        public OrbitRotationDirection RotationDirection => rotationDirection;
        public float CurrentAngleDeg => _currentAngleDeg;

        private void Update()
        {
            if (_orbitCenter == null)
                return;

            var directionSign = (int)rotationDirection;
            _currentAngleDeg += angularSpeedDegPerSecond * directionSign * Time.deltaTime;
            SnapToOrbitPosition();
        }

        public void SetOrbitCenter(Transform center)
        {
            SetOrbitCenter(center, _surfaceRadiusUnits);
        }

        public void SetOrbitCenter(Transform center, float surfaceRadiusUnits)
        {
            var nextSurfaceRadius = Mathf.Max(0f, surfaceRadiusUnits);
            var changed = _orbitCenter != center || !Mathf.Approximately(_surfaceRadiusUnits, nextSurfaceRadius);

            _orbitCenter = center;
            _surfaceRadiusUnits = nextSurfaceRadius;

            if (_orbitCenter == null)
            {
                if (changed)
                    OrbitCenterChanged?.Invoke(_orbitCenter);
                return;
            }

            if (keepCurrentAngleWhenCenterChanges)
            {
                var offset = (Vector2)(transform.position - _orbitCenter.position);
                if (offset.sqrMagnitude > 0.0001f)
                    _currentAngleDeg = Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg;
            }

            if (changed)
                OrbitCenterChanged?.Invoke(_orbitCenter);
        }

        public void SetRotationDirection(OrbitRotationDirection direction)
        {
            rotationDirection = direction;
        }

        public void SetAltitudeFromSurface(float altitudeUnits)
        {
            altitudeFromSurface = Mathf.Max(0f, altitudeUnits);
        }

        public void SetAngularSpeedDegPerSecond(float speedDegPerSecond)
        {
            angularSpeedDegPerSecond = Mathf.Max(0.01f, speedDegPerSecond);
        }

        public void SetCurrentAngleDeg(float angleDeg)
        {
            _currentAngleDeg = angleDeg;
        }

        public void SnapToOrbitPosition()
        {
            if (_orbitCenter == null)
                return;

            var radius = Mathf.Max(0.01f, _surfaceRadiusUnits + altitudeFromSurface);
            var angleRad = _currentAngleDeg * Mathf.Deg2Rad;
            var offset = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;
            var currentZ = transform.position.z;
            transform.position = new Vector3(
                _orbitCenter.position.x + offset.x,
                _orbitCenter.position.y + offset.y,
                currentZ);

            var fromCenter = (Vector2)transform.position - (Vector2)_orbitCenter.position;
            AlignYAxisToWorldDirection(fromCenter);
        }

        public void AlignXAxisToWorldDirection(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.000001f)
                return;

            var angleDeg = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
        }

        public void AlignYAxisToWorldDirection(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.000001f)
                return;

            var angleDeg = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
            transform.rotation = Quaternion.Euler(0f, 0f, angleDeg);
        }
    }
}
