using System;
using UnityEngine;
using UnityEngine.UI;

namespace App.Planets.Persistence
{
    public class WorldGameplayCanvasController : MonoBehaviour
    {
        [Header("Canvas")]
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private RectTransform panelRoot;

        [Header("Layout")]
        [SerializeField] [Range(0.1f, 0.9f)] private float rightPanelWidthFraction = 1f / 3f;
        [SerializeField] private bool enforceRightPanelLayoutAtRuntime = true;

        [Header("Buttons")]
        [SerializeField] private Button exitCurrentWorldButton;
        [SerializeField] private Button zoomInButton;
        [SerializeField] private Button zoomOutButton;
        [SerializeField] private Button toggleOrbitDirectionButton;
        
        [Header("Aim")]
        [SerializeField] private GameplayAimJoystick aimJoystick;

        private Action _onExitWorldRequested;
        private Action _onZoomInRequested;
        private Action _onZoomOutRequested;
        private Action _onToggleOrbitDirectionRequested;
        private Action<Vector2> _onAimDirectionChanged;
        private Action _onAimReleased;
        private Vector2Int _lastScreenSize;
        public Vector2 AimDirection => aimJoystick ? aimJoystick.Direction : Vector2.zero;
        public GameplayAimJoystick AimJoystick => aimJoystick;

        private void OnEnable()
        {
            BindButtons();
            BindAimJoystick();
            ApplyRightPanelLayout(force: true);
        }

        private void Update()
        {
            ApplyRightPanelLayout(force: false);
        }

        private void OnDisable()
        {
            UnbindButtons();
            UnbindAimJoystick();
        }

        public void Initialize(
            Action onExitWorldRequested,
            Action onZoomInRequested,
            Action onZoomOutRequested,
            Action onToggleOrbitDirectionRequested,
            Action<Vector2> onAimDirectionChanged,
            Action onAimReleased)
        {
            _onExitWorldRequested = onExitWorldRequested;
            _onZoomInRequested = onZoomInRequested;
            _onZoomOutRequested = onZoomOutRequested;
            _onToggleOrbitDirectionRequested = onToggleOrbitDirectionRequested;
            _onAimDirectionChanged = onAimDirectionChanged;
            _onAimReleased = onAimReleased;
            BindButtons();
            BindAimJoystick();
        }

        public void Release()
        {
            UnbindButtons();
            _onExitWorldRequested = null;
            _onZoomInRequested = null;
            _onZoomOutRequested = null;
            _onToggleOrbitDirectionRequested = null;
            _onAimDirectionChanged = null;
            _onAimReleased = null;
            UnbindAimJoystick();
        }

        public void SetVisible(bool isVisible)
        {
            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(isVisible);
        }

        public void SetInteractable(bool isInteractable)
        {
            if (exitCurrentWorldButton != null)
                exitCurrentWorldButton.interactable = isInteractable;
            
            if (zoomInButton != null)
                zoomInButton.interactable = isInteractable;
            
            if (zoomOutButton != null)
                zoomOutButton.interactable = isInteractable;
            
            if (toggleOrbitDirectionButton != null)
                toggleOrbitDirectionButton.interactable = isInteractable;

            if (aimJoystick != null)
                aimJoystick.SetInteractable(isInteractable);
        }

        private void BindButtons()
        {
            UnbindButtons();

            if (exitCurrentWorldButton != null)
                exitCurrentWorldButton.onClick.AddListener(NotifyExitWorldRequested);
            
            if (zoomInButton != null)
                zoomInButton.onClick.AddListener(NotifyZoomInRequested);
            
            if (zoomOutButton != null)
                zoomOutButton.onClick.AddListener(NotifyZoomOutRequested);
            
            if (toggleOrbitDirectionButton != null)
                toggleOrbitDirectionButton.onClick.AddListener(NotifyToggleOrbitDirectionRequested);

        }

        private void UnbindButtons()
        {
            if (exitCurrentWorldButton != null)
                exitCurrentWorldButton.onClick.RemoveListener(NotifyExitWorldRequested);
            
            if (zoomInButton != null)
                zoomInButton.onClick.RemoveListener(NotifyZoomInRequested);
            
            if (zoomOutButton != null)
                zoomOutButton.onClick.RemoveListener(NotifyZoomOutRequested);
            
            if (toggleOrbitDirectionButton != null)
                toggleOrbitDirectionButton.onClick.RemoveListener(NotifyToggleOrbitDirectionRequested);

        }

        private void NotifyExitWorldRequested()
        {
            _onExitWorldRequested?.Invoke();
        }

        private void NotifyZoomInRequested()
        {
            _onZoomInRequested?.Invoke();
        }

        private void NotifyZoomOutRequested()
        {
            _onZoomOutRequested?.Invoke();
        }

        private void NotifyToggleOrbitDirectionRequested()
        {
            _onToggleOrbitDirectionRequested?.Invoke();
        }

        private void BindAimJoystick()
        {
            UnbindAimJoystick();

            if (!aimJoystick)
                return;

            aimJoystick.DirectionChanged += OnAimDirectionChanged;
            aimJoystick.Released += OnAimReleased;
        }

        private void UnbindAimJoystick()
        {
            if (!aimJoystick)
                return;

            aimJoystick.DirectionChanged -= OnAimDirectionChanged;
            aimJoystick.Released -= OnAimReleased;
        }

        private void OnAimDirectionChanged(Vector2 aimDirection)
        {
            _onAimDirectionChanged?.Invoke(aimDirection);
        }

        private void OnAimReleased()
        {
            _onAimReleased?.Invoke();
        }

        private void ApplyRightPanelLayout(bool force)
        {
            if (!enforceRightPanelLayoutAtRuntime || panelRoot == null)
                return;

            var size = new Vector2Int(Screen.width, Screen.height);
            if (!force && size == _lastScreenSize)
                return;

            _lastScreenSize = size;

            var clampedWidth = Mathf.Clamp(rightPanelWidthFraction, 0.1f, 0.9f);
            var left = 1f - clampedWidth;
            panelRoot.anchorMin = new Vector2(left, 0f);
            panelRoot.anchorMax = new Vector2(1f, 1f);
            panelRoot.pivot = new Vector2(1f, 0.5f);
            panelRoot.offsetMin = Vector2.zero;
            panelRoot.offsetMax = Vector2.zero;
        }
    }
}
