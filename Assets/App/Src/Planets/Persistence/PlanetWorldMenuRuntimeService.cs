using System;
using App.Entities;
using App.Infrastructure.DI.Base;
using App.Signals;
using Cysharp.Threading.Tasks;
using Zenject;

namespace App.Planets.Persistence
{
    public class PlanetWorldMenuRuntimeService : IGameService, IDisposable, ITickable
    {
        private enum UiState
        {
            MainMenu,
            InGame
        }

        private readonly PlanetWorldMenuController _context;
        private readonly IGameplayCameraZoomRuntimeService _cameraZoomService;
        private readonly IGameplayPlanetSwitchRuntimeService _planetSwitchService;
        private readonly IGameplayHeroFlightAltitudeRuntimeService _flightAltitudeService;
        private bool _operationInProgress;

        [Inject] private SignalBus _signalBus;
        private readonly WorldCharacterSpawnRuntimeService _characterSpawnRuntimeService;
        private readonly WorldLooseObjectsPersistenceService _worldLooseObjectsPersistenceService;

        public PlanetWorldMenuRuntimeService(
            PlanetWorldMenuController context,
            [InjectOptional] IGameplayCameraZoomRuntimeService cameraZoomService,
            [InjectOptional] IGameplayPlanetSwitchRuntimeService planetSwitchService,
            [InjectOptional] IGameplayHeroFlightAltitudeRuntimeService flightAltitudeService,
            WorldCharacterSpawnRuntimeService characterSpawnRuntimeService,
            [InjectOptional] WorldLooseObjectsPersistenceService worldLooseObjectsPersistenceService)
        {
            _characterSpawnRuntimeService = characterSpawnRuntimeService;
            _worldLooseObjectsPersistenceService = worldLooseObjectsPersistenceService;
            _context = context;
            _cameraZoomService = cameraZoomService;
            _planetSwitchService = planetSwitchService;
            _flightAltitudeService = flightAltitudeService;
        }

        public UniTask Initialize()
        {
            if (_context == null || _context.WorldManager == null)
                return UniTask.CompletedTask;

            _characterSpawnRuntimeService.HeroSpawned += OnHeroSpawned;
            if (_context.MenuCanvasController != null)
            {
                _context.MenuCanvasController.Initialize(
                    OnNewGameRequested,
                    OnLoadWorldRequested,
                    _context.DefaultNewGameWorldSize);
                _context.MenuCanvasController.RefreshWorldList();
            }

            _context.GameplayCanvasController?.Initialize(
                OnExitWorldRequested,
                OnZoomInRequested,
                OnZoomOutRequested,
                OnIncreaseFlightAltitudeRequested,
                OnDecreaseFlightAltitudeRequested,
                OnToggleOrbitDirectionRequested,
                OnShootRocketRequested,
                OnCreateRocketRequested,
                OnShootDrillType1Requested,
                OnCreateDrillType1Requested,
                OnShootDrillType2Requested,
                OnCreateDrillType2Requested,
                OnShootDrillType3Requested,
                OnCreateDrillType3Requested,
                OnAimDirectionChanged,
                OnAimReleased);

            ApplyUiState(UiState.MainMenu);
            SyncBusyState();
            UpdateLoadingProgress();


            return UniTask.CompletedTask;
        }

        private void OnHeroSpawned(EntityHeroTag obj)
        {
            obj.GetComponent<HeroAimRayVisualizer>()?.SetAimJoystick(_context.GameplayCanvasController.AimJoystick);
            SyncGameplayResourceAndProjectileCounts();
        }

        public UniTask PostInitialize()
        {
            _signalBus.Fire(new UiCreatedSignal { Joystick = _context.GameplayCanvasController!.AimJoystick });
            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _characterSpawnRuntimeService.HeroSpawned -= OnHeroSpawned;
            _context?.MenuCanvasController?.Release();
            _context?.GameplayCanvasController?.Release();
        }

        public void Tick()
        {
            if (!_context || !_context.WorldManager)
                return;

            SyncBusyState();
            UpdateLoadingProgress();
            SyncGameplayResourceAndProjectileCounts();
        }

