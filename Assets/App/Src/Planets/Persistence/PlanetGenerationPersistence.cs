using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using App.Planets.Generation;
using UnityEngine;

namespace App.Planets.Persistence
{
    public class PlanetGenerationPersistence : MonoBehaviour
    {
        [SerializeField] private string generatedRootName = "__GeneratedPlanet";
        [SerializeField] private string cacheFolderName = "PlanetCache";
        [SerializeField] private string cacheId = "default";
        [SerializeField] private bool autoSaveAfterRuntimeGeneration = true;
        [SerializeField] private bool runtimeSaveOnlyIfChanged = true;
        [SerializeField] private bool verboseLogging = true;

        private PlanetGenerator _generator;
        private int? _lastSavedRuntimeSignature;

        private void OnEnable()
        {
            _generator = GetComponent<PlanetGenerator>();
            if (_generator)
                _generator.PlanetGenerated += OnPlanetGenerated;
        }

        private void OnDisable()
        {
            if (_generator)
                _generator.PlanetGenerated -= OnPlanetGenerated;
        }

        public void Configure(
            string generatedRootNameValue,
            string cacheFolderNameValue,
            string cacheIdValue,
            bool autoSaveAfterRuntimeGenerationValue,
            bool runtimeSaveOnlyIfChangedValue,
            bool verboseLoggingValue)
        {
            generatedRootName = generatedRootNameValue;
            cacheFolderName = cacheFolderNameValue;
            cacheId = cacheIdValue;
            autoSaveAfterRuntimeGeneration = autoSaveAfterRuntimeGenerationValue;
            runtimeSaveOnlyIfChanged = runtimeSaveOnlyIfChangedValue;
            verboseLogging = verboseLoggingValue;
        }

        [ContextMenu("Save Generated Cache (Force)")]
        public void SaveGeneratedCache()
        {
            SaveGeneratedCacheInternal(saveOnlyIfChangedAtRuntime: false);
        }

        [ContextMenu("Save Generated Cache (Runtime If Changed)")]
        public void SaveGeneratedCacheRuntimeAware()
        {
            SaveGeneratedCacheInternal(saveOnlyIfChangedAtRuntime: runtimeSaveOnlyIfChanged);
        }

        private void OnPlanetGenerated(PlanetGenerator _)
        {
            if (!Application.isPlaying || !autoSaveAfterRuntimeGeneration)
                return;

            SaveGeneratedCacheRuntimeAware();
        }

        private void SaveGeneratedCacheInternal(bool saveOnlyIfChangedAtRuntime)
        {
            var generatedRoot = transform.Find(generatedRootName);
            if (!generatedRoot)
            {
                Debug.LogWarning($"Generated root '{generatedRootName}' not found under '{name}'.", this);
                return;
            }

            var saveRoot = Path.Combine(Application.persistentDataPath, cacheFolderName, cacheId);
            var texturesDir = Path.Combine(saveRoot, "textures");
            var manifestPath = Path.Combine(saveRoot, "manifest.json");
            int? runtimeSignatureCandidate = null;

            if (Application.isPlaying && saveOnlyIfChangedAtRuntime)
            {
                var currentSignature = BuildRuntimeSnapshotSignature(generatedRoot);
                runtimeSignatureCandidate = currentSignature;
                if (_lastSavedRuntimeSignature.HasValue && _lastSavedRuntimeSignature.Value == currentSignature && File.Exists(manifestPath))
                {
                    if (verboseLogging)
                        Debug.Log($"Skipped cache save '{cacheId}' in runtime: no generation changes detected.", this);
                    return;
                }
            }

            if (Directory.Exists(saveRoot))
                Directory.Delete(saveRoot, true);

            Directory.CreateDirectory(texturesDir);

            var manifest = new GenerationCacheManifest
            {
                version = 1,
                cacheId = cacheId,
                generatedRootName = generatedRootName,
                sourceObjectName = name,
                savedAtUtc = DateTime.UtcNow.ToString("O")
            };

            var textureIndexByInstanceId = new Dictionary<int, int>();
            var spriteIndexByKey = new Dictionary<string, int>();

            var renderers = generatedRoot.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer.sprite)
                    continue;

                var textureIndex = EnsureTextureSaved(renderer.sprite.texture, texturesDir, manifest, textureIndexByInstanceId);
                if (textureIndex < 0)
                    continue;

                var spriteIndex = EnsureSpriteSaved(renderer.sprite, textureIndex, manifest, spriteIndexByKey);
                manifest.renderers.Add(CreateRendererEntry(renderer, generatedRoot, spriteIndex));
            }

            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);

            if (Application.isPlaying)
                _lastSavedRuntimeSignature = runtimeSignatureCandidate ?? BuildRuntimeSnapshotSignature(generatedRoot);

            if (verboseLogging)
            {
                Debug.Log(
                    $"Saved planet cache '{cacheId}'. Renderers: {manifest.renderers.Count}, Sprites: {manifest.sprites.Count}, Textures: {manifest.textures.Count}. Path: {saveRoot}",
                    this);
            }
        }

        private static int EnsureTextureSaved(
            Texture2D texture,
            string texturesDir,
            GenerationCacheManifest manifest,
            Dictionary<int, int> textureIndexByInstanceId)
        {
            if (!texture)
                return -1;

            var textureId = texture.GetInstanceID();
            if (textureIndexByInstanceId.TryGetValue(textureId, out var existingIndex))
                return existingIndex;

            if (!texture.isReadable)
            {
                Debug.LogWarning($"Texture '{texture.name}' is not readable and cannot be cached.");
                return -1;
            }

            var pngBytes = texture.EncodeToPNG();
            var textureIndex = manifest.textures.Count;
            var fileName = $"texture_{textureIndex:D4}.png";
            var filePath = Path.Combine(texturesDir, fileName);
            File.WriteAllBytes(filePath, pngBytes);

            manifest.textures.Add(new TextureCacheEntry
            {
                fileName = fileName,
                width = texture.width,
                height = texture.height,
                filterMode = texture.filterMode,
                wrapMode = texture.wrapMode
            });

            textureIndexByInstanceId[textureId] = textureIndex;
            return textureIndex;
        }

        private static int EnsureSpriteSaved(
            Sprite sprite,
            int textureIndex,
            GenerationCacheManifest manifest,
            Dictionary<string, int> spriteIndexByKey)
        {
            var rect = sprite.rect;
            var pivot = sprite.pivot;
            var key = $"{textureIndex}:{rect.x}:{rect.y}:{rect.width}:{rect.height}:{pivot.x}:{pivot.y}:{sprite.pixelsPerUnit}";

            if (spriteIndexByKey.TryGetValue(key, out var existingIndex))
                return existingIndex;

            var spriteIndex = manifest.sprites.Count;
            manifest.sprites.Add(new SpriteCacheEntry
            {
                textureIndex = textureIndex,
                rect = rect,
                pivot = new Vector2(
                    rect.width <= 0f ? 0.5f : pivot.x / rect.width,
                    rect.height <= 0f ? 0.5f : pivot.y / rect.height),
                pixelsPerUnit = sprite.pixelsPerUnit
            });

            spriteIndexByKey[key] = spriteIndex;
            return spriteIndex;
        }

        private static RendererCacheEntry CreateRendererEntry(SpriteRenderer renderer, Transform root, int spriteIndex)
        {
            return new RendererCacheEntry
            {
                path = GetRelativePath(renderer.transform, root),
                spriteIndex = spriteIndex,
                localPosition = renderer.transform.localPosition,
                localRotation = renderer.transform.localRotation,
                localScale = renderer.transform.localScale,
                color = renderer.color,
                sortingLayerName = renderer.sortingLayerName,
                sortingOrder = renderer.sortingOrder,
                flipX = renderer.flipX,
                flipY = renderer.flipY,
                enabled = renderer.enabled
            };
        }

        private static string GetRelativePath(Transform target, Transform root)
        {
            if (target == root)
                return string.Empty;

            var segments = new List<string>();
            var current = target;
            while (current && current != root)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments);
        }

        private int BuildRuntimeSnapshotSignature(Transform generatedRoot)
        {
            return BuildUniqueSegmentSetSignature(generatedRoot);
        }

        private static int BuildUniqueSegmentSetSignature(Transform generatedRoot)
        {
            var renderers = generatedRoot.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            var segmentPaths = new List<string>(renderers.Length);

            foreach (var renderer in renderers)
            {
                if (!IsSegmentRenderer(renderer))
                    continue;

                segmentPaths.Add(GetRelativePath(renderer.transform, generatedRoot));
            }

            segmentPaths.Sort(string.CompareOrdinal);

            return segmentPaths.Aggregate(17, (current, path) => Combine(current, path.GetHashCode()));
        }

        private static bool IsSegmentRenderer(SpriteRenderer renderer) => renderer && renderer.gameObject.name.StartsWith("Segment_");

        private static int Combine(int hash, int value)
        {
            unchecked
            {
                return (hash * 31) + value;
            }
        }

        [Serializable]
        private class GenerationCacheManifest
        {
            public int version;
            public string cacheId;
            public string generatedRootName;
            public string sourceObjectName;
            public string savedAtUtc;
            public List<TextureCacheEntry> textures = new List<TextureCacheEntry>();
            public List<SpriteCacheEntry> sprites = new List<SpriteCacheEntry>();
            public List<RendererCacheEntry> renderers = new List<RendererCacheEntry>();
        }

        [Serializable]
        private class TextureCacheEntry
        {
            public string fileName;
            public int width;
            public int height;
            public FilterMode filterMode;
            public TextureWrapMode wrapMode;
        }

        [Serializable]
        private class SpriteCacheEntry
        {
            public int textureIndex;
            public Rect rect;
            public Vector2 pivot;
            public float pixelsPerUnit;
        }

        [Serializable]
        private class RendererCacheEntry
        {
            public string path;
            public int spriteIndex;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
            public Color color;
            public string sortingLayerName;
            public int sortingOrder;
            public bool flipX;
            public bool flipY;
            public bool enabled;
        }

    }
}
