using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Planets.Persistence
{
    public class WorldMenuCanvasController : MonoBehaviour
    {
        [Header("Canvas")]
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private RectTransform panelRoot;

        [Header("Layout")]
        [SerializeField] [Range(0.1f, 0.9f)] private float rightPanelWidthFraction = 1f / 3f;
        [SerializeField] private bool enforceRightPanelLayoutAtRuntime = true;

        [Header("Buttons")]
        [SerializeField] private Button newGameButton;
        [SerializeField] private TMP_Dropdown newGameWorldTypeDropdown;

        [Header("Load World List")]
        [SerializeField] private ScrollRect loadWorldScroll;
        [SerializeField] private Transform loadWorldContentRoot;
        [SerializeField] private Button loadWorldItemButtonPrefab;
        [SerializeField] private string worldsRootFolderName = "Worlds";
        [SerializeField] private string managerWorldManifestFileName = "planet_world_manifest.json";

        private readonly List<Button> _spawnedWorldButtons = new();
        private Action<PlanetWorldManager.WorldSize> _onNewGameRequested;
        private Action<string> _onWorldLoadRequested;
        private Vector2Int _lastScreenSize;

        private void OnEnable()
        {
            if (loadWorldScroll != null && loadWorldContentRoot == null)
                loadWorldContentRoot = loadWorldScroll.content;

            BindButtons();
            ApplyRightPanelLayout(force: true);
        }

        private void Update()
        {
            ApplyRightPanelLayout(force: false);
        }

        private void OnDisable()
        {
            UnbindButtons();
            ClearWorldButtons();
        }

        public void Initialize(
            Action<PlanetWorldManager.WorldSize> onNewGameRequested,
            Action<string> onWorldLoadRequested,
            PlanetWorldManager.WorldSize defaultWorldSize)
        {
            _onNewGameRequested = onNewGameRequested;
            _onWorldLoadRequested = onWorldLoadRequested;

            BindButtons();
            SyncWorldSizeDropdownSelection(defaultWorldSize);

            if (loadWorldScroll != null && loadWorldContentRoot == null)
                loadWorldContentRoot = loadWorldScroll.content;
        }

        public void Release()
        {
            UnbindButtons();
            _onNewGameRequested = null;
            _onWorldLoadRequested = null;
        }

        public void SetVisible(bool isVisible)
        {
            if (rootCanvas != null)
                rootCanvas.gameObject.SetActive(isVisible);
        }

        public void SetInteractable(bool isInteractable)
        {
            if (newGameButton != null)
                newGameButton.interactable = isInteractable;

            for (var i = 0; i < _spawnedWorldButtons.Count; i++)
            {
                var button = _spawnedWorldButtons[i];
                if (button != null)
                    button.interactable = isInteractable;
            }
        }

        [ContextMenu("Refresh World List")]
        public void RefreshWorldList()
        {
            ClearWorldButtons();

            if (!loadWorldItemButtonPrefab || !loadWorldContentRoot)
                return;

            var worldsRootPath = Path.Combine(Application.persistentDataPath, worldsRootFolderName);
            if (!Directory.Exists(worldsRootPath))
                return;

            var worldDirectories = Directory.GetDirectories(worldsRootPath);
            Array.Sort(worldDirectories, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < worldDirectories.Length; i++)
            {
                if (!HasLoadableWorldManifest(worldDirectories[i]))
                    continue;

                var worldId = Path.GetFileName(worldDirectories[i]);
                if (string.IsNullOrWhiteSpace(worldId))
                    continue;

                CreateWorldButton(worldId);
            }
        }

        private void BindButtons()
        {
            UnbindButtons();

            if (newGameButton != null)
                newGameButton.onClick.AddListener(NotifyNewGameRequested);
        }

        private void UnbindButtons()
        {
            if (newGameButton != null)
                newGameButton.onClick.RemoveListener(NotifyNewGameRequested);
        }

        private void NotifyNewGameRequested()
        {
            _onNewGameRequested?.Invoke(ResolveSelectedWorldSize());
        }

        private PlanetWorldManager.WorldSize ResolveSelectedWorldSize()
        {
            var selectedIndex = newGameWorldTypeDropdown != null ? newGameWorldTypeDropdown.value : 0;
            return (PlanetWorldManager.WorldSize)Mathf.Clamp(
                selectedIndex,
                0,
                Enum.GetValues(typeof(PlanetWorldManager.WorldSize)).Length - 1);
        }

        private void SyncWorldSizeDropdownSelection(PlanetWorldManager.WorldSize defaultWorldSize)
        {
            if (newGameWorldTypeDropdown == null)
                return;

            var index = Mathf.Clamp(
                (int)defaultWorldSize,
                0,
                Enum.GetValues(typeof(PlanetWorldManager.WorldSize)).Length - 1);
            newGameWorldTypeDropdown.SetValueWithoutNotify(index);
        }

        private bool HasLoadableWorldManifest(string worldDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(worldDirectoryPath))
                return false;

            var manifestPath = Path.Combine(worldDirectoryPath, managerWorldManifestFileName);
            if (!File.Exists(manifestPath))
                return false;

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<WorldListManifest>(json);
            return manifest != null && manifest.planets != null && manifest.planets.Count > 0;
        }

        private void CreateWorldButton(string worldId)
        {
            var button = Instantiate(loadWorldItemButtonPrefab, loadWorldContentRoot, worldPositionStays: false);
            button.name = $"World_{worldId}";
            button.onClick.AddListener(() => _onWorldLoadRequested?.Invoke(worldId));
            _spawnedWorldButtons.Add(button);

            var text = button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (text)
                text.text = worldId;
        }

        private void ClearWorldButtons()
        {
            for (var i = _spawnedWorldButtons.Count - 1; i >= 0; i--)
            {
                var button = _spawnedWorldButtons[i];
                if (button == null)
                    continue;

                if (Application.isPlaying)
                    Destroy(button.gameObject);
                else
                    DestroyImmediate(button.gameObject);
            }

            _spawnedWorldButtons.Clear();
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

        [Serializable]
        private class WorldListManifest
        {
            public List<WorldListPlanetEntry> planets = new();
        }

        [Serializable]
        private class WorldListPlanetEntry
        {
            public string planetId;
        }
    }
}
