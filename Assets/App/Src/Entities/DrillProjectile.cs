using App.Planets.Core;
using App.Planets.Generation;
using UnityEngine;

namespace App.Entities
{
    public class DrillProjectile : Projectile
    {
        [Header("Drill")]
        [SerializeField] private PlanetSegmentMaterial drillMaterial = PlanetSegmentMaterial.IronOre;
        [SerializeField] [Min(0.1f)] private float damagePerSecond = 12f;
        [SerializeField] [Min(0.05f)] private float damageTickSeconds = 0.2f;

        [Header("Drop")]
        [SerializeField] private MinedMaterialChunk minedMaterialPrefab;
        [SerializeField] [Min(0f)] private float droppedChunkAltitude = 0.5f;

        private PlanetSegment _targetSegment;
        private PlanetGenerator _targetPlanet;
        private Vector3 _contactPosition;
        private float _damageTickTimer;
        private int _minedPoints;
        private bool _isDrilling;
        private bool _consumed;
        private bool _dropMinedChunkOnComplete;

        public PlanetSegmentMaterial DrillMaterial => drillMaterial;
        public bool IsDrillingActive => _isDrilling;
        public bool IsProjectileLaunched => IsLaunched;
        public Vector3 ProjectileDirection => Direction;
        public PlanetGenerator TargetPlanet => _targetPlanet;
        public PlanetSegment TargetSegment => _targetSegment;
        public int MinedPoints => _minedPoints;
        public float DamageTickTimer => _damageTickTimer;
        public bool DropMinedChunkOnComplete => _dropMinedChunkOnComplete;

        private void OnTriggerEnter2D(Collider2D other)
        {
            var point = other != null ? other.ClosestPoint(transform.position) : (Vector2)transform.position;
            TryStartDrilling(other, point);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var point = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
            TryStartDrilling(collision.collider, point);
        }

        protected override void Update()
        {
            if (!_isDrilling)
            {
                base.Update();
                return;
            }

            if (_targetSegment == null || _targetSegment.IsDestroyed)
            {
                CompleteDrilling();
                return;
            }

            _damageTickTimer += Time.deltaTime;
            if (_damageTickTimer < damageTickSeconds)
                return;

            _damageTickTimer = 0f;
            var damage = Mathf.Max(1, Mathf.CeilToInt(damagePerSecond * damageTickSeconds));
            var before = _targetSegment.CurrentMaterialPoints;
            var destroyed = _targetSegment.ApplyDamage(damage);
            var dealt = Mathf.Max(0, before - _targetSegment.CurrentMaterialPoints);
            _minedPoints += dealt;

            if (destroyed || _targetSegment.IsDestroyed)
                CompleteDrilling();
        }

        private void TryStartDrilling(Collider2D other, Vector2 hitPoint)
        {
            if (_consumed || _isDrilling || other == null)
                return;

            if (!CanInteractWith(other))
                return;

            var segment = other.GetComponentInParent<PlanetSegment>();
            if (segment == null || segment.IsDestroyed)
            {
                DestroySelf();
                return;
            }

            if (!CanDrillMaterial(segment.Material))
            {
                DestroySelf();
                return;
            }

            _consumed = true;
            _isDrilling = true;
            _targetSegment = segment;
            _targetPlanet = segment.GetComponentInParent<PlanetGenerator>();
            _contactPosition = hitPoint;
            _dropMinedChunkOnComplete = segment.Material != PlanetSegmentMaterial.Stone;

            StopMovement();
            transform.position = hitPoint;
            DisableOwnColliders();
        }

        private void CompleteDrilling()
        {
            if (!_isDrilling)
                return;

            _isDrilling = false;

            if (_dropMinedChunkOnComplete && _minedPoints > 0)
                SpawnMinedChunk();

            DestroySelf();
        }

        private bool CanDrillMaterial(PlanetSegmentMaterial targetMaterial)
        {
            if (targetMaterial == drillMaterial)
                return true;

            return drillMaterial == PlanetSegmentMaterial.IronOre && targetMaterial == PlanetSegmentMaterial.Stone;
        }

        private void SpawnMinedChunk()
        {
            MinedMaterialChunk chunk;
            if (minedMaterialPrefab != null)
                chunk = Instantiate(minedMaterialPrefab, _contactPosition, Quaternion.identity, null);
            else
                chunk = new GameObject("MinedMaterialChunk").AddComponent<MinedMaterialChunk>();

            chunk.Initialize(drillMaterial, _minedPoints);
            SetupStaticOrbit(chunk.transform);
        }

        private void SetupStaticOrbit(Transform chunk)
        {
            if (chunk == null || _targetPlanet == null)
                return;

            var orbit = chunk.GetComponent<PlanetOrbitMovement>();
            if (!orbit)
                orbit = chunk.gameObject.AddComponent<PlanetOrbitMovement>();

            orbit.SetAngularSpeedDegPerSecond(0f);
            orbit.SetAltitudeFromSurface(droppedChunkAltitude);
            orbit.SetOrbitCenter(_targetPlanet.transform, _targetPlanet.EstimatedOuterRadiusUnits);

            var offset = (Vector2)(chunk.position - _targetPlanet.transform.position);
            if (offset.sqrMagnitude > 0.000001f)
                orbit.SetCurrentAngleDeg(Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg);

            orbit.SnapToOrbitPosition();
        }

        private void DisableOwnColliders()
        {
            var colliders = GetComponents<Collider2D>();
            for (var i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        public void RestoreDrillingState(
            PlanetGenerator targetPlanet,
            PlanetSegment targetSegment,
            Vector3 contactPosition,
            int minedPoints,
            float damageTickTimer,
            bool dropMinedChunkOnComplete)
        {
            _targetPlanet = targetPlanet;
            _targetSegment = targetSegment;
            _contactPosition = contactPosition;
            _minedPoints = Mathf.Max(0, minedPoints);
            _damageTickTimer = Mathf.Max(0f, damageTickTimer);
            _dropMinedChunkOnComplete = dropMinedChunkOnComplete;
            _isDrilling = true;
            _consumed = true;
            StopMovement();
            transform.position = contactPosition;
            DisableOwnColliders();
        }
    }
}
