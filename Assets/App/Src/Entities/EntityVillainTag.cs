using App.Infrastructure.DI.Base;
using App.Planets.Core;
using App.Planets.Persistence;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace App.Entities
{
    public sealed class EntityVillainTag : MonoBehaviour
    {
        [Header("Mining AI")]
        [SerializeField] [Min(0.1f)] private float mineShotIntervalSeconds = 2.5f;
        [SerializeField] [Min(1f)] private float resourceProbeDistance = 64f;
        [SerializeField] private LayerMask resourceProbeMask = Physics2D.DefaultRaycastLayers;
        [SerializeField] private bool autoCraftDrillAmmo = true;

        [Header("Combat AI")]
        [SerializeField] [Min(0.1f)] private float attackShotIntervalSeconds = 1.4f;
        [SerializeField] [Min(0f)] private float attackAltitudeOffsetFromTarget = 1.25f;
        [SerializeField] [Min(0.05f)] private float attackReactionDelaySeconds = 0.45f;
        [SerializeField] [Range(1f, 45f)] private float attackAimToleranceDeg = 10f;
        [SerializeField] [Min(0.01f)] private float attackAltitudeAdjustSpeedUnitsPerSecond = 2f;
        [SerializeField] [Range(0f, 1f)] private float attackMissChance = 0.35f;
        [SerializeField] [Range(1f, 45f)] private float attackMissAngleMinDeg = 8f;
        [SerializeField] [Range(1f, 60f)] private float attackMissAngleMaxDeg = 22f;
        
        [Header("Planet Transfer AI")]
        [SerializeField] [Min(1f)] private float transferCheckIntervalSeconds = 10f;
        [SerializeField] [Range(0f, 1f)] private float transferChancePerCheck = 0.25f;
        [SerializeField] [Min(0.1f)] private float transferSpeedUnitsPerSecond = 9f;
        [SerializeField] [Min(0.05f)] private float transferArrivalThresholdUnits = 0.35f;

        public float MineShotIntervalSeconds => mineShotIntervalSeconds;
        public float ResourceProbeDistance => resourceProbeDistance;
        public LayerMask ResourceProbeMask => resourceProbeMask;
        public bool AutoCraftDrillAmmo => autoCraftDrillAmmo;

        public float AttackShotIntervalSeconds => attackShotIntervalSeconds;
        public float AttackAltitudeOffsetFromTarget => attackAltitudeOffsetFromTarget;
        public float AttackReactionDelaySeconds => attackReactionDelaySeconds;
        public float AttackAimToleranceDeg => attackAimToleranceDeg;
        public float AttackAltitudeAdjustSpeedUnitsPerSecond => attackAltitudeAdjustSpeedUnitsPerSecond;
        public float AttackMissChance => attackMissChance;
        public float AttackMissAngleMinDeg => attackMissAngleMinDeg;
        public float AttackMissAngleMaxDeg => Mathf.Max(attackMissAngleMinDeg, attackMissAngleMaxDeg);
        public float TransferCheckIntervalSeconds => transferCheckIntervalSeconds;
        public float TransferChancePerCheck => transferChancePerCheck;
        public float TransferSpeedUnitsPerSecond => transferSpeedUnitsPerSecond;
        public float TransferArrivalThresholdUnits => transferArrivalThresholdUnits;
    }

    public sealed class EntityNpcCombatRuntimeService : IGameService, ITickable
    {
        private const float TickIntervalSeconds = 0.2f;

        private readonly System.Collections.Generic.Dictionary<int, float> _nextMineShotTimeByEntity = new();
        private readonly System.Collections.Generic.Dictionary<int, float> _nextAttackShotTimeByEntity = new();
        private readonly System.Collections.Generic.Dictionary<int, float> _nextAttackCourseUpdateTimeByEntity = new();
        private readonly System.Collections.Generic.Dictionary<int, float> _nextTransferCheckTimeByEntity = new();
        private readonly System.Collections.Generic.Dictionary<int, NpcTravelState> _activeTravelStateByEntity = new();
        private readonly System.Collections.Generic.List<PlanetWorldManager.PlanetBinding> _planetsBuffer = new();
        private readonly System.Collections.Generic.List<int> _travelEntityIdsBuffer = new();
        private readonly PlanetWorldManager _worldManager;

        private float _nextTickTime;

        public EntityNpcCombatRuntimeService([InjectOptional] PlanetWorldManager worldManager)
        {
            _worldManager = worldManager;
        }

        public UniTask Initialize()
        {
            _nextTickTime = 0f;
            _nextMineShotTimeByEntity.Clear();
            _nextAttackShotTimeByEntity.Clear();
            _nextAttackCourseUpdateTimeByEntity.Clear();
            _nextTransferCheckTimeByEntity.Clear();
            _activeTravelStateByEntity.Clear();
            return UniTask.CompletedTask;
        }

        public void Tick()
        {
            UpdateActiveTravels();

            if (Time.time < _nextTickTime)
                return;

            _nextTickTime = Time.time + TickIntervalSeconds;

            var peacefuls = Object.FindObjectsByType<EntityPeacefulTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var villains = Object.FindObjectsByType<EntityVillainTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            var heroes = Object.FindObjectsByType<EntityHeroTag>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            for (var i = 0; i < peacefuls.Length; i++)
                ProcessPeaceful(peacefuls[i]);

            for (var i = 0; i < villains.Length; i++)
                ProcessVillain(villains[i], peacefuls, heroes);
        }

        private void ProcessPeaceful(EntityPeacefulTag tag)
        {
            if (!tag)
                return;

            if (!TryGetCommonComponents(tag.gameObject, out var orbit, out var shooter, out var stock, out var inventory))
                return;

            if (ProcessNpcTravel(tag.gameObject, orbit))
                return;

            TryStartRandomPlanetTransfer(
                tag.gameObject,
                orbit,
                tag.TransferCheckIntervalSeconds,
                tag.TransferChancePerCheck,
                tag.TransferSpeedUnitsPerSecond,
                tag.TransferArrivalThresholdUnits);

            TryMineBySurfaceUnderEntity(
                tag.gameObject,
                orbit,
                shooter,
                stock,
                inventory,
                tag.MineShotIntervalSeconds,
                tag.ResourceProbeMask,
                tag.ResourceProbeDistance,
                tag.AutoCraftDrillAmmo);
        }

        private void ProcessVillain(EntityVillainTag tag, EntityPeacefulTag[] peacefuls, EntityHeroTag[] heroes)
        {
            if (!tag)
                return;

            if (!TryGetCommonComponents(tag.gameObject, out var orbit, out var shooter, out var stock, out var inventory))
                return;

            if (ProcessNpcTravel(tag.gameObject, orbit))
                return;

            TryStartRandomPlanetTransfer(
                tag.gameObject,
                orbit,
                tag.TransferCheckIntervalSeconds,
                tag.TransferChancePerCheck,
                tag.TransferSpeedUnitsPerSecond,
                tag.TransferArrivalThresholdUnits);

            var hasRockets = stock.Rockets > 0;
            if (hasRockets && TryFindTargetOnSamePlanet(tag.transform, orbit, peacefuls, heroes, out var targetTransform, out var targetOrbit))
            {
                var targetAltitude = targetOrbit ? targetOrbit.EffectiveAltitudeFromSurface : 0f;
                var desiredAltitude = Mathf.Max(0f, targetAltitude + tag.AttackAltitudeOffsetFromTarget);
                var altitudeStep = Mathf.Max(0.01f, tag.AttackAltitudeAdjustSpeedUnitsPerSecond) * TickIntervalSeconds;
                var nextAltitude = Mathf.MoveTowards(orbit.AltitudeFromSurface, desiredAltitude, altitudeStep);
                orbit.SetAltitudeFromSurface(nextAltitude);

                var villainId = tag.gameObject.GetInstanceID();
                if (Time.time >= GetNextAllowedShotTime(_nextAttackCourseUpdateTimeByEntity, villainId))
                {
                    UpdateOrbitCourseTowardsTarget(
                        orbit,
                        targetTransform.position,
                        tag.AttackAimToleranceDeg);
                    SetNextAllowedShotTime(_nextAttackCourseUpdateTimeByEntity, villainId, tag.AttackReactionDelaySeconds);
                }

                var alignedForShot = IsOrbitAlignedToTarget(
                    orbit,
                    targetTransform.position,
                    tag.AttackAimToleranceDeg);

                if (Time.time >= GetNextAllowedShotTime(_nextAttackShotTimeByEntity, villainId) &&
                    alignedForShot &&
                    shooter.ShootRocketAtWorldPosition(GetVillainAimPoint(shooter.transform.position, targetTransform.position, tag)) &&
                    stock.TryConsumeRocket())
                {
                    SetNextAllowedShotTime(_nextAttackShotTimeByEntity, villainId, tag.AttackShotIntervalSeconds);
                }

                return;
            }

            TryMineBySurfaceUnderEntity(
                tag.gameObject,
                orbit,
                shooter,
                stock,
                inventory,
                tag.MineShotIntervalSeconds,
                tag.ResourceProbeMask,
                tag.ResourceProbeDistance,
                tag.AutoCraftDrillAmmo);
        }

        private bool ProcessNpcTravel(GameObject entity, PlanetOrbitMovement orbit)
        {
            if (!entity || !orbit)
                return false;

            var entityId = entity.GetInstanceID();
            return _activeTravelStateByEntity.ContainsKey(entityId);
        }

        private void TryStartRandomPlanetTransfer(
            GameObject entity,
            PlanetOrbitMovement orbit,
            float checkIntervalSeconds,
            float chancePerCheck,
            float transferSpeedUnitsPerSecond,
            float arrivalThresholdUnits)
        {
            if (!entity || !orbit || orbit.OrbitCenter == null)
                return;

            var entityId = entity.GetInstanceID();
            if (_activeTravelStateByEntity.ContainsKey(entityId))
                return;

            if (Time.time < GetNextAllowedShotTime(_nextTransferCheckTimeByEntity, entityId))
                return;

            SetNextAllowedShotTime(_nextTransferCheckTimeByEntity, entityId, Mathf.Max(1f, checkIntervalSeconds));
            if (Random.value > Mathf.Clamp01(chancePerCheck))
                return;

            if (!TryPickRandomTargetPlanet(orbit.OrbitCenter, out var targetCenter, out var targetSurfaceRadius))
                return;

            orbit.SetOrbitCenter(null, orbit.SurfaceRadiusUnits);
            _activeTravelStateByEntity[entityId] = new NpcTravelState(
                entity,
                orbit,
                targetCenter,
                targetSurfaceRadius,
                Mathf.Max(0.1f, transferSpeedUnitsPerSecond),
                Mathf.Max(0.05f, arrivalThresholdUnits));
        }

        private void UpdateActiveTravels()
        {
            if (_activeTravelStateByEntity.Count == 0)
                return;

            _travelEntityIdsBuffer.Clear();
            foreach (var pair in _activeTravelStateByEntity)
                _travelEntityIdsBuffer.Add(pair.Key);

            for (var i = 0; i < _travelEntityIdsBuffer.Count; i++)
            {
                var entityId = _travelEntityIdsBuffer[i];
                if (!_activeTravelStateByEntity.TryGetValue(entityId, out var state))
                    continue;

                if (!UpdateSingleTravel(state))
                    _activeTravelStateByEntity.Remove(entityId);
            }
        }

        private static bool UpdateSingleTravel(NpcTravelState state)
        {
            if (state.EntityObject == null || state.Orbit == null || state.TargetCenter == null)
                return false;

            var center = (Vector2)state.TargetCenter.position;
            var position = (Vector2)state.EntityObject.transform.position;
            var toEntity = position - center;
            if (toEntity.sqrMagnitude <= 0.000001f)
                toEntity = Vector2.right;

            var desiredOrbitRadius = Mathf.Max(0.01f, state.TargetSurfaceRadiusUnits + state.Orbit.EffectiveAltitudeFromSurface);
            var desiredPosition = center + toEntity.normalized * desiredOrbitRadius;
            var nextPosition = Vector2.MoveTowards(position, desiredPosition, Mathf.Max(0.01f, state.TransferSpeedUnitsPerSecond) * Time.deltaTime);
            var movementDirection = nextPosition - position;

            state.EntityObject.transform.position = new Vector3(nextPosition.x, nextPosition.y, state.EntityObject.transform.position.z);
            state.Orbit.AlignXAxisToWorldDirection(movementDirection);

            if ((desiredPosition - nextPosition).sqrMagnitude > state.ArrivalThresholdUnits * state.ArrivalThresholdUnits)
                return true;

            state.Orbit.SetOrbitCenter(state.TargetCenter, state.TargetSurfaceRadiusUnits);
            state.Orbit.SetCurrentAngleDeg(Mathf.Atan2(nextPosition.y - center.y, nextPosition.x - center.x) * Mathf.Rad2Deg);
            state.Orbit.SnapToOrbitPosition();
            return false;
        }

        private bool TryPickRandomTargetPlanet(Transform currentCenter, out Transform targetCenter, out float targetSurfaceRadius)
        {
            targetCenter = null;
            targetSurfaceRadius = 0f;

            if (_worldManager == null)
                return false;

            _planetsBuffer.Clear();
            if (_worldManager.GetActivePlanets(_planetsBuffer) <= 1)
                return false;

            var candidateCount = 0;
            for (var i = 0; i < _planetsBuffer.Count; i++)
            {
                var binding = _planetsBuffer[i];
                if (!binding.generator || binding.generator.transform == currentCenter)
                    continue;

                candidateCount++;
            }

            if (candidateCount == 0)
                return false;

            var selectedIndex = Random.Range(0, candidateCount);
            var currentCandidate = 0;
            for (var i = 0; i < _planetsBuffer.Count; i++)
            {
                var binding = _planetsBuffer[i];
                if (!binding.generator || binding.generator.transform == currentCenter)
                    continue;

                if (currentCandidate != selectedIndex)
                {
                    currentCandidate++;
                    continue;
                }

                targetCenter = binding.generator.transform;
                targetSurfaceRadius = Mathf.Max(0f, binding.generator.EstimatedOuterRadiusUnits);
                return true;
            }

            return false;
        }

        private void TryMineBySurfaceUnderEntity(
            GameObject entity,
            PlanetOrbitMovement orbit,
            CharacterProjectileShooter shooter,
            EntityProjectileStock stock,
            EntityMaterialInventory inventory,
            float mineShotIntervalSeconds,
            LayerMask probeMask,
            float probeDistance,
            bool autoCraftDrillAmmo)
        {
            if (!entity || !orbit || !shooter || !stock || orbit.OrbitCenter == null)
                return;

            if (!TryGetSegmentMaterialUnderEntity(orbit, probeMask, probeDistance, out var segmentMaterial))
                return;

            if (!TryResolveDrillAmmoType(segmentMaterial, out var drillType))
                return;

            var entityId = entity.GetInstanceID();
            if (Time.time < GetNextAllowedShotTime(_nextMineShotTimeByEntity, entityId))
                return;

            if (!EnsureDrillAmmo(stock, inventory, drillType, autoCraftDrillAmmo))
                return;

            var fired = ShootDrill(shooter, drillType);
            if (!fired)
                return;

            if (!ConsumeDrillAmmo(stock, drillType))
                return;

            SetNextAllowedShotTime(_nextMineShotTimeByEntity, entityId, mineShotIntervalSeconds);
        }

        private static bool TryGetCommonComponents(
            GameObject entity,
            out PlanetOrbitMovement orbit,
            out CharacterProjectileShooter shooter,
            out EntityProjectileStock stock,
            out EntityMaterialInventory inventory)
        {
            orbit = null;
            shooter = null;
            stock = null;
            inventory = null;

            if (!entity)
                return false;

            orbit = entity.GetComponent<PlanetOrbitMovement>();
            shooter = entity.GetComponent<CharacterProjectileShooter>();
            stock = entity.GetComponent<EntityProjectileStock>();
            inventory = entity.GetComponent<EntityMaterialInventory>();
            return orbit && shooter && stock && inventory;
        }

        private static bool TryGetSegmentMaterialUnderEntity(
            PlanetOrbitMovement orbit,
            LayerMask probeMask,
            float probeDistance,
            out PlanetSegmentMaterial material)
        {
            material = PlanetSegmentMaterial.Stone;
            if (!orbit || orbit.OrbitCenter == null)
                return false;

            var origin = (Vector2)orbit.transform.position;
            var towardCenter = (Vector2)orbit.OrbitCenter.position - origin;
            if (towardCenter.sqrMagnitude <= 0.000001f)
                return false;

            var hits = Physics2D.RaycastAll(
                origin,
                towardCenter.normalized,
                Mathf.Max(1f, probeDistance),
                probeMask);

            for (var i = 0; i < hits.Length; i++)
            {
                var collider = hits[i].collider;
                if (!collider)
                    continue;

                var segment = collider.GetComponentInParent<PlanetSegment>();
                if (segment == null || segment.IsDestroyed)
                    continue;

                if (!segment.transform.IsChildOf(orbit.OrbitCenter))
                    continue;

                material = segment.Material;
                return true;
            }

            return false;
        }

        private static bool TryResolveDrillAmmoType(PlanetSegmentMaterial material, out DrillAmmoType ammoType)
        {
            switch (material)
            {
                case PlanetSegmentMaterial.IronOre:
                    ammoType = DrillAmmoType.Type1;
                    return true;
                case PlanetSegmentMaterial.CobaltOre:
                    ammoType = DrillAmmoType.Type2;
                    return true;
                case PlanetSegmentMaterial.TitaniumOre:
                    ammoType = DrillAmmoType.Type3;
                    return true;
                case PlanetSegmentMaterial.Stone:
                    ammoType = DrillAmmoType.Type1;
                    return true;
                default:
                    ammoType = DrillAmmoType.Type1;
                    return false;
            }
        }

        private static bool EnsureDrillAmmo(
            EntityProjectileStock stock,
            EntityMaterialInventory inventory,
            DrillAmmoType ammoType,
            bool autoCraft)
        {
            switch (ammoType)
            {
                case DrillAmmoType.Type1:
                    if (stock.DrillType1 > 0)
                        return true;
                    return autoCraft && stock.TryCreateDrillType1();
                case DrillAmmoType.Type2:
                    if (stock.DrillType2 > 0)
                        return true;
                    return autoCraft && stock.TryCreateDrillType2(inventory);
                case DrillAmmoType.Type3:
                    if (stock.DrillType3 > 0)
                        return true;
                    return autoCraft && stock.TryCreateDrillType3(inventory);
                default:
                    return false;
            }
        }

        private static bool ConsumeDrillAmmo(EntityProjectileStock stock, DrillAmmoType ammoType)
        {
            switch (ammoType)
            {
                case DrillAmmoType.Type1:
                    return stock.TryConsumeDrillType1();
                case DrillAmmoType.Type2:
                    return stock.TryConsumeDrillType2();
                case DrillAmmoType.Type3:
                    return stock.TryConsumeDrillType3();
                default:
                    return false;
            }
        }

        private static bool ShootDrill(CharacterProjectileShooter shooter, DrillAmmoType ammoType)
        {
            switch (ammoType)
            {
                case DrillAmmoType.Type1:
                    return shooter.ShootDrillType1();
                case DrillAmmoType.Type2:
                    return shooter.ShootDrillType2();
                case DrillAmmoType.Type3:
                    return shooter.ShootDrillType3();
                default:
                    return false;
            }
        }

        private static bool UpdateOrbitCourseTowardsTarget(PlanetOrbitMovement orbit, Vector3 targetPosition, float aimToleranceDeg)
        {
            if (!orbit || orbit.OrbitCenter == null)
                return false;

            var toTarget = (Vector2)(targetPosition - orbit.OrbitCenter.position);
            if (toTarget.sqrMagnitude <= 0.000001f)
                return false;

            var targetAngleDeg = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            var deltaDeg = Mathf.DeltaAngle(orbit.CurrentAngleDeg, targetAngleDeg);

            if (deltaDeg > 0f)
                orbit.SetRotationDirection(OrbitRotationDirection.CounterClockwise);
            else if (deltaDeg < 0f)
                orbit.SetRotationDirection(OrbitRotationDirection.Clockwise);

            return Mathf.Abs(deltaDeg) <= Mathf.Max(0.5f, aimToleranceDeg);
        }

        private static bool IsOrbitAlignedToTarget(PlanetOrbitMovement orbit, Vector3 targetPosition, float aimToleranceDeg)
        {
            if (!orbit || orbit.OrbitCenter == null)
                return false;

            var toTarget = (Vector2)(targetPosition - orbit.OrbitCenter.position);
            if (toTarget.sqrMagnitude <= 0.000001f)
                return false;

            var targetAngleDeg = Mathf.Atan2(toTarget.y, toTarget.x) * Mathf.Rad2Deg;
            var deltaDeg = Mathf.DeltaAngle(orbit.CurrentAngleDeg, targetAngleDeg);
            return Mathf.Abs(deltaDeg) <= Mathf.Max(0.5f, aimToleranceDeg);
        }

        private static Vector3 GetVillainAimPoint(Vector3 shooterPosition, Vector3 targetPosition, EntityVillainTag tag)
        {
            if (tag == null)
                return targetPosition;

            if (Random.value > Mathf.Clamp01(tag.AttackMissChance))
                return targetPosition;

            var toTarget = targetPosition - shooterPosition;
            if (toTarget.sqrMagnitude <= 0.000001f)
                return targetPosition;

            var missMin = Mathf.Abs(tag.AttackMissAngleMinDeg);
            var missMax = Mathf.Max(missMin, Mathf.Abs(tag.AttackMissAngleMaxDeg));
            var missAngle = Random.Range(missMin, missMax);
            if (Random.value < 0.5f)
                missAngle = -missAngle;

            var rotatedDirection = Quaternion.Euler(0f, 0f, missAngle) * toTarget.normalized;
            return shooterPosition + rotatedDirection * toTarget.magnitude;
        }

        private static bool TryFindTargetOnSamePlanet(
            Transform villainTransform,
            PlanetOrbitMovement villainOrbit,
            EntityPeacefulTag[] peacefuls,
            EntityHeroTag[] heroes,
            out Transform targetTransform,
            out PlanetOrbitMovement targetOrbit)
        {
            targetTransform = null;
            targetOrbit = null;

            if (!villainTransform || !villainOrbit || villainOrbit.OrbitCenter == null)
                return false;

            var center = villainOrbit.OrbitCenter;
            var bestDistanceSqr = float.MaxValue;

            for (var i = 0; i < heroes.Length; i++)
            {
                var hero = heroes[i];
                if (!hero)
                    continue;

                var orbit = hero.GetComponent<PlanetOrbitMovement>();
                if (!orbit || orbit.OrbitCenter != center)
                    continue;

                var distanceSqr = (hero.transform.position - villainTransform.position).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                    continue;

                bestDistanceSqr = distanceSqr;
                targetTransform = hero.transform;
                targetOrbit = orbit;
            }

            for (var i = 0; i < peacefuls.Length; i++)
            {
                var peaceful = peacefuls[i];
                if (!peaceful || peaceful.transform == villainTransform)
                    continue;

                var orbit = peaceful.GetComponent<PlanetOrbitMovement>();
                if (!orbit || orbit.OrbitCenter != center)
                    continue;

                var distanceSqr = (peaceful.transform.position - villainTransform.position).sqrMagnitude;
                if (distanceSqr >= bestDistanceSqr)
                    continue;

                bestDistanceSqr = distanceSqr;
                targetTransform = peaceful.transform;
                targetOrbit = orbit;
            }

            return targetTransform != null;
        }

        private static float GetNextAllowedShotTime(System.Collections.Generic.Dictionary<int, float> cooldowns, int entityId)
        {
            return cooldowns.TryGetValue(entityId, out var time) ? time : 0f;
        }

        private static void SetNextAllowedShotTime(System.Collections.Generic.Dictionary<int, float> cooldowns, int entityId, float intervalSeconds)
        {
            cooldowns[entityId] = Time.time + Mathf.Max(0.1f, intervalSeconds);
        }

        private enum DrillAmmoType
        {
            Type1 = 1,
            Type2 = 2,
            Type3 = 3
        }

        private readonly struct NpcTravelState
        {
            public readonly GameObject EntityObject;
            public readonly PlanetOrbitMovement Orbit;
            public readonly Transform TargetCenter;
            public readonly float TargetSurfaceRadiusUnits;
            public readonly float TransferSpeedUnitsPerSecond;
            public readonly float ArrivalThresholdUnits;

            public NpcTravelState(
                GameObject entityObject,
                PlanetOrbitMovement orbit,
                Transform targetCenter,
                float targetSurfaceRadiusUnits,
                float transferSpeedUnitsPerSecond,
                float arrivalThresholdUnits)
            {
                EntityObject = entityObject;
                Orbit = orbit;
                TargetCenter = targetCenter;
                TargetSurfaceRadiusUnits = targetSurfaceRadiusUnits;
                TransferSpeedUnitsPerSecond = transferSpeedUnitsPerSecond;
                ArrivalThresholdUnits = arrivalThresholdUnits;
            }
        }
    }
}
