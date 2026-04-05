using App.Infrastructure.DI;
using App.Infrastructure.DI.Base;
using App.Planets.Generation;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace App.Entities
{
    public interface IGameplayPlanetSwitchRuntimeService
    {
        void SetAimDirection(Vector2 aimDirection);
        bool TrySwitchToSelectedPlanet();
        void ToggleOrbitDirection();
    }

    public class HeroPlanetSwitchRuntimeService : IGameService, IGameplayPlanetSwitchRuntimeService, ITickable
    {
        private const float DefaultMaxRaycastDistance = 250f;
        private const float AimDirectionThresholdSqr = 0.01f;
        private const float FallbackTransferSpeed = 22f;
        private const float FallbackArrivalThreshold = 0.25f;

        private readonly IHeroOrbitRuntimeProvider _heroOrbitProvider;
        private PlanetGenerator _selectedPlanet;
        private PlanetGenerator _activeTravelTarget;
        private Vector2 _aimDirection;

        public HeroPlanetSwitchRuntimeService([InjectOptional] IHeroOrbitRuntimeProvider heroOrbitProvider)
        {
            _heroOrbitProvider = heroOrbitProvider;
        }

        public UniTask Initialize()
        {
            _selectedPlanet = null;
            _activeTravelTarget = null;
            _aimDirection = Vector2.zero;
            return UniTask.CompletedTask;
        }

        public void Tick()
        {
            UpdateTravel();
        }

        public void SetAimDirection(Vector2 aimDirection)
        {
            _aimDirection = aimDirection;
            RefreshSelectedPlanet();
        }

        public bool TrySwitchToSelectedPlanet()
        {
            if (_selectedPlanet == null)
                return false;

            var heroOrbit = _heroOrbitProvider?.CurrentHeroOrbitMovement;
            if (!heroOrbit)
                return false;

            StartOrRedirectTravel(_selectedPlanet, heroOrbit);
            RefreshSelectedPlanet();
            return true;
        }

        public void ToggleOrbitDirection()
        {
            var heroOrbit = _heroOrbitProvider?.CurrentHeroOrbitMovement;
            if (!heroOrbit)
                return;

            var nextDirection = heroOrbit.RotationDirection == OrbitRotationDirection.Clockwise
                ? OrbitRotationDirection.CounterClockwise
                : OrbitRotationDirection.Clockwise;
            heroOrbit.SetRotationDirection(nextDirection);
        }

        private void RefreshSelectedPlanet()
        {
            _selectedPlanet = null;

            var hero = _heroOrbitProvider?.CurrentHero;
            var heroOrbit = _heroOrbitProvider?.CurrentHeroOrbitMovement;
            if (!hero || !heroOrbit)
                return;

            if (_aimDirection.sqrMagnitude < AimDirectionThresholdSqr)
                return;

            var origin = (Vector2)hero.transform.position;
            var direction = _aimDirection.normalized;
            var hit = Physics2D.Raycast(origin, direction, DefaultMaxRaycastDistance);
            if (!hit.collider)
                return;

            var hitPlanet = hit.collider.GetComponentInParent<PlanetGenerator>();
            if (!hitPlanet)
                return;

            if (heroOrbit.OrbitCenter == hitPlanet.transform)
                return;

            _selectedPlanet = hitPlanet;
        }

        private void StartOrRedirectTravel(PlanetGenerator targetPlanet, PlanetOrbitMovement heroOrbit)
        {
            if (!targetPlanet || !heroOrbit)
                return;

            _activeTravelTarget = targetPlanet;
            if (heroOrbit.OrbitCenter != null)
                heroOrbit.SetOrbitCenter(null, heroOrbit.SurfaceRadiusUnits);
        }

        private void UpdateTravel()
        {
            if (_activeTravelTarget == null)
                return;

            var hero = _heroOrbitProvider?.CurrentHero;
            var heroOrbit = _heroOrbitProvider?.CurrentHeroOrbitMovement;
            if (!hero || !heroOrbit)
            {
                _activeTravelTarget = null;
                return;
            }

            if (!_activeTravelTarget)
            {
                _activeTravelTarget = null;
                return;
            }

            var center = (Vector2)_activeTravelTarget.transform.position;
            var heroPosition = (Vector2)hero.transform.position;
            var toHero = heroPosition - center;
            if (toHero.sqrMagnitude <= 0.000001f)
                toHero = _aimDirection.sqrMagnitude > AimDirectionThresholdSqr ? _aimDirection.normalized : Vector2.right;

            var desiredOrbitRadius = Mathf.Max(
                0.01f,
                _activeTravelTarget.EstimatedOuterRadiusUnits + Mathf.Max(0f, heroOrbit.AltitudeFromSurface));
            var desiredPosition = center + toHero.normalized * desiredOrbitRadius;

            var speed = Mathf.Max(0.01f, hero.TransferSpeedUnitsPerSecond > 0f
                ? hero.TransferSpeedUnitsPerSecond
                : FallbackTransferSpeed);
            var nextPosition = Vector2.MoveTowards(heroPosition, desiredPosition, speed * Time.deltaTime);
            var movementDirection = nextPosition - heroPosition;
            hero.transform.position = new Vector3(nextPosition.x, nextPosition.y, hero.transform.position.z);
            heroOrbit.AlignXAxisToWorldDirection(movementDirection);

            var arrivalThreshold = Mathf.Max(0.01f, hero.TransferArrivalThresholdUnits > 0f
                ? hero.TransferArrivalThresholdUnits
                : FallbackArrivalThreshold);
            if ((desiredPosition - nextPosition).sqrMagnitude > arrivalThreshold * arrivalThreshold)
                return;

            heroOrbit.SetOrbitCenter(_activeTravelTarget.transform, _activeTravelTarget.EstimatedOuterRadiusUnits);
            heroOrbit.SetCurrentAngleDeg(Mathf.Atan2(nextPosition.y - center.y, nextPosition.x - center.x) * Mathf.Rad2Deg);
            heroOrbit.SnapToOrbitPosition();
            _activeTravelTarget = null;
        }
    }
}
