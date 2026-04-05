using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using App.Planets.Core;
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
            RestoreRenderers(manifest, generatedRoot, sprites);

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

        private static void RestoreRenderers(GenerationCacheManifest manifest, Transform generatedRoot, List<Sprite> sprites)
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

                EnsureColliderForPlanetPart(target.gameObject, renderer, entry.path);
            }
        }

        private static void EnsureColliderForPlanetPart(GameObject target, SpriteRenderer renderer, string path)
        {
            if (target == null || renderer == null || renderer.sprite == null)
                return;

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
