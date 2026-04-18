using System.Collections.Generic;
using App.Planets.Core;
using UnityEngine;

namespace App.Planets.Generation
{
    internal sealed class PlanetSpriteFactory
    {
        internal readonly struct Settings
        {
            public Settings(
                float pixelsPerUnit,
                FilterMode filterMode,
                float textureTileSizeUnits,
                Vector2 textureUvOffset,
                bool enableSegmentOutline,
                Color segmentOutlineColor,
                float segmentOutlineWidthPixels)
            {
                PixelsPerUnit = pixelsPerUnit;
                FilterMode = filterMode;
                TextureTileSizeUnits = textureTileSizeUnits;
                TextureUvOffset = textureUvOffset;
                EnableSegmentOutline = enableSegmentOutline;
                SegmentOutlineColor = segmentOutlineColor;
                SegmentOutlineWidthPixels = segmentOutlineWidthPixels;
            }

            public float PixelsPerUnit { get; }
            public FilterMode FilterMode { get; }
            public float TextureTileSizeUnits { get; }
            public Vector2 TextureUvOffset { get; }
            public bool EnableSegmentOutline { get; }
            public Color SegmentOutlineColor { get; }
            public float SegmentOutlineWidthPixels { get; }
        }

        private readonly List<Texture2D> _generatedTextures = new List<Texture2D>();
        private readonly HashSet<int> _warnedUnreadableTextures = new HashSet<int>();

        private MonoBehaviour _context;
        private string _ownerName;
        private Settings _settings;

        public PlanetSpriteFactory(MonoBehaviour context, string ownerName, Settings settings)
        {
            Reconfigure(context, ownerName, settings);
        }

        public void Reconfigure(MonoBehaviour context, string ownerName, Settings settings)
        {
            _context = context;
            _ownerName = ownerName;
            _settings = settings;
        }

        public void ResetWarnings()
        {
            _warnedUnreadableTextures.Clear();
        }

        public void DestroyGeneratedTextures()
        {
            foreach (var texture in _generatedTextures)
            {
                if (!texture)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(texture);
                else
                    Object.DestroyImmediate(texture);
            }

            _generatedTextures.Clear();
        }

        public Sprite CreateCircleSprite(float radiusUnits, Color fallbackColor, PlanetSegmentProfile profile)
        {
            var radiusPx = Mathf.Max(1, Mathf.CeilToInt(radiusUnits * _settings.PixelsPerUnit));
            var diameterPx = radiusPx * 2;
            var texture = CreateTexture(diameterPx, diameterPx, $"Circle_{_ownerName}");
            var pixels = new Color[diameterPx * diameterPx];
            var center = new Vector2(radiusPx - 0.5f, radiusPx - 0.5f);
            var radiusSqr = radiusPx * radiusPx;
            var fillTexture = GetReadableTextureOrNull(profile);
            var tint = profile ? profile.Tint : Color.white;
            var textureScale = profile ? profile.TextureScale : 1f;

            for (var y = 0; y < diameterPx; y++)
            {
                for (var x = 0; x < diameterPx; x++)
                {
                    var offset = new Vector2(x - center.x, y - center.y);
                    var index = y * diameterPx + x;
                    if (offset.sqrMagnitude > radiusSqr)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    var uvSourceUnits = offset / _settings.PixelsPerUnit;
                    pixels[index] = SampleFillColor(fillTexture, tint, fallbackColor, uvSourceUnits, textureScale);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, diameterPx, diameterPx), new Vector2(0.5f, 0.5f), _settings.PixelsPerUnit);
        }

        public Sprite CreateRingSegmentSprite(float innerRadiusUnits, float outerRadiusUnits, float angleDeg, Color fallbackColor, PlanetSegmentProfile profile)
        {
            var outerPx = Mathf.Max(1, Mathf.CeilToInt(outerRadiusUnits * _settings.PixelsPerUnit));
            var innerPx = Mathf.Max(0, Mathf.CeilToInt(innerRadiusUnits * _settings.PixelsPerUnit));
            var size = outerPx * 2;
            var texture = CreateTexture(size, size, $"LayerSegment_{_ownerName}");
            var pixels = new Color[size * size];
            var center = new Vector2(outerPx - 0.5f, outerPx - 0.5f);
            var innerSqr = innerPx * innerPx;
            var outerSqr = outerPx * outerPx;
            var halfAngle = angleDeg * 0.5f;
            var outlineWidth = Mathf.Max(1f, _settings.SegmentOutlineWidthPixels);
            var fillTexture = GetReadableTextureOrNull(profile);
            var tint = profile ? profile.Tint : Color.white;
            var textureScale = profile ? profile.TextureScale : 1f;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var direction = new Vector2(x - center.x, y - center.y);
                    var distanceSqr = direction.sqrMagnitude;
                    var index = y * size + x;

                    if (distanceSqr < innerSqr || distanceSqr > outerSqr)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    var pixelAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                    var insideSegment = Mathf.Abs(Mathf.DeltaAngle(0f, pixelAngle)) <= halfAngle;
                    if (!insideSegment)
                    {
                        pixels[index] = Color.clear;
                        continue;
                    }

                    if (_settings.EnableSegmentOutline)
                    {
                        var radius = Mathf.Sqrt(distanceSqr);
                        var nearInner = innerPx > 0 && Mathf.Abs(radius - innerPx) <= outlineWidth;
                        var nearOuter = Mathf.Abs(outerPx - radius) <= outlineWidth;
                        var angularWidthDeg = Mathf.Rad2Deg * (outlineWidth / Mathf.Max(1f, radius));
                        var nearAngleEdge = halfAngle - Mathf.Abs(Mathf.DeltaAngle(0f, pixelAngle)) <= angularWidthDeg;

                        if (nearInner || nearOuter || nearAngleEdge)
                        {
                            pixels[index] = _settings.SegmentOutlineColor;
                            continue;
                        }
                    }

                    var uvSourceUnits = direction / _settings.PixelsPerUnit;
                    pixels[index] = SampleFillColor(fillTexture, tint, fallbackColor, uvSourceUnits, textureScale);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), _settings.PixelsPerUnit);
        }

        private Texture2D GetReadableTextureOrNull(PlanetSegmentProfile profile)
        {
            if (profile == null || profile.FillTexture == null)
                return null;

            var texture = profile.FillTexture;
            if (texture.isReadable)
                return texture;

            var textureId = texture.GetInstanceID();
            if (_warnedUnreadableTextures.Add(textureId))
                Debug.LogWarning($"Texture '{texture.name}' is not Read/Write enabled, fallback color will be used.", _context);

            return null;
        }

        private Color SampleFillColor(Texture2D fillTexture, Color tint, Color fallbackColor, Vector2 sourceUnits, float textureScale)
        {
            if (fillTexture == null)
                return fallbackColor;

            var tileSize = Mathf.Max(0.001f, _settings.TextureTileSizeUnits);
            var profileScale = Mathf.Clamp(textureScale, 0.1f, 16f);
            var u = sourceUnits.x / tileSize * profileScale + _settings.TextureUvOffset.x;
            var v = sourceUnits.y / tileSize * profileScale + _settings.TextureUvOffset.y;
            var textureColor = fillTexture.GetPixelBilinear(Mathf.Repeat(u, 1f), Mathf.Repeat(v, 1f));
            return textureColor * tint;
        }

        private Texture2D CreateTexture(int width, int height, string name)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                filterMode = _settings.FilterMode,
                wrapMode = TextureWrapMode.Clamp
            };

            _generatedTextures.Add(texture);
            return texture;
        }
    }
}
