using System;
using System.Threading;
using App.Entities.Config;
using App.Infrastructure.DI;
using App.Infrastructure.DI.Base;
using App.Planets.Generation;
using Cysharp.Threading.Tasks;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Zenject;

namespace App.Entities
{
    public interface IGameplayCameraZoomRuntimeService
    {
        void ZoomIn();
        void ZoomOut();
    }

    public class HeroPlanetCinemachineCameraRuntimeService : IGameService, IDisposable, IGameplayCameraZoomRuntimeService
    {
        private readonly IHeroOrbitRuntimeProvider _heroOrbitProvider;

        private EntityHeroTag _currentHero;
        private PlanetOrbitMovement _heroOrbitMovement;
        private Transform _desiredTarget;
        private CinemachineCamera _camera;
        private Transform _followProxy;
        private CancellationTokenSource _updateLoopCts;
        private Vector3 _proxyVelocity;
        private float _screenXVelocity;
        private float _orthographicSizeVelocity;
        private float _currentScreenX;
        private float _desiredScreenX;
        private float? _desiredOrthographicSize;
        private bool _hasManualZoomOverride;
        private bool _isProxyInitialized;
        [Inject] private HeroPlanetCinemachineCameraConfig _config;

        public HeroPlanetCinemachineCameraRuntimeService(
            [InjectOptional] IHeroOrbitRuntimeProvider heroOrbitProvider)
        {
            _heroOrbitProvider = heroOrbitProvider;
        }

        public async UniTask Initialize()
        {
            _camera = (await Addressables.InstantiateAsync(_config.cameraAssetReference))
                .GetComponent<CinemachineCamera>();
            _hasManualZoomOverride = false;
            _followProxy = new GameObject("HeroPlanetCameraFollowProxy").transform;
            _isProxyInitialized = false;
            _desiredTarget = null;
            _desiredScreenX = 0f;
            _currentScreenX = 0f;
            _desiredOrthographicSize = null;

            if (_camera)
            {
                _camera.Follow = _followProxy;
                _camera.LookAt = _followProxy;
            }

            _updateLoopCts = new CancellationTokenSource();
            RunCameraUpdateLoop(_updateLoopCts.Token).Forget();

            if (_heroOrbitProvider == null)
                return;

            _heroOrbitProvider.HeroOrbitChanged += OnHeroOrbitChanged;
            OnHeroOrbitChanged(_heroOrbitProvider.CurrentHero, _heroOrbitProvider.CurrentHeroOrbitMovement);
        }

        public void Dispose()
        {
            if (_heroOrbitProvider != null)
                _heroOrbitProvider.HeroOrbitChanged -= OnHeroOrbitChanged;

            SubscribeToOrbitCenterEvents(null);
            if (_updateLoopCts != null)
            {
                _updateLoopCts.Cancel();
                _updateLoopCts.Dispose();
                _updateLoopCts = null;
            }

            if (_followProxy != null)
            {
                UnityEngine.Object.Destroy(_followProxy.gameObject);
                _followProxy = null;
            }
        }

        private void OnHeroOrbitChanged(EntityHeroTag hero, PlanetOrbitMovement heroOrbit)
        {
            _currentHero = hero;
            SubscribeToOrbitCenterEvents(heroOrbit);

            var orbitCenter = heroOrbit ? heroOrbit.OrbitCenter : null;
            ApplyGameplayComposition(orbitCenter, heroOrbit);
        }

        private void SubscribeToOrbitCenterEvents(PlanetOrbitMovement heroOrbit)
        {
            if (_heroOrbitMovement == heroOrbit)
                return;

            if (_heroOrbitMovement)
                _heroOrbitMovement.OrbitCenterChanged -= OnHeroOrbitCenterChanged;

            _heroOrbitMovement = heroOrbit;

            if (_heroOrbitMovement)
                _heroOrbitMovement.OrbitCenterChanged += OnHeroOrbitCenterChanged;
        }

        private void OnHeroOrbitCenterChanged(Transform orbitCenter)
        {
            ApplyGameplayComposition(orbitCenter, _heroOrbitMovement);
        }

        private void ApplyGameplayComposition(Transform orbitCenter, PlanetOrbitMovement heroOrbit)
        {
            _desiredTarget = orbitCenter ? orbitCenter : (_currentHero ? _currentHero.transform : null);
            _desiredScreenX = ResolveDesiredScreenX(orbitCenter);
            _desiredOrthographicSize = ResolveDesiredOrthographicSize(orbitCenter, heroOrbit);
        }

