using UnityEngine;

namespace App.Src.Planets
{
    public class PlanetGenerator : MonoBehaviour
    {
        public void Start()
        {
            
            var a = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), Vector2.zero, pixelsPerUnit: 1f);
            
            var rend = GetComponent<SpriteRenderer>();
            rend.sprite = a;
            rend.color = Color.aquamarine;
        }
    }
}