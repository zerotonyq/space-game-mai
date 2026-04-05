using UnityEngine;

namespace App.Planets.Core
{
    public class PlanetSegmentProfile : MonoBehaviour
    {
        [SerializeField] private Texture2D fillTexture;
        [SerializeField] private Color tint = Color.white;

        public Texture2D FillTexture => fillTexture;
        public Color Tint => tint;
    }
}