        private float FitPlanetToGameplayArea(
            CinemachineCamera camera,
            Transform planetTransform,
            PlanetOrbitMovement heroOrbit)
        {
            var lens = camera.Lens;
            if (!lens.Orthographic)
                return lens.OrthographicSize;

            var planetRadius = ResolvePlanetRadius(planetTransform, heroOrbit);
            if (planetRadius <= 0.0001f)
                return lens.OrthographicSize;

            var gameplayWidthFraction = Mathf.Clamp(1f - _config.rightUiWidthFraction, 0.1f, 1f);
            var aspect = Mathf.Max(0.01f, lens.Aspect);

            var requiredByHeight = planetRadius;
            var requiredByWidth = planetRadius / Mathf.Max(0.01f, aspect * gameplayWidthFraction);
            var targetSize = Mathf.Max(requiredByHeight, requiredByWidth) *
                             Mathf.Max(1f, _config.planetFitPaddingMultiplier);

            return Mathf.Clamp(targetSize, _config.minOrthographicSize, _config.maxOrthographicSize);
        }

        private static float ResolvePlanetRadius(Transform planetTransform, PlanetOrbitMovement heroOrbit)
        {
            if (heroOrbit && heroOrbit.OrbitCenter == planetTransform)
                return Mathf.Max(0f, heroOrbit.SurfaceRadiusUnits);

            var generator = planetTransform.GetComponent<PlanetGenerator>();
            return generator ? Mathf.Max(0f, generator.EstimatedOuterRadiusUnits) : 0f;
        }

        public void ZoomIn()
        {
            ApplyZoom(1f / Mathf.Max(1.01f, _config.zoomStepMultiplier));
        }

        public void ZoomOut()
        {
            ApplyZoom(Mathf.Max(1.01f, _config.zoomStepMultiplier));
        }

        private void ApplyZoom(float scaleMultiplier)
        {
            if (!_camera)
                return;

            var lens = _camera.Lens;
            if (!lens.Orthographic)
                return;

            var nextSize = lens.OrthographicSize * Mathf.Max(0.01f, scaleMultiplier);
            lens.OrthographicSize = Mathf.Clamp(nextSize, _config.minOrthographicSize, _config.maxOrthographicSize);
            _camera.Lens = lens;
            _hasManualZoomOverride = true;
            _desiredOrthographicSize = lens.OrthographicSize;
        }

        private async UniTaskVoid RunCameraUpdateLoop(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    UpdateCameraState(Time.deltaTime);
                    await UniTask.Yield(PlayerLoopTiming.PostLateUpdate, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Ignore loop cancellation during dispose.
            }
        }

        private void UpdateCameraState(float deltaTime)
        {
            if (!_camera || _followProxy == null)
                return;

            if (_desiredTarget != null)
            {
                var desiredPosition = _desiredTarget.position;
                if (!_isProxyInitialized)
                {
                    _followProxy.position = desiredPosition;
                    _isProxyInitialized = true;
                }
                else
                {
                    _followProxy.position = Vector3.SmoothDamp(
                        _followProxy.position,
                        desiredPosition,
                        ref _proxyVelocity,
                        0.16f,
                        Mathf.Infinity,
                        deltaTime);
                }
            }
            else
            {
                _isProxyInitialized = false;
                _proxyVelocity = Vector3.zero;
            }

            var positionComposer =
                _camera.GetCinemachineComponent(CinemachineCore.Stage.Body) as CinemachinePositionComposer;
            if (positionComposer)
            {
                _currentScreenX = Mathf.SmoothDamp(_currentScreenX, _desiredScreenX, ref _screenXVelocity, 0.14f, Mathf.Infinity, deltaTime);
                var composition = positionComposer.Composition;
                composition.ScreenPosition.x = _currentScreenX;
                positionComposer.Composition = composition;
            }

            if (_desiredOrthographicSize.HasValue)
            {
                var lens = _camera.Lens;
                if (lens.Orthographic)
                {
                    lens.OrthographicSize = Mathf.SmoothDamp(
                        lens.OrthographicSize,
                        _desiredOrthographicSize.Value,
                        ref _orthographicSizeVelocity,
                        0.18f,
                        Mathf.Infinity,
                        deltaTime);
                    lens.OrthographicSize = Mathf.Clamp(lens.OrthographicSize, _config.minOrthographicSize, _config.maxOrthographicSize);
                    _camera.Lens = lens;
                }
            }
        }

        private float ResolveDesiredScreenX(Transform orbitCenter)
        {
            if (!_config.adjustScreenXForGameplayArea || orbitCenter == null)
                return 0f;

            var gameplayWidthFraction = Mathf.Clamp(1f - _config.rightUiWidthFraction, 0.1f, 1f);
            return gameplayWidthFraction * 0.5f - 0.5f;
        }

        private float? ResolveDesiredOrthographicSize(Transform orbitCenter, PlanetOrbitMovement heroOrbit)
        {
            if (!_camera || !_camera.Lens.Orthographic)
                return null;

            if (!_config.adjustOrthographicSize || orbitCenter == null || _hasManualZoomOverride)
                return null;

            return FitPlanetToGameplayArea(_camera, orbitCenter, heroOrbit);
        }
    }
}
