using App.Surfaces.Base;
using UnityEngine;

namespace App.Planets
{
    public abstract class Planet : MonoBehaviour, ISurface
    {
        public Vector2 GetForwardDirection(Vector2 worldPosition)
        {
            
            return Vector2.zero;
        }
    }
}