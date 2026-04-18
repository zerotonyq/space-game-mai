using System;
using System.Globalization;
using TMPro;
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
        [SerializeField] private Button increaseFlightAltitudeButton;
        [SerializeField] private Button decreaseFlightAltitudeButton;
        [SerializeField] private Button toggleOrbitDirectionButton;
        [SerializeField] private ProjectileActionButtons rocketButtons;
        [SerializeField] private ProjectileActionButtons drillType1Buttons;
        [SerializeField] private ProjectileActionButtons drillType2Buttons;
        [SerializeField] private ProjectileActionButtons drillType3Buttons;

        [Header("Resource Texts")]
        [SerializeField] private TextMeshProUGUI magmaPointsText;
        [SerializeField] private TextMeshProUGUI metal1PointsText;
        [SerializeField] private TextMeshProUGUI metal2PointsText;
        [SerializeField] private TextMeshProUGUI metal3PointsText;
        [SerializeField] private string resourceCountFormat = "{0}";

        [Header("Generated Flight Altitude Buttons")]
        [SerializeField] private bool createMissingFlightAltitudeButtons = true;
        [SerializeField] private Vector2 increaseFlightAltitudeButtonPosition = new(145f, 110f);
        [SerializeField] private Vector2 decreaseFlightAltitudeButtonPosition = new(145f, 46f);
        [SerializeField] private Vector2 flightAltitudeButtonSize = new(70f, 50f);
        
        [Header("Aim")]
        [SerializeField] private GameplayAimJoystick aimJoystick;

        private Action _onExitWorldRequested;
        private Action _onZoomInRequested;
        private Action _onZoomOutRequested;
        private Action _onIncreaseFlightAltitudeRequested;
        private Action _onDecreaseFlightAltitudeRequested;
        private Action _onToggleOrbitDirectionRequested;
        private Action _onShootRocketRequested;
        private Action _onCreateRocketRequested;
        private Action _onShootDrillType1Requested;
        private Action _onCreateDrillType1Requested;
        private Action _onShootDrillType2Requested;
        private Action _onCreateDrillType2Requested;
        private Action _onShootDrillType3Requested;
        private Action _onCreateDrillType3Requested;
        private Action<Vector2> _onAimDirectionChanged;
        private Action _onAimReleased;
        private Vector2Int _lastScreenSize;
        public Vector2 AimDirection => aimJoystick ? aimJoystick.Direction : Vector2.zero;
        public GameplayAimJoystick AimJoystick => aimJoystick;

        private void Awake()
        {
            EnsureFlightAltitudeButtons();
        }

        private void OnEnable()
        {
            EnsureFlightAltitudeButtons();
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
            Action onIncreaseFlightAltitudeRequested,
            Action onDecreaseFlightAltitudeRequested,
            Action onToggleOrbitDirectionRequested,
            Action onShootRocketRequested,
            Action onCreateRocketRequested,
            Action onShootDrillType1Requested,
            Action onCreateDrillType1Requested,
            Action onShootDrillType2Requested,
            Action onCreateDrillType2Requested,
            Action onShootDrillType3Requested,
            Action onCreateDrillType3Requested,
            Action<Vector2> onAimDirectionChanged,
            Action onAimReleased)
        {
            _onExitWorldRequested = onExitWorldRequested;
            _onZoomInRequested = onZoomInRequested;
            _onZoomOutRequested = onZoomOutRequested;
            _onIncreaseFlightAltitudeRequested = onIncreaseFlightAltitudeRequested;
            _onDecreaseFlightAltitudeRequested = onDecreaseFlightAltitudeRequested;
            _onToggleOrbitDirectionRequested = onToggleOrbitDirectionRequested;
            _onShootRocketRequested = onShootRocketRequested;
            _onCreateRocketRequested = onCreateRocketRequested;
            _onShootDrillType1Requested = onShootDrillType1Requested;
            _onCreateDrillType1Requested = onCreateDrillType1Requested;
            _onShootDrillType2Requested = onShootDrillType2Requested;
            _onCreateDrillType2Requested = onCreateDrillType2Requested;
            _onShootDrillType3Requested = onShootDrillType3Requested;
            _onCreateDrillType3Requested = onCreateDrillType3Requested;
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
            _onIncreaseFlightAltitudeRequested = null;
            _onDecreaseFlightAltitudeRequested = null;
            _onToggleOrbitDirectionRequested = null;
            _onShootRocketRequested = null;
            _onCreateRocketRequested = null;
            _onShootDrillType1Requested = null;
            _onCreateDrillType1Requested = null;
            _onShootDrillType2Requested = null;
            _onCreateDrillType2Requested = null;
            _onShootDrillType3Requested = null;
            _onCreateDrillType3Requested = null;
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

            if (increaseFlightAltitudeButton != null)
                increaseFlightAltitudeButton.interactable = isInteractable;

            if (decreaseFlightAltitudeButton != null)
                decreaseFlightAltitudeButton.interactable = isInteractable;
            
            if (toggleOrbitDirectionButton != null)
                toggleOrbitDirectionButton.interactable = isInteractable;
            
            rocketButtons?.SetInteractable(isInteractable);
            drillType1Buttons?.SetInteractable(isInteractable);
            drillType2Buttons?.SetInteractable(isInteractable);
            drillType3Buttons?.SetInteractable(isInteractable);

            if (aimJoystick != null)
                aimJoystick.SetInteractable(isInteractable);
        }

        public void SetProjectileCounts(int rockets, int drillType1, int drillType2, int drillType3)
        {
            rocketButtons?.SetCreatedCount(rockets);
            drillType1Buttons?.SetCreatedCount(drillType1);
            drillType2Buttons?.SetCreatedCount(drillType2);
            drillType3Buttons?.SetCreatedCount(drillType3);
        }

        public void SetResourceCounts(int magma, int metal1, int metal2, int metal3)
        {
            SetCountText(magmaPointsText, magma);
            SetCountText(metal1PointsText, metal1);
            SetCountText(metal2PointsText, metal2);
            SetCountText(metal3PointsText, metal3);
        }

        public void ResetResourceAndProjectileCounts()
        {
            SetProjectileCounts(0, 0, 0, 0);
            SetResourceCounts(0, 0, 0, 0);
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

            if (increaseFlightAltitudeButton != null)
                increaseFlightAltitudeButton.onClick.AddListener(NotifyIncreaseFlightAltitudeRequested);

            if (decreaseFlightAltitudeButton != null)
                decreaseFlightAltitudeButton.onClick.AddListener(NotifyDecreaseFlightAltitudeRequested);
            
            if (toggleOrbitDirectionButton != null)
                toggleOrbitDirectionButton.onClick.AddListener(NotifyToggleOrbitDirectionRequested);

            rocketButtons?.Bind(NotifyShootRocketRequested, NotifyCreateRocketRequested);
            drillType1Buttons?.Bind(NotifyShootDrillType1Requested, NotifyCreateDrillType1Requested);
            drillType2Buttons?.Bind(NotifyShootDrillType2Requested, NotifyCreateDrillType2Requested);
            drillType3Buttons?.Bind(NotifyShootDrillType3Requested, NotifyCreateDrillType3Requested);
        }

        private void UnbindButtons()
        {
            if (exitCurrentWorldButton != null)
                exitCurrentWorldButton.onClick.RemoveListener(NotifyExitWorldRequested);
            
            if (zoomInButton != null)
                zoomInButton.onClick.RemoveListener(NotifyZoomInRequested);
            
            if (zoomOutButton != null)
                zoomOutButton.onClick.RemoveListener(NotifyZoomOutRequested);

            if (increaseFlightAltitudeButton != null)
                increaseFlightAltitudeButton.onClick.RemoveListener(NotifyIncreaseFlightAltitudeRequested);

            if (decreaseFlightAltitudeButton != null)
                decreaseFlightAltitudeButton.onClick.RemoveListener(NotifyDecreaseFlightAltitudeRequested);
            
            if (toggleOrbitDirectionButton != null)
                toggleOrbitDirectionButton.onClick.RemoveListener(NotifyToggleOrbitDirectionRequested);

            rocketButtons?.Unbind();
            drillType1Buttons?.Unbind();
            drillType2Buttons?.Unbind();
            drillType3Buttons?.Unbind();
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

        private void NotifyIncreaseFlightAltitudeRequested()
        {
            _onIncreaseFlightAltitudeRequested?.Invoke();
        }

        private void NotifyDecreaseFlightAltitudeRequested()
        {
            _onDecreaseFlightAltitudeRequested?.Invoke();
        }

        private void NotifyToggleOrbitDirectionRequested()
        {
            _onToggleOrbitDirectionRequested?.Invoke();
        }

        private void NotifyShootRocketRequested() => _onShootRocketRequested?.Invoke();
        private void NotifyCreateRocketRequested() => _onCreateRocketRequested?.Invoke();
        private void NotifyShootDrillType1Requested() => _onShootDrillType1Requested?.Invoke();
        private void NotifyCreateDrillType1Requested() => _onCreateDrillType1Requested?.Invoke();
        private void NotifyShootDrillType2Requested() => _onShootDrillType2Requested?.Invoke();
        private void NotifyCreateDrillType2Requested() => _onCreateDrillType2Requested?.Invoke();
        private void NotifyShootDrillType3Requested() => _onShootDrillType3Requested?.Invoke();
        private void NotifyCreateDrillType3Requested() => _onCreateDrillType3Requested?.Invoke();

        private void SetCountText(TextMeshProUGUI text, int count)
        {
            if (text != null)
                text.text = FormatCount(resourceCountFormat, Mathf.Max(0, count));
        }

        private static string FormatCount(string format, int count)
        {
            if (string.IsNullOrWhiteSpace(format))
                return count.ToString(CultureInfo.InvariantCulture);

            try
            {
                if (format.Contains("{0"))
                    return string.Format(CultureInfo.InvariantCulture, format, count);

                return count.ToString(format, CultureInfo.InvariantCulture);
            }
            catch (FormatException)
            {
                return count.ToString(CultureInfo.InvariantCulture);
            }
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

        private void EnsureFlightAltitudeButtons()
        {
            if (!createMissingFlightAltitudeButtons || panelRoot == null)
                return;

            if (increaseFlightAltitudeButton == null)
                increaseFlightAltitudeButton = CreatePanelButton(
                    "FlightAltitudeIncrease",
                    "^",
                    increaseFlightAltitudeButtonPosition);

            if (decreaseFlightAltitudeButton == null)
                decreaseFlightAltitudeButton = CreatePanelButton(
                    "FlightAltitudeDecrease",
                    "v",
                    decreaseFlightAltitudeButtonPosition);
        }

        private Button CreatePanelButton(string objectName, string label, Vector2 anchoredPosition)
        {
            var buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(panelRoot, false);

            var rectTransform = (RectTransform)buttonObject.transform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.zero;
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = anchoredPosition;
            rectTransform.sizeDelta = flightAltitudeButtonSize;

            var image = buttonObject.GetComponent<Image>();
            image.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
            image.type = Image.Type.Sliced;
            image.color = Color.white;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var textObject = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);

            var textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            textRect.pivot = new Vector2(0.5f, 0.5f);

            var text = textObject.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 24;
            text.color = new Color(0.196f, 0.196f, 0.196f, 1f);
            text.raycastTarget = false;

            return button;
        }
    }
}
