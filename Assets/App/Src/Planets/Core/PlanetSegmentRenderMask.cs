using UnityEngine;

namespace App.Planets.Core
{
    [DisallowMultipleComponent]
    public sealed class PlanetSegmentRenderMask : MonoBehaviour
    {
        private const string ShaderName = "App/PlanetSegmentMask";
        private static Material _sharedMaskMaterial;

        [SerializeField] private bool useMaskRendering;
        [SerializeField] [Range(0f, 0.5f)] private float innerRadiusNormalized;
        [SerializeField] [Range(0f, 0.5f)] private float outerRadiusNormalized = 0.5f;
        [SerializeField] [Range(0f, 180f)] private float halfAngleDeg = 45f;
        [SerializeField] private Texture2D fillTexture;
        [SerializeField] private Color fillColor = Color.white;
        [SerializeField] [Range(0.1f, 16f)] private float textureScale = 1f;
        [SerializeField] [Range(0.001f, 10f)] private float textureTileSizeUnits = 0.25f;
        [SerializeField] private Vector2 textureUvOffset = Vector2.zero;
        [SerializeField] private bool enableOutline = true;
        [SerializeField] private Color outlineColor = Color.black;
        [SerializeField] [Range(0f, 0.2f)] private float outlineWidthNormalized = 0.01f;

        public bool UseMaskRendering => useMaskRendering;
        public float InnerRadiusNormalized => innerRadiusNormalized;
        public float OuterRadiusNormalized => outerRadiusNormalized;
        public float HalfAngleDeg => halfAngleDeg;
        public Texture2D FillTexture => fillTexture;
        public Color FillColor => fillColor;
        public float TextureScale => textureScale;
        public float TextureTileSizeUnits => textureTileSizeUnits;
        public Vector2 TextureUvOffset => textureUvOffset;
        public bool EnableOutline => enableOutline;
        public Color OutlineColor => outlineColor;
        public float OutlineWidthNormalized => outlineWidthNormalized;

        public void Configure(
            float innerNorm,
            float outerNorm,
            float halfAngle,
            Texture2D texture,
            Color color,
            float fillTextureScale,
            bool outlineEnabled,
            Color outline,
            float outlineNorm,
            float tileSizeUnits = 0.25f,
            Vector2 uvOffset = default)
        {
            useMaskRendering = true;
            innerRadiusNormalized = Mathf.Clamp(innerNorm, 0f, 0.5f);
            outerRadiusNormalized = Mathf.Clamp(outerNorm, 0f, 0.5f);
            halfAngleDeg = Mathf.Clamp(halfAngle, 0f, 180f);
            fillTexture = texture;
            fillColor = color;
            textureScale = Mathf.Clamp(fillTextureScale, 0.1f, 16f);
            textureTileSizeUnits = Mathf.Max(0.001f, tileSizeUnits);
            textureUvOffset = uvOffset;
            enableOutline = outlineEnabled;
            outlineColor = outline;
            outlineWidthNormalized = Mathf.Clamp(outlineNorm, 0f, 0.2f);
        }

        public void Disable()
        {
            useMaskRendering = false;
        }

        public void Apply(SpriteRenderer renderer, Texture mainTexture = null)
        {
            if (!renderer)
                return;

            if (!useMaskRendering)
                return;

            var material = GetOrCreateSharedMaskMaterial();
            if (!material)
                return;

            renderer.sharedMaterial = material;

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetFloat("_InnerRadiusNorm", innerRadiusNormalized);
            block.SetFloat("_OuterRadiusNorm", outerRadiusNormalized);
            block.SetFloat("_HalfAngleDeg", halfAngleDeg);
            block.SetColor("_Color", fillColor);
            var lossyScale = renderer.transform.lossyScale;
            var tileSize = Mathf.Max(0.001f, textureTileSizeUnits);
            var repeatX = Mathf.Max(0.001f, Mathf.Abs(lossyScale.x) / tileSize * textureScale);
            var repeatY = Mathf.Max(0.001f, Mathf.Abs(lossyScale.y) / tileSize * textureScale);
            block.SetVector("_TextureTiling", new Vector4(repeatX, repeatY, textureUvOffset.x, textureUvOffset.y));
            block.SetFloat("_EnableOutline", enableOutline ? 1f : 0f);
            block.SetColor("_OutlineColor", outlineColor);
            block.SetFloat("_OutlineNorm", outlineWidthNormalized);
            var textureToUse = mainTexture ? mainTexture : fillTexture;
            if (textureToUse != null)
                block.SetTexture("_MainTex", textureToUse);
            renderer.SetPropertyBlock(block);
        }

        private static Material GetOrCreateSharedMaskMaterial()
        {
            if (_sharedMaskMaterial)
                return _sharedMaskMaterial;

            var shader = Shader.Find(ShaderName);
            if (!shader)
                return null;

            _sharedMaskMaterial = new Material(shader)
            {
                name = "PlanetSegmentMask_Material"
            };
            return _sharedMaskMaterial;
        }
    }
}
