using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;

namespace App.Entities.Config
{
    [CreateAssetMenu(
        fileName = "HeroPlanetCinemachineCameraConfig",
        menuName = "App/Config/Hero Planet Cinemachine Camera Config")]
    public class HeroPlanetCinemachineCameraConfig : ScriptableObject
    {
        [Header("Screen Layout")]
        [Range(0f, 0.75f)] public float rightUiWidthFraction = 1f / 3f;
        public bool adjustScreenXForGameplayArea = true;

        [Header("Planet Fit")]
        public bool adjustOrthographicSize = true;
        [Min(1f)] public float planetFitPaddingMultiplier = 1.1f;
        
        [Header("Manual Zoom")]
        [Min(1.01f)] public float zoomStepMultiplier = 1.15f;
        [Min(0.01f)] public float minOrthographicSize = 5f;
        [Min(0.01f)] public float maxOrthographicSize = 200f;
        public AssetReferenceGameObject cameraAssetReference;
    }
}
