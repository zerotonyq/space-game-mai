using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using App.Planets.Core;
using App.Planets.Generation;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

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
        private bool _isSaveInProgress;
        private bool _pendingForceSave;
        private bool _pendingRuntimeAwareSave;

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
            EnqueueSave(saveOnlyIfChangedAtRuntime: false);
        }

        [ContextMenu("Save Generated Cache (Runtime If Changed)")]
        public void SaveGeneratedCacheRuntimeAware()
        {
            EnqueueSave(saveOnlyIfChangedAtRuntime: runtimeSaveOnlyIfChanged);
        }

        public UniTask SaveGeneratedCacheAsync()
        {
            return SaveGeneratedCacheInternalAsync(saveOnlyIfChangedAtRuntime: false);
        }

        public UniTask SaveGeneratedCacheRuntimeAwareAsync()
        {
            return SaveGeneratedCacheInternalAsync(saveOnlyIfChangedAtRuntime: runtimeSaveOnlyIfChanged);
        }

        private void OnPlanetGenerated(PlanetGenerator _)
        {
            if (!Application.isPlaying || !autoSaveAfterRuntimeGeneration)
                return;

            EnqueueSave(saveOnlyIfChangedAtRuntime: runtimeSaveOnlyIfChanged);
        }

        private void EnqueueSave(bool saveOnlyIfChangedAtRuntime)
        {
            if (saveOnlyIfChangedAtRuntime)
                _pendingRuntimeAwareSave = true;
            else
                _pendingForceSave = true;

            if (_isSaveInProgress)
                return;

            ProcessSaveQueueAsync().Forget();
        }

        private async UniTask ProcessSaveQueueAsync()
        {
            _isSaveInProgress = true;
            try
            {
                while (_pendingForceSave || _pendingRuntimeAwareSave)
                {
                    var saveOnlyIfChangedAtRuntime = !_pendingForceSave && _pendingRuntimeAwareSave;
                    _pendingForceSave = false;
                    _pendingRuntimeAwareSave = false;
                    await SaveGeneratedCacheInternalAsync(saveOnlyIfChangedAtRuntime);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                _isSaveInProgress = false;
            }
        }

        private async UniTask SaveGeneratedCacheInternalAsync(bool saveOnlyIfChangedAtRuntime)
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

            var manifest = new GenerationCacheManifest
            {
                version = 2,
                cacheId = cacheId,
                generatedRootName = generatedRootName,
                sourceObjectName = name,
                savedAtUtc = DateTime.UtcNow.ToString("O")
            };

            var textureIndexByInstanceId = new Dictionary<int, int>();
            var spriteIndexByKey = new Dictionary<string, int>();
            var pendingTextureWrites = new List<PendingTextureWrite>();

            var renderers = generatedRoot.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer.sprite)
                    continue;

                var textureIndex = EnsureTextureSaved(renderer.sprite.texture, texturesDir, manifest, textureIndexByInstanceId, pendingTextureWrites);
                if (textureIndex < 0)
                    continue;

                var mask = renderer.GetComponent<PlanetSegmentRenderMask>();
                var maskTextureIndex = -1;
                if (mask && mask.UseMaskRendering && mask.FillTexture)
                {
                    maskTextureIndex = EnsureTextureSaved(mask.FillTexture, texturesDir, manifest, textureIndexByInstanceId, pendingTextureWrites);
                }

                var spriteIndex = EnsureSpriteSaved(renderer.sprite, textureIndex, manifest, spriteIndexByKey);
                manifest.renderers.Add(CreateRendererEntry(renderer, generatedRoot, spriteIndex, maskTextureIndex));

                if (Application.isPlaying && i % 8 == 0)
                    await UniTask.Yield();
            }

            SaveSegmentStates(generatedRoot, manifest);
            var manifestJson = JsonUtility.ToJson(manifest, true);

            await UniTask.SwitchToThreadPool();
            try
            {
                if (Directory.Exists(saveRoot))
                    Directory.Delete(saveRoot, true);

                Directory.CreateDirectory(texturesDir);

                for (var i = 0; i < pendingTextureWrites.Count; i++)
                    File.WriteAllBytes(pendingTextureWrites[i].FilePath, pendingTextureWrites[i].Bytes);

                File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
            }

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
            Dictionary<int, int> textureIndexByInstanceId,
            List<PendingTextureWrite> pendingTextureWrites)
        {
            if (!texture)
                return -1;

            var textureId = texture.GetInstanceID();
            if (textureIndexByInstanceId.TryGetValue(textureId, out var existingIndex))
                return existingIndex;

            var pngBytes = EncodeTextureToPng(texture);
            if (pngBytes == null || pngBytes.Length == 0)
            {
                Debug.LogWarning(
                    $"Texture '{texture.name}' could not be encoded to PNG. format={texture.format}, graphicsFormat={texture.graphicsFormat}, readable={texture.isReadable}, compressed={IsCompressedFormat(texture)}.");
                return -1;
            }
            var textureIndex = manifest.textures.Count;
            var fileName = $"texture_{textureIndex:D4}.png";
            var filePath = Path.Combine(texturesDir, fileName);
            pendingTextureWrites.Add(new PendingTextureWrite(filePath, pngBytes));

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

        private static byte[] EncodeTextureToPng(Texture2D texture)
        {
            if (!texture)
                return null;

            if (!IsCompressedFormat(texture))
            {
                try
                {
                    return texture.EncodeToPNG();
                }
                catch
                {
                    // Fallback for formats unsupported by EncodeToPNG.
                }
            }

            Texture2D converted = null;
            try
            {
                var width = Mathf.Max(1, texture.width);
                var height = Mathf.Max(1, texture.height);
                converted = new Texture2D(
                    width,
                    height,
                    TextureFormat.RGBA32,
                    mipChain: false);

                var copied = false;
                if (texture.isReadable)
                {
                    try
                    {
                        converted.SetPixels32(texture.GetPixels32());
                        converted.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                        copied = true;
                    }
                    catch
                    {
                        copied = false;
                    }
                }

                if (!copied)
                {
                    var previousActive = RenderTexture.active;
                    var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                    try
                    {
                        Graphics.Blit(texture, rt);
                        RenderTexture.active = rt;
                        converted.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                        converted.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                        copied = true;
                    }
                    catch
                    {
                        copied = false;
                    }
                    finally
                    {
                        RenderTexture.active = previousActive;
                        RenderTexture.ReleaseTemporary(rt);
                    }
                }

                if (!copied)
                {
                    var pixels = new Color32[width * height];
                    for (var i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32(255, 255, 255, 255);
                    converted.SetPixels32(pixels);
                    converted.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                }

                return converted.EncodeToPNG();
            }
            catch
            {
                return null;
            }
            finally
            {
                if (converted != null)
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(converted);
                    else
                        UnityEngine.Object.DestroyImmediate(converted);
                }
            }
        }

        private static bool IsCompressedFormat(Texture2D texture)
        {
            if (!texture)
                return false;

            return GraphicsFormatUtility.IsCompressedFormat(texture.graphicsFormat);
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

        private static RendererCacheEntry CreateRendererEntry(SpriteRenderer renderer, Transform root, int spriteIndex, int maskTextureIndex)
        {
            var mask = renderer.GetComponent<PlanetSegmentRenderMask>();
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
                enabled = renderer.enabled,
                useSegmentMaskRendering = mask && mask.UseMaskRendering,
                maskInnerRadiusNormalized = mask ? mask.InnerRadiusNormalized : 0f,
                maskOuterRadiusNormalized = mask ? mask.OuterRadiusNormalized : 0f,
                maskHalfAngleDeg = mask ? mask.HalfAngleDeg : 0f,
                maskFillColor = mask ? mask.FillColor : Color.white,
                maskTextureScale = mask ? mask.TextureScale : 1f,
                maskTextureTileSizeUnits = mask ? mask.TextureTileSizeUnits : 0.25f,
                maskTextureUvOffset = mask ? mask.TextureUvOffset : Vector2.zero,
                maskTextureIndex = maskTextureIndex,
                maskEnableOutline = mask && mask.EnableOutline,
                maskOutlineColor = mask ? mask.OutlineColor : Color.black,
                maskOutlineWidthNormalized = mask ? mask.OutlineWidthNormalized : 0f
            };
        }

        private static void SaveSegmentStates(Transform generatedRoot, GenerationCacheManifest manifest)
        {
            var segments = generatedRoot.GetComponentsInChildren<PlanetSegment>(includeInactive: true);
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (!segment)
                    continue;

                manifest.segmentStates.Add(new SegmentStateCacheEntry
                {
                    path = GetRelativePath(segment.transform, generatedRoot),
                    material = segment.Material,
                    initialMaterialPoints = segment.InitialMaterialPoints,
                    currentMaterialPoints = segment.CurrentMaterialPoints,
                    segmentAreaUnits = segment.SegmentAreaUnits,
                    isCoreSegment = segment.IsCoreSegment,
                    isDestroyed = segment.IsDestroyed
                });
            }
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
            return BuildSegmentStateSignature(generatedRoot);
        }

        private static int BuildSegmentStateSignature(Transform generatedRoot)
        {
            var segments = generatedRoot.GetComponentsInChildren<PlanetSegment>(includeInactive: true);
            if (segments.Length == 0)
                return BuildUniqueSegmentSetSignatureFromRenderers(generatedRoot);

            var segmentEntries = new List<string>(segments.Length);
            foreach (var segment in segments)
            {
                if (!segment)
                    continue;

                var path = GetRelativePath(segment.transform, generatedRoot);
                segmentEntries.Add($"{path}|{(int)segment.Material}|{segment.InitialMaterialPoints}|{segment.CurrentMaterialPoints}|{segment.IsCoreSegment}|{segment.IsDestroyed}");
            }

            segmentEntries.Sort(string.CompareOrdinal);
            return segmentEntries.Aggregate(17, (current, entry) => Combine(current, entry.GetHashCode()));
        }

        private static int BuildUniqueSegmentSetSignatureFromRenderers(Transform generatedRoot)
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

        private readonly struct PendingTextureWrite
        {
            public readonly string FilePath;
            public readonly byte[] Bytes;

            public PendingTextureWrite(string filePath, byte[] bytes)
            {
                FilePath = filePath;
                Bytes = bytes;
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
            public List<SegmentStateCacheEntry> segmentStates = new List<SegmentStateCacheEntry>();
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
            public bool useSegmentMaskRendering;
            public float maskInnerRadiusNormalized;
            public float maskOuterRadiusNormalized;
            public float maskHalfAngleDeg;
            public Color maskFillColor;
            public float maskTextureScale;
            public float maskTextureTileSizeUnits;
            public Vector2 maskTextureUvOffset;
            public int maskTextureIndex;
            public bool maskEnableOutline;
            public Color maskOutlineColor;
            public float maskOutlineWidthNormalized;
        }

        [Serializable]
        private class SegmentStateCacheEntry
        {
            public string path;
            public PlanetSegmentMaterial material;
            public int initialMaterialPoints;
            public int currentMaterialPoints;
            public float segmentAreaUnits;
            public bool isCoreSegment;
            public bool isDestroyed;
        }

    }
}
