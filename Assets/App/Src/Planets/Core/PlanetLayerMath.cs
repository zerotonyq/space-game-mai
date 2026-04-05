using System.Collections.Generic;
using App.Planets.Generation;
using UnityEngine;

namespace App.Planets.Core
{
    internal static class PlanetLayerMath
    {
        public static float GetLayerRotation(IReadOnlyList<PlanetLayerRotationSetting> layerRotations, int layerIndex)
        {
            if (layerIndex >= layerRotations.Count)
                return 0f;

            var setting = layerRotations[layerIndex];
            return setting?.Degrees ?? 0f;
        }

        public static Color GetLayerColor(int layerIndex, int layerCount)
        {
            var hue = layerCount <= 1 ? 0.55f : Mathf.Lerp(0.55f, 0.05f, layerIndex / (float)(layerCount - 1));
            return Color.HSVToRGB(hue, 0.45f, 1f);
        }
    }
}
