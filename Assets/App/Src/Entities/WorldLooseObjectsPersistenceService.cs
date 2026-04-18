using System;
using System.Collections.Generic;
using System.IO;
using App.Entities.Config;
using App.Infrastructure.DI.Base;
using App.Planets.Core;
using App.Planets.Generation;
using App.Planets.Persistence;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace App.Entities
{
    public class WorldLooseObjectsPersistenceService : IGameService, IDisposable
    {
        private const string GeneratedRootName = "__GeneratedPlanet";
        private const string DefaultStateFileName = "world_objects_state.json";

        private readonly PlanetWorldManager _worldManager;
        private readonly PlanetWorldService _worldService;
        private readonly WorldCharacterSpawnSettings _settings;
        private readonly IHeroOrbitRuntimeProvider _heroOrbitRuntimeProvider;

        public WorldLooseObjectsPersistenceService(
            PlanetWorldManager worldManager,
            PlanetWorldService worldService,
            WorldCharacterSpawnSettings settings,
            IHeroOrbitRuntimeProvider heroOrbitRuntimeProvider)
        {
            _worldManager = worldManager;
            _worldService = worldService;
            _settings = settings;
            _heroOrbitRuntimeProvider = heroOrbitRuntimeProvider;
        }

        public UniTask Initialize()
        {
            if (_worldManager != null)
            {
                _worldManager.WorldLoaded += OnWorldLoaded;
                _worldManager.WorldCreated += OnWorldCreated;
                _worldManager.WorldUnloaded += OnWorldUnloaded;
            }

            return UniTask.CompletedTask;
        }

        public void Dispose()
        {
            if (_worldManager != null)
            {
                _worldManager.WorldLoaded -= OnWorldLoaded;
                _worldManager.WorldCreated -= OnWorldCreated;
                _worldManager.WorldUnloaded -= OnWorldUnloaded;
            }
        }

        public void SaveCurrentWorldObjectsStateNow()
        {
            if (_worldManager == null || string.IsNullOrWhiteSpace(_worldManager.CurrentWorldId))
                return;

            SaveWorldObjectsState(_worldManager.CurrentWorldId);
        }

        private UniTask OnWorldCreated(string worldId)
        {
            DestroyAllLooseObjects();
            return UniTask.CompletedTask;
        }

        private UniTask OnWorldLoaded(string worldId)
        {
            DestroyAllLooseObjects();
            TryLoadWorldObjectsState(worldId);
            return UniTask.CompletedTask;
        }

        private UniTask OnWorldUnloaded(string worldId)
        {
            // WorldUnloaded is fired after planets are already removed; saving here may overwrite valid state.
            if (_worldManager != null && _worldManager.ActivePlanetCount > 0)
                SaveWorldObjectsState(worldId);

            if (_settings != null && _settings.destroyWorldObjectsOnWorldUnload)
                DestroyAllLooseObjects();

            return UniTask.CompletedTask;
        }

        private void SaveWorldObjectsState(string worldId)
        {
            if (_worldService == null || string.IsNullOrWhiteSpace(worldId) || _settings == null)
                return;

            var worldRoot = _worldService.GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
                return;

            Directory.CreateDirectory(worldRoot);
            var statePath = Path.Combine(worldRoot, ResolveStateFileName());

            var state = new WorldLooseObjectsState();
            var planetIdsByCenter = BuildPlanetIdsByCenterMap();

            var drills = UnityEngine.Object.FindObjectsByType<DrillProjectile>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var drill in drills)
            {
                if (drill == null)
                    continue;

                if (!TryResolveDrillType(drill.DrillMaterial, out var drillType))
                    continue;

                var entry = new DrillEntry
                {
                    type = drillType,
                    position = drill.transform.position,
                    rotation = drill.transform.rotation,
                    isDrilling = drill.IsDrillingActive,
                    isLaunched = drill.IsProjectileLaunched,
                    launchedDirection = drill.ProjectileDirection,
                    minedPoints = drill.MinedPoints,
                    damageTickTimer = drill.DamageTickTimer,
                    dropMinedChunkOnComplete = drill.DropMinedChunkOnComplete
                };

                if (drill.IsDrillingActive && drill.TargetPlanet != null && drill.TargetSegment != null)
                {
                    if (planetIdsByCenter.TryGetValue(drill.TargetPlanet.transform, out var planetId))
                    {
                        entry.targetPlanetId = planetId;
                        entry.targetSegmentPath = GetSegmentPathRelativeToGeneratedRoot(drill.TargetSegment.transform, drill.TargetPlanet.transform);
                    }
                }

                state.drills.Add(entry);
            }

            var chunks = UnityEngine.Object.FindObjectsByType<MinedMaterialChunk>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                    continue;

                var entry = new ChunkEntry
                {
                    material = chunk.Material,
                    points = chunk.MaterialPoints,
                    position = chunk.transform.position,
                    rotation = chunk.transform.rotation
                };

                var orbit = chunk.GetComponent<PlanetOrbitMovement>();
                if (orbit != null && orbit.OrbitCenter != null && planetIdsByCenter.TryGetValue(orbit.OrbitCenter, out var planetId))
                {
                    entry.hasOrbit = true;
                    entry.orbitPlanetId = planetId;
                    entry.orbitAltitude = orbit.AltitudeFromSurface;
                    entry.orbitAngleDeg = orbit.CurrentAngleDeg;
                }

                state.chunks.Add(entry);
            }

            File.WriteAllText(statePath, JsonUtility.ToJson(state, true));
        }

        private bool TryLoadWorldObjectsState(string worldId)
        {
            if (_worldService == null || string.IsNullOrWhiteSpace(worldId) || _settings == null)
                return false;

            var worldRoot = _worldService.GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
                return false;

            var statePath = Path.Combine(worldRoot, ResolveStateFileName());
            if (!File.Exists(statePath))
                return false;

            var json = File.ReadAllText(statePath);
            var state = JsonUtility.FromJson<WorldLooseObjectsState>(json);
            if (state == null)
                return false;

            RestoreDrills(state.drills);
            RestoreChunks(state.chunks);
            return true;
        }

        private void RestoreDrills(List<DrillEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            foreach (var entry in entries)
            {
                if (!TryGetDrillPrefab(entry.type, out var prefab) || prefab == null)
                    continue;

                var instance = UnityEngine.Object.Instantiate(prefab, entry.position, entry.rotation, null);
                if (!instance)
                    continue;

                if (!entry.isDrilling)
                {
                    if (entry.isLaunched && entry.launchedDirection.sqrMagnitude > 0.000001f)
                        instance.Launch(entry.launchedDirection);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(entry.targetPlanetId) || string.IsNullOrWhiteSpace(entry.targetSegmentPath))
                {
                    UnityEngine.Object.Destroy(instance.gameObject);
                    continue;
                }

                if (!_worldManager.TryGetPlanetGeneratorById(entry.targetPlanetId, out var planet))
                {
                    UnityEngine.Object.Destroy(instance.gameObject);
                    continue;
                }

                var segment = ResolveSegment(planet, entry.targetSegmentPath);
                if (segment == null || segment.IsDestroyed)
                {
                    UnityEngine.Object.Destroy(instance.gameObject);
                    continue;
                }

                instance.RestoreDrillingState(
                    planet,
                    segment,
                    entry.position,
                    entry.minedPoints,
                    entry.damageTickTimer,
                    entry.dropMinedChunkOnComplete);
            }
        }

        private void RestoreChunks(List<ChunkEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return;

            foreach (var entry in entries)
            {
                MinedMaterialChunk chunk;
                if (_settings != null && _settings.minedMaterialChunkPrefab != null)
                    chunk = UnityEngine.Object.Instantiate(_settings.minedMaterialChunkPrefab, entry.position, entry.rotation, null);
                else
                {
                    var chunkObject = new GameObject("MinedMaterialChunk");
                    chunkObject.transform.position = entry.position;
                    chunkObject.transform.rotation = entry.rotation;
                    chunk = chunkObject.AddComponent<MinedMaterialChunk>();
                }

                var chunkObjectTransform = chunk.transform;
                chunk.Initialize(entry.material, entry.points);

                if (!entry.hasOrbit || string.IsNullOrWhiteSpace(entry.orbitPlanetId))
                    continue;

                if (!_worldManager.TryGetPlanetGeneratorById(entry.orbitPlanetId, out var planet))
                    continue;

                var orbit = chunkObjectTransform.GetComponent<PlanetOrbitMovement>();
                if (!orbit)
                    orbit = chunkObjectTransform.gameObject.AddComponent<PlanetOrbitMovement>();

                orbit.SetAngularSpeedDegPerSecond(0f);
                orbit.SetAltitudeFromSurface(entry.orbitAltitude);
                orbit.SetOrbitCenter(planet.transform, planet.EstimatedOuterRadiusUnits);
                orbit.SetCurrentAngleDeg(entry.orbitAngleDeg);
                orbit.SnapToOrbitPosition();
            }
        }

        private static PlanetSegment ResolveSegment(PlanetGenerator planet, string segmentPath)
        {
            if (planet == null || string.IsNullOrWhiteSpace(segmentPath))
                return null;

            var generatedRoot = planet.transform.Find(GeneratedRootName);
            if (!generatedRoot)
                return null;

            var segmentTransform = generatedRoot.Find(segmentPath);
            if (!segmentTransform)
                return null;

            return segmentTransform.GetComponent<PlanetSegment>();
        }

        private static string GetSegmentPathRelativeToGeneratedRoot(Transform segment, Transform planetRoot)
        {
            if (segment == null || planetRoot == null)
                return null;

            var generatedRoot = planetRoot.Find(GeneratedRootName);
            if (!generatedRoot)
                return null;

            var parts = new List<string>();
            var current = segment;
            while (current != null && current != generatedRoot)
            {
                parts.Add(current.name);
                current = current.parent;
            }

            if (current != generatedRoot)
                return null;

            parts.Reverse();
            return string.Join("/", parts);
        }

        private Dictionary<Transform, string> BuildPlanetIdsByCenterMap()
        {
            var result = new Dictionary<Transform, string>();
            if (_worldManager == null)
                return result;

            var planets = new List<PlanetWorldManager.PlanetBinding>();
            if (_worldManager.GetActivePlanets(planets) <= 0)
                return result;

            foreach (var binding in planets)
            {
                if (binding.generator == null || string.IsNullOrWhiteSpace(binding.planetId))
                    continue;

                result[binding.generator.transform] = binding.planetId;
            }

            return result;
        }

        private bool TryGetDrillPrefab(DrillProjectileType type, out DrillProjectile prefab)
        {
            if (_settings != null)
            {
                switch (type)
                {
                    case DrillProjectileType.Type1:
                        prefab = _settings.drillType1Prefab;
                        return prefab != null;
                    case DrillProjectileType.Type2:
                        prefab = _settings.drillType2Prefab;
                        return prefab != null;
                    case DrillProjectileType.Type3:
                        prefab = _settings.drillType3Prefab;
                        return prefab != null;
                }
            }

            prefab = null;
            var hero = _heroOrbitRuntimeProvider?.CurrentHero;
            if (!hero)
                return false;

            var shooter = hero.GetComponent<CharacterProjectileShooter>();
            if (!shooter)
                return false;

            switch (type)
            {
                case DrillProjectileType.Type1:
                    prefab = shooter.DrillProjectileType1Prefab;
                    return prefab != null;
                case DrillProjectileType.Type2:
                    prefab = shooter.DrillProjectileType2Prefab;
                    return prefab != null;
                case DrillProjectileType.Type3:
                    prefab = shooter.DrillProjectileType3Prefab;
                    return prefab != null;
                default:
                    return false;
            }
        }

        private string ResolveStateFileName()
        {
            if (_settings == null || string.IsNullOrWhiteSpace(_settings.worldObjectsStateFileName))
                return DefaultStateFileName;

            return _settings.worldObjectsStateFileName;
        }

        private static bool TryResolveDrillType(PlanetSegmentMaterial material, out DrillProjectileType type)
        {
            switch (material)
            {
                case PlanetSegmentMaterial.IronOre:
                    type = DrillProjectileType.Type1;
                    return true;
                case PlanetSegmentMaterial.CobaltOre:
                    type = DrillProjectileType.Type2;
                    return true;
                case PlanetSegmentMaterial.TitaniumOre:
                    type = DrillProjectileType.Type3;
                    return true;
                default:
                    type = DrillProjectileType.Type1;
                    return false;
            }
        }

        private static void DestroyAllLooseObjects()
        {
            var drills = UnityEngine.Object.FindObjectsByType<DrillProjectile>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var drill in drills)
            {
                if (drill == null)
                    continue;
                UnityEngine.Object.Destroy(drill.gameObject);
            }

            var chunks = UnityEngine.Object.FindObjectsByType<MinedMaterialChunk>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var chunk in chunks)
            {
                if (chunk == null)
                    continue;
                UnityEngine.Object.Destroy(chunk.gameObject);
            }
        }

        [Serializable]
        private class WorldLooseObjectsState
        {
            public List<DrillEntry> drills = new();
            public List<ChunkEntry> chunks = new();
        }

        [Serializable]
        private class DrillEntry
        {
            public DrillProjectileType type;
            public Vector3 position;
            public Quaternion rotation;
            public bool isDrilling;
            public bool isLaunched;
            public Vector3 launchedDirection;
            public string targetPlanetId;
            public string targetSegmentPath;
            public int minedPoints;
            public float damageTickTimer;
            public bool dropMinedChunkOnComplete;
        }

        [Serializable]
        private class ChunkEntry
        {
            public PlanetSegmentMaterial material;
            public int points;
            public Vector3 position;
            public Quaternion rotation;
            public bool hasOrbit;
            public string orbitPlanetId;
            public float orbitAltitude;
            public float orbitAngleDeg;
        }

        private enum DrillProjectileType
        {
            Type1 = 0,
            Type2 = 1,
            Type3 = 2
        }
    }
}
