using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using App.Planets.Core;
using App.Planets.Generation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace App.Planets.Persistence
{
    internal static class PlanetGenerationCacheLoader
    {
        private static readonly Dictionary<int, RuntimeCacheState> RuntimeStatesByOwnerId = new();

        public static bool TryLoad(
            Transform owner,
            string generatedRootName,
            string cacheFolderName,
            string cacheId,
            MonoBehaviour context,
            bool verboseLogging)
        {
            var saveRoot = Path.Combine(Application.persistentDataPath, cacheFolderName, cacheId);
            var texturesDir = Path.Combine(saveRoot, "textures");
            var manifestPath = Path.Combine(saveRoot, "manifest.json");

            if (!File.Exists(manifestPath))
                return false;

            var json = File.ReadAllText(manifestPath);
            var manifest = JsonUtility.FromJson<GenerationCacheManifest>(json);
            if (manifest == null)
                return false;

            var generatedRoot = PlanetHierarchyUtility.GetOrCreateGeneratedRoot(owner, generatedRootName);
            PlanetHierarchyUtility.ClearChildren(generatedRoot, Application.isPlaying);

            var runtimeState = GetOrCreateRuntimeState(owner.gameObject.GetInstanceID());
            runtimeState.DestroyLoadedAssets();

            var textures = LoadTextures(manifest, texturesDir, runtimeState);
            var sprites = CreateSprites(manifest, textures, runtimeState);
            RestoreRenderers(manifest, generatedRoot, sprites, textures);
            RestoreSegmentStates(manifest, generatedRoot);
            UpdateGeneratorOuterRadiusFromRenderers(owner, generatedRoot);

            if (verboseLogging)
            {
                Debug.Log(
                    $"Loaded planet cache '{cacheId}'. Renderers: {manifest.renderers.Count}, Sprites: {manifest.sprites.Count}, Textures: {manifest.textures.Count}. Path: {saveRoot}",
                    context);
            }

            return true;
        }

        private static List<Texture2D> LoadTextures(GenerationCacheManifest manifest, string texturesDir, RuntimeCacheState runtimeState)
        {
            var textures = new List<Texture2D>(manifest.textures.Count);

            foreach (var entry in manifest.textures)
            {
                var texturePath = Path.Combine(texturesDir, entry.fileName);
                if (!File.Exists(texturePath))
                {
                    textures.Add(null);
                    continue;
                }

                var bytes = File.ReadAllBytes(texturePath);
                var texture = new Texture2D(Mathf.Max(1, entry.width), Mathf.Max(1, entry.height), TextureFormat.RGBA32, mipChain: false)
                {
                    filterMode = entry.filterMode,
                    wrapMode = entry.wrapMode,
                    name = entry.fileName
                };
                texture.LoadImage(bytes, markNonReadable: false);
                textures.Add(texture);
                runtimeState.LoadedTextures.Add(texture);
            }

            return textures;
        }

        private static List<Sprite> CreateSprites(GenerationCacheManifest manifest, List<Texture2D> textures, RuntimeCacheState runtimeState)
        {
            var sprites = new List<Sprite>(manifest.sprites.Count);

            foreach (var entry in manifest.sprites)
            {
                if (entry.textureIndex < 0 || entry.textureIndex >= textures.Count || textures[entry.textureIndex] == null)
                {
                    sprites.Add(null);
                    continue;
                }

                var texture = textures[entry.textureIndex];
                var sprite = Sprite.Create(
                    texture,
                    entry.rect,
                    entry.pivot,
                    entry.pixelsPerUnit);
                sprites.Add(sprite);
                runtimeState.LoadedSprites.Add(sprite);
            }

            return sprites;
        }

        private static void RestoreRenderers(GenerationCacheManifest manifest, Transform generatedRoot, List<Sprite> sprites, List<Texture2D> textures)
        {
            foreach (var entry in manifest.renderers)
            {
                var target = GetOrCreateRelativeTransform(generatedRoot, entry.path);
                target.localPosition = entry.localPosition;
                target.localRotation = entry.localRotation;
                target.localScale = entry.localScale;

                var renderer = PlanetHierarchyUtility.GetOrAddSpriteRenderer(target.gameObject);
                renderer.sprite = entry.spriteIndex >= 0 && entry.spriteIndex < sprites.Count ? sprites[entry.spriteIndex] : null;
                renderer.color = entry.color;
                renderer.sortingLayerName = entry.sortingLayerName;
                renderer.sortingOrder = entry.sortingOrder;
                renderer.flipX = entry.flipX;
                renderer.flipY = entry.flipY;
                renderer.enabled = entry.enabled;

                if (entry.useSegmentMaskRendering)
                {
                    var mask = target.GetComponent<PlanetSegmentRenderMask>();
                    if (!mask)
                        mask = target.gameObject.AddComponent<PlanetSegmentRenderMask>();

                    mask.Configure(
                        entry.maskInnerRadiusNormalized,
                        entry.maskOuterRadiusNormalized,
                        entry.maskHalfAngleDeg,
                        ResolveMaskTexture(textures, entry.maskTextureIndex),
                        entry.maskFillColor,
                        entry.maskTextureScale <= 0f ? 1f : entry.maskTextureScale,
                        entry.maskEnableOutline,
                        entry.maskOutlineColor,
                        entry.maskOutlineWidthNormalized,
                        entry.maskTextureTileSizeUnits <= 0f ? 0.25f : entry.maskTextureTileSizeUnits,
                        entry.maskTextureUvOffset);
                    mask.Apply(renderer, ResolveMaskTexture(textures, entry.maskTextureIndex));
                }

                EnsureColliderForPlanetPart(target.gameObject, renderer, entry);
            }
        }

        private static void RestoreSegmentStates(GenerationCacheManifest manifest, Transform generatedRoot)
        {
            if (manifest.segmentStates != null && manifest.segmentStates.Count > 0)
            {
                for (var i = 0; i < manifest.segmentStates.Count; i++)
                {
                    var entry = manifest.segmentStates[i];
                    if (entry == null || string.IsNullOrWhiteSpace(entry.path))
                        continue;

                    var target = GetOrCreateRelativeTransform(generatedRoot, entry.path);
                    var segment = target.GetComponent<PlanetSegment>();
                    if (!segment)
                        segment = target.gameObject.AddComponent<PlanetSegment>();

                    segment.RestoreState(
                        entry.material,
                        entry.initialMaterialPoints,
                        entry.currentMaterialPoints,
                        entry.segmentAreaUnits,
                        entry.isCoreSegment,
                        entry.isDestroyed);
                }

                return;
            }

            BootstrapSegmentStatesFromRenderers(generatedRoot);
        }

        private static void EnsureColliderForPlanetPart(GameObject target, SpriteRenderer renderer, RendererCacheEntry entry)
        {
            if (target == null || renderer == null || renderer.sprite == null)
                return;

            if (entry.useSegmentMaskRendering)
            {
                ConfigureMaskedSegmentCollider(target, entry.maskInnerRadiusNormalized, entry.maskOuterRadiusNormalized, entry.maskHalfAngleDeg * 2f);
                return;
            }

            var path = entry.path;
            var isCenter = string.Equals(path, "CenterCircle", StringComparison.Ordinal);
            if (isCenter)
            {
                var circle = target.GetComponent<CircleCollider2D>();
                if (!circle)
                    circle = target.AddComponent<CircleCollider2D>();

                var bounds = renderer.sprite.bounds;
                circle.radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
                circle.offset = Vector2.zero;
                circle.isTrigger = false;
                return;
            }

            var polygon = target.GetComponent<PolygonCollider2D>();
            if (!polygon)
                polygon = target.AddComponent<PolygonCollider2D>();

            polygon.isTrigger = false;
        }

        private static void ConfigureMaskedSegmentCollider(GameObject target, float innerNorm, float outerNorm, float angleDeg)
        {
            var collider = target.GetComponent<PolygonCollider2D>();
            if (!collider)
                collider = target.AddComponent<PolygonCollider2D>();

            var halfAngleRad = Mathf.Deg2Rad * Mathf.Clamp(angleDeg * 0.5f, 0.1f, 179.9f);
            var arcSteps = Mathf.Max(2, Mathf.CeilToInt(angleDeg / 12f));
            var points = new List<Vector2>(arcSteps * 2 + 2);

            for (var i = 0; i <= arcSteps; i++)
            {
                var t = i / (float)arcSteps;
                var angle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outerNorm);
            }

            if (innerNorm > 0.0001f)
            {
                for (var i = arcSteps; i >= 0; i--)
                {
                    var t = i / (float)arcSteps;
                    var angle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
                    points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * innerNorm);
                }
            }
            else
            {
                points.Add(Vector2.zero);
            }

            collider.pathCount = 1;
            collider.SetPath(0, points.ToArray());
            collider.isTrigger = false;
        }

        private static Texture2D ResolveMaskTexture(List<Texture2D> textures, int textureIndex)
        {
            if (textures == null || textureIndex < 0 || textureIndex >= textures.Count)
                return null;

            return textures[textureIndex];
        }

        private static void UpdateGeneratorOuterRadiusFromRenderers(Transform owner, Transform generatedRoot)
        {
            if (!owner || !generatedRoot)
                return;

            var generator = owner.GetComponent<PlanetGenerator>();
            if (!generator)
                return;

            var rootPosition = (Vector2)generatedRoot.position;
            var renderers = generatedRoot.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            var maxRadius = 0f;

            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer || !renderer.sprite)
                    continue;

                var centerDistance = ((Vector2)renderer.transform.position - rootPosition).magnitude;
                var spriteRadiusLocal = Mathf.Max(renderer.sprite.bounds.extents.x, renderer.sprite.bounds.extents.y);
                var scale = renderer.transform.lossyScale;
                var scaleMultiplier = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                var worldSpriteRadius = spriteRadiusLocal * scaleMultiplier;
                maxRadius = Mathf.Max(maxRadius, centerDistance + worldSpriteRadius);
            }

            if (maxRadius > 0f)
                generator.SetGeneratedOuterRadiusUnits(maxRadius);
        }

        private static Transform GetOrCreateRelativeTransform(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root;

            var segments = path.Split('/');
            var current = root;
            for (var i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                var child = current.Find(segment);
                if (child == null)
                {
                    var go = new GameObject(segment);
                    go.transform.SetParent(current, false);
                    child = go.transform;
                }

                current = child;
            }

            return current;
        }

        private static void BootstrapSegmentStatesFromRenderers(Transform generatedRoot)
        {
            var renderers = generatedRoot.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            for (var i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (!renderer || !renderer.sprite)
                    continue;

                var objectName = renderer.gameObject.name;
                var isCenter = string.Equals(objectName, "CenterCircle", StringComparison.Ordinal);
                var isSegment = objectName.StartsWith("Segment_", StringComparison.Ordinal);
                if (!isCenter && !isSegment)
                    continue;

                var segment = renderer.GetComponent<PlanetSegment>();
                if (segment)
                    continue;

                var material = isCenter ? PlanetSegmentMaterial.Magma : PlanetSegmentMaterial.Stone;
                var areaUnits = EstimateAreaUnits(renderer, isCenter);
                var initialPoints = PlanetSegmentPointsCalculator.CalculatePoints(areaUnits, material);
                var isDestroyed = !renderer.enabled;
                var currentPoints = isDestroyed ? 0 : initialPoints;

                segment = renderer.gameObject.AddComponent<PlanetSegment>();
                segment.RestoreState(material, initialPoints, currentPoints, areaUnits, isCenter, isDestroyed);
            }
        }

        private static float EstimateAreaUnits(SpriteRenderer renderer, bool isCenter)
        {
            var bounds = renderer.sprite.bounds;
            if (isCenter)
            {
                var radius = Mathf.Max(bounds.extents.x, bounds.extents.y);
                return PlanetSegmentPointsCalculator.CalculateCircleAreaUnits(radius);
            }

            var width = Mathf.Max(0f, bounds.size.x);
            var height = Mathf.Max(0f, bounds.size.y);
            return width * height * 0.5f;
        }

        private static RuntimeCacheState GetOrCreateRuntimeState(int ownerId)
        {
            if (RuntimeStatesByOwnerId.TryGetValue(ownerId, out var state))
                return state;

            state = new RuntimeCacheState();
            RuntimeStatesByOwnerId[ownerId] = state;
            return state;
        }

        [Serializable]
        private class GenerationCacheManifest
        {
            public int version;
            public string cacheId;
            public string generatedRootName;
            public string sourceObjectName;
            public string savedAtUtc;
            public List<TextureCacheEntry> textures = new();
            public List<SpriteCacheEntry> sprites = new();
            public List<RendererCacheEntry> renderers = new();
            public List<SegmentStateCacheEntry> segmentStates = new();
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

        private sealed class RuntimeCacheState
        {
            public readonly List<Texture2D> LoadedTextures = new();
            public readonly List<Sprite> LoadedSprites = new();

            public void DestroyLoadedAssets()
            {
                foreach (var sprite in LoadedSprites.Where(sprite => sprite))
                {
                    if (Application.isPlaying)
                        Object.Destroy(sprite);
                    else
                        Object.DestroyImmediate(sprite);
                }

                foreach (var texture in LoadedTextures.Where(texture => texture))
                {
                    if (Application.isPlaying)
                        Object.Destroy(texture);
                    else
                        Object.DestroyImmediate(texture);
                }

                LoadedSprites.Clear();
                LoadedTextures.Clear();
            }
        }
    }
}