        private void OnNewGameRequested(PlanetWorldManager.WorldSize worldSize)
        {
            _characterSpawnRuntimeService?.SaveCurrentWorldEntitiesStateNow();
            _worldLooseObjectsPersistenceService?.SaveCurrentWorldObjectsStateNow();

            RequestWorldOperation(
                async () =>
                {
                    var worldId = BuildNewWorldId();
                    await _context.WorldManager.CreateWorldAsync(worldId, worldSize);
                },
                UiState.InGame,
                refreshWorldListAfterCompletion: false);
        }

        private void OnLoadWorldRequested(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                return;

            _characterSpawnRuntimeService?.SaveCurrentWorldEntitiesStateNow();
            _worldLooseObjectsPersistenceService?.SaveCurrentWorldObjectsStateNow();

            RequestWorldOperation(
                () => _context.WorldManager.LoadWorldAsync(worldId, saveAndUnloadCurrent: true),
                UiState.InGame,
                refreshWorldListAfterCompletion: false);
        }

        private void OnExitWorldRequested()
        {
            _characterSpawnRuntimeService?.SaveCurrentWorldEntitiesStateNow();
            _worldLooseObjectsPersistenceService?.SaveCurrentWorldObjectsStateNow();
            SyncGameplayResourceAndProjectileCounts();

            RequestWorldOperation(
                () => _context.WorldManager.UnloadCurrentWorldAsync(_context.SaveBeforeUnloadCurrentWorld),
                UiState.MainMenu,
                refreshWorldListAfterCompletion: true);
        }

        private void OnZoomInRequested()
        {
            _cameraZoomService?.ZoomIn();
        }

        private void OnZoomOutRequested()
        {
            _cameraZoomService?.ZoomOut();
        }

        private void OnIncreaseFlightAltitudeRequested()
        {
            _flightAltitudeService?.IncreaseFlightAltitude();
        }

        private void OnDecreaseFlightAltitudeRequested()
        {
            _flightAltitudeService?.DecreaseFlightAltitude();
        }

        private void OnToggleOrbitDirectionRequested()
        {
            _planetSwitchService?.ToggleOrbitDirection();
        }

        private void OnAimDirectionChanged(UnityEngine.Vector2 aimDirection)
        {
            _planetSwitchService?.SetAimDirection(aimDirection);
        }

        private void OnAimReleased()
        {
            _planetSwitchService?.TrySwitchToSelectedPlanet();
        }

        private void OnShootRocketRequested()
        {
            TryShootIfAvailable(
                stock => stock.Rockets > 0,
                shooter => shooter.ShootRocket(),
                stock => stock.TryConsumeRocket());
        }

        private void OnCreateRocketRequested()
        {
            TryCreateProjectile((stock, inventory) => stock.TryCreateRocket(inventory));
        }

        private void OnShootDrillType1Requested()
        {
            TryShootIfAvailable(
                stock => stock.DrillType1 > 0,
                shooter => shooter.ShootDrillType1(),
                stock => stock.TryConsumeDrillType1());
        }

        private void OnCreateDrillType1Requested()
        {
            TryCreateProjectile((stock, _) => stock.TryCreateDrillType1());
        }

        private void OnShootDrillType2Requested()
        {
            TryShootIfAvailable(
                stock => stock.DrillType2 > 0,
                shooter => shooter.ShootDrillType2(),
                stock => stock.TryConsumeDrillType2());
        }

        private void OnCreateDrillType2Requested()
        {
            TryCreateProjectile((stock, inventory) => stock.TryCreateDrillType2(inventory));
        }

        private void OnShootDrillType3Requested()
        {
            TryShootIfAvailable(
                stock => stock.DrillType3 > 0,
                shooter => shooter.ShootDrillType3(),
                stock => stock.TryConsumeDrillType3());
        }

        private void OnCreateDrillType3Requested()
        {
            TryCreateProjectile((stock, inventory) => stock.TryCreateDrillType3(inventory));
        }

        private void TryShootIfAvailable(
            Func<EntityProjectileStock, bool> hasAmmo,
            Func<CharacterProjectileShooter, bool> shootAction,
            Func<EntityProjectileStock, bool> consumeAction)
        {
            if (hasAmmo == null || shootAction == null || consumeAction == null)
                return;

            var hero = _characterSpawnRuntimeService.CurrentHero;
            if (!hero)
                return;

            var stock = hero.GetComponent<EntityProjectileStock>();
            var shooter = hero.GetComponent<CharacterProjectileShooter>();
            if (!stock || !shooter)
                return;

            if (!hasAmmo(stock))
                return;

            if (!shootAction(shooter))
                return;

            if (consumeAction(stock))
                SyncGameplayResourceAndProjectileCounts();
        }

        private void TryCreateProjectile(Func<EntityProjectileStock, EntityMaterialInventory, bool> createAction)
        {
            if (createAction == null)
                return;

            var hero = _characterSpawnRuntimeService.CurrentHero;
            if (!hero)
                return;

            var stock = hero.GetComponent<EntityProjectileStock>();
            var inventory = hero.GetComponent<EntityMaterialInventory>();
            if (!stock || !inventory)
                return;

            if (createAction(stock, inventory))
                SyncGameplayResourceAndProjectileCounts();
        }

        private void RequestWorldOperation(Func<UniTask> operation, UiState stateOnComplete,
            bool refreshWorldListAfterCompletion)
        {
            if (operation == null || _context?.WorldManager == null || _operationInProgress ||
                _context.WorldManager.IsBusy)
                return;

            ExecuteWorldOperationAsync(operation, stateOnComplete, refreshWorldListAfterCompletion).Forget();
        }

        private async UniTaskVoid ExecuteWorldOperationAsync(
            Func<UniTask> operation,
            UiState stateOnComplete,
            bool refreshWorldListAfterCompletion)
        {
            _operationInProgress = true;
            SetCanvasInteractable(false);
            SyncBusyState();
            UpdateLoadingProgress();

            try
            {
                await operation.Invoke();

                if (refreshWorldListAfterCompletion)
                    _context.MenuCanvasController?.RefreshWorldList();

                ApplyUiState(stateOnComplete);
                SyncGameplayResourceAndProjectileCounts();
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogException(exception);
            }
            finally
            {
                _operationInProgress = false;
                SetCanvasInteractable(true);
                SyncBusyState();
                UpdateLoadingProgress();
            }
        }

        private void ApplyUiState(UiState state)
        {
            var showMainMenu = state == UiState.MainMenu;
            var showInGame = state == UiState.InGame;

            _context.MenuCanvasController?.SetVisible(showMainMenu);
            _context.GameplayCanvasController?.SetVisible(showInGame);

            if (showMainMenu)
                _context.GameplayCanvasController?.ResetResourceAndProjectileCounts();
        }

        private void SyncGameplayResourceAndProjectileCounts()
        {
            var gameplayCanvas = _context?.GameplayCanvasController;
            if (gameplayCanvas == null)
                return;

            var hero = _characterSpawnRuntimeService?.CurrentHero;
            if (!hero)
            {
                gameplayCanvas.ResetResourceAndProjectileCounts();
                return;
            }

            var stock = hero.GetComponent<EntityProjectileStock>();
            gameplayCanvas.SetProjectileCounts(
                stock ? stock.Rockets : 0,
                stock ? stock.DrillType1 : 0,
                stock ? stock.DrillType2 : 0,
                stock ? stock.DrillType3 : 0);

            var inventory = hero.GetComponent<EntityMaterialInventory>();
            gameplayCanvas.SetResourceCounts(
                inventory ? inventory.MagmaPoints : 0,
                inventory ? inventory.Metal1Points : 0,
                inventory ? inventory.Metal2Points : 0,
                inventory ? inventory.Metal3Points : 0);
        }

        private void SetCanvasInteractable(bool isInteractable)
        {
            _context.MenuCanvasController?.SetInteractable(isInteractable);
            _context.GameplayCanvasController?.SetInteractable(isInteractable);
        }

        private void SyncBusyState()
        {
            var isBusy = _context.WorldManager != null && _context.WorldManager.IsBusy;
            _context.LoadingCanvasController?.SetVisible(isBusy);
            SetCanvasInteractable(!isBusy);
        }

        private void UpdateLoadingProgress()
        {
            if (_context.LoadingCanvasController == null)
                return;

            var isLoadingWorld = _context.WorldManager != null && _context.WorldManager.IsLoadingWorld;
            var progress = _context.WorldManager != null ? _context.WorldManager.CurrentWorldLoadProgress01 : 0f;
            _context.LoadingCanvasController.SetProgress(isLoadingWorld, progress);
        }

        private string BuildNewWorldId()
        {
            var prefix = string.IsNullOrWhiteSpace(_context.NewWorldIdPrefix)
                ? "world"
                : _context.NewWorldIdPrefix.Trim();
            return $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
        }
    }
}
