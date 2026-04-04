using System;
using App.Entities;
using App.Infrastructure.DI;
using App.Planets.GfxGen.Persistence;
using Cysharp.Threading.Tasks;
using Zenject;

namespace App.Planets.GfxGen
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
        private bool _operationInProgress;

        public PlanetWorldMenuRuntimeService(
            PlanetWorldMenuController context,
            [InjectOptional] IGameplayCameraZoomRuntimeService cameraZoomService,
            [InjectOptional] IGameplayPlanetSwitchRuntimeService planetSwitchService)
        {
            _context = context;
            _cameraZoomService = cameraZoomService;
            _planetSwitchService = planetSwitchService;
        }

        public UniTask Initialize()
        {
            if (_context == null || _context.WorldManager == null)
                return UniTask.CompletedTask;

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
                OnToggleOrbitDirectionRequested,
                OnAimDirectionChanged,
                OnAimReleased);

            ApplyUiState(UiState.MainMenu);
            SyncBusyState();
            UpdateLoadingProgress();

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            _context?.MenuCanvasController?.Release();
            _context?.GameplayCanvasController?.Release();
        }

        public void Tick()
        {
            if (_context == null || _context.WorldManager == null)
                return;

            SyncBusyState();
            UpdateLoadingProgress();
        }

        private void OnNewGameRequested(PlanetWorldManager.WorldSize worldSize)
        {
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

            RequestWorldOperation(
                () => _context.WorldManager.LoadWorldAsync(worldId, saveAndUnloadCurrent: true),
                UiState.InGame,
                refreshWorldListAfterCompletion: false);
        }

        private void OnExitWorldRequested()
        {
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

        private void RequestWorldOperation(Func<UniTask> operation, UiState stateOnComplete, bool refreshWorldListAfterCompletion)
        {
            if (operation == null || _context?.WorldManager == null || _operationInProgress || _context.WorldManager.IsBusy)
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
            var prefix = string.IsNullOrWhiteSpace(_context.NewWorldIdPrefix) ? "world" : _context.NewWorldIdPrefix.Trim();
            return $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
        }
    }
}
