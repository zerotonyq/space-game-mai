using App.Infrastructure.DI;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace App.Entities.Config
{
    [CreateAssetMenu(
        fileName = "WorldCharacterSpawnSettings",
        menuName = "App/Config/World Character Spawn Settings")]
    public class WorldCharacterSpawnSettings : ScriptableObject
    {
        [Header("Prefabs")]
        public AssetReferenceGameObject heroAssetReference;
        public AssetReferenceGameObject peacefulAssetReference;
        public AssetReferenceGameObject villainAssetReference;

        [Header("Per Planet Population")]
        [Min(0)] public int minEntitiesPerPlanet = 1;
        [Min(0)] public int maxEntitiesPerPlanet = 5;
        [Min(0)] public int perPlanetCountJitter = 1;
        [Range(0f, 1f)] public float villainPlanetChance = 0.5f;

        [Header("Orbit Defaults")]
        [Min(0f)] public float heroAltitudeFromSurface = 2f;
        [Min(0f)] public float npcAltitudeMinFromSurface = 1f;
        [Min(0f)] public float npcAltitudeMaxFromSurface = 4f;
        [Min(0.01f)] public float heroAngularSpeedDegPerSecond = 25f;
        [Min(0.01f)] public float npcAngularSpeedMinDegPerSecond = 15f;
        [Min(0.01f)] public float npcAngularSpeedMaxDegPerSecond = 40f;
        public OrbitRotationDirection heroRotationDirection = OrbitRotationDirection.Clockwise;
        public bool randomizeNpcDirection = true;

        [Header("Persistence")]
        public string entitiesStateFileName = "entities_state.json";
        public bool destroyEntitiesOnWorldUnload = true;
        public bool verboseLogging = true;
    }
}
