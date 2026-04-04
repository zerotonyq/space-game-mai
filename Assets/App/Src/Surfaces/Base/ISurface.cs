using UnityEngine;

namespace App.Surfaces.Base
{
    public interface ISurface
    {
        Vector2 GetForwardDirection(Vector2 worldPosition);
    }
}