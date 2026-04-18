using UnityEngine;

namespace App.Planets.Core
{
    public class PlanetSegmentProfile : MonoBehaviour
    {
        [SerializeField] private Texture2D fillTexture;
        [SerializeField] private Color tint = Color.white;
        [SerializeField] [Range(0.1f, 16f)] private float textureScale = 1f;
        [SerializeField] private PlanetSegmentMaterial material = PlanetSegmentMaterial.Stone;

        public Texture2D FillTexture => fillTexture;
        public Color Tint => tint;
        public float TextureScale => Mathf.Clamp(textureScale, 0.1f, 16f);
        public PlanetSegmentMaterial Material => material;
    }
}
