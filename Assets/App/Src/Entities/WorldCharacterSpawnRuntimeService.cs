using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using App.Entities.Config;
using App.Infrastructure.DI;
using App.Infrastructure.DI.Base;
using App.Planets.Generation;
using App.Planets.Persistence;
using App.Signals;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using Zenject;

namespace App.Entities
{
    public interface IHeroOrbitRuntimeProvider
    {
        EntityHeroTag CurrentHero { get; }
        PlanetOrbitMovement CurrentHeroOrbitMovement { get; }
        event Action<EntityHeroTag, PlanetOrbitMovement> HeroOrbitChanged;
    }

    public class WorldCharacterSpawnRuntimeService : IGameService, IDisposable, IHeroOrbitRuntimeProvider
    {
        private enum SpawnedEntityKind
        {
            Hero = 0,
            Peaceful = 1,
            Villain = 2
        }

        private readonly PlanetWorldManager _worldManager;
        private readonly PlanetWorldService _worldService;
        private readonly WorldCharacterSpawnSettings _settings;

        private readonly List<SpawnedEntityRuntime> _spawnedEntities = new();
        private readonly List<PlanetWorldManager.PlanetBinding> _planetsBuffer = new();
        private EntityHeroTag _currentHero;
        private PlanetOrbitMovement _currentHeroOrbitMovement;

        public EntityHeroTag CurrentHero => _currentHero;
        public PlanetOrbitMovement CurrentHeroOrbitMovement => _currentHeroOrbitMovement;
        public event Action<EntityHeroTag, PlanetOrbitMovement> HeroOrbitChanged;
        public event Action<EntityHeroTag> HeroSpawned;
        
        
        public WorldCharacterSpawnRuntimeService(
            PlanetWorldManager worldManager,
            PlanetWorldService worldService,
            WorldCharacterSpawnSettings settings)
        {
            _worldManager = worldManager;
            _worldService = worldService;
            _settings = settings;
        }

        public async UniTask Initialize()
        {
            if (!_worldManager)
            {
                Debug.LogError("No world manager");
                return;
            }

            _worldManager.WorldCreated += OnWorldCreated;
            _worldManager.WorldLoaded += OnWorldLoaded;
            _worldManager.WorldUnloaded += OnWorldUnloaded;

            var worldId = _worldManager.CurrentWorldId;
            if (_worldManager.ActivePlanetCount <= 0)
                return;

            if (!(await TryLoadAndSpawnEntities(worldId)))
                await CreateNewWorldEntities(worldId);
        }

        public void Dispose()
        {
            if (_worldManager)
            {
                _worldManager.WorldCreated -= OnWorldCreated;
                _worldManager.WorldLoaded -= OnWorldLoaded;
                _worldManager.WorldUnloaded -= OnWorldUnloaded;
            }

            SaveCurrentWorldEntitiesState();
        }

        private async UniTask OnWorldCreated(string worldId)
        {
            DestroySpawnedEntities();
            await CreateNewWorldEntities(worldId);
        }

        private async UniTask OnWorldLoaded(string worldId)
        {
            DestroySpawnedEntities();
            if (!(await TryLoadAndSpawnEntities(worldId)))
                await CreateNewWorldEntities(worldId);
        }

        private UniTask OnWorldUnloaded(string worldId)
        {
            SaveEntitiesState(worldId);

            if (_settings.destroyEntitiesOnWorldUnload)
                DestroySpawnedEntities();

            return UniTask.CompletedTask;
        }

        private async UniTask CreateNewWorldEntities(string worldId)
        {
            if (!CollectPlanets())
            {
                if (_settings.verboseLogging)
                    Debug.LogWarning($"Entity spawn skipped for world '{worldId}': no planets available.");
                return;
            }

            await SpawnHeroOnRandomPlanet();
            await SpawnPlanetPopulation();
            SaveEntitiesState(worldId);
        }

        private async UniTask<bool> TryLoadAndSpawnEntities(string worldId)
        {
            if (!TryLoadState(worldId, out var state) || state.entities == null || state.entities.Count == 0)
                return false;

            var spawnedAny = false;
            foreach (var entry in state.entities)
            {
                if (entry == null)
                    continue;

                if (!TryParseKind(entry.kind, out var kind))
                    continue;

                if (string.IsNullOrWhiteSpace(entry.planetId))
                    continue;

                if (!_worldManager.TryGetPlanetGeneratorById(entry.planetId, out var planetGenerator))
                    continue;

                var instance = await SpawnEntity(kind, planetGenerator, entry.planetId);

                if (instance == null)
                    continue;

                instance.Movement.SetAltitudeFromSurface(Mathf.Max(0f, entry.altitudeFromSurface));
                instance.Movement.SetAngularSpeedDegPerSecond(Mathf.Max(0.01f, entry.angularSpeedDegPerSecond));
                instance.Movement.SetCurrentAngleDeg(entry.angleDeg);
                instance.Movement.SetRotationDirection(
                    ParseDirection(entry.direction, OrbitRotationDirection.Clockwise));
                instance.Movement.SnapToOrbitPosition();
                spawnedAny = true;
            }

            return spawnedAny;
        }

        private bool CollectPlanets()
        {
            _planetsBuffer.Clear();
            
            if (!_worldManager)
                return false;

            return _worldManager.GetActivePlanets(_planetsBuffer) > 0;
        }

        private async UniTask SpawnHeroOnRandomPlanet()
        {
            var planet = _planetsBuffer[UnityEngine.Random.Range(0, _planetsBuffer.Count)];
            var hero = await SpawnEntity(SpawnedEntityKind.Hero, planet.generator, planet.planetId);

            var heroTag = hero.GameObject.GetComponent<EntityHeroTag>();
            var altitude = heroTag.OrbitAltitudeFromSurface;
            var speed = heroTag.AngularSpeedDegPerSecond;
            var direction = heroTag.RotationDirection;
            var angle = heroTag.ResolveStartAngleDeg();

            hero.Movement.SetAltitudeFromSurface(altitude);
            hero.Movement.SetAngularSpeedDegPerSecond(speed);
            hero.Movement.SetRotationDirection(direction);
            hero.Movement.SetCurrentAngleDeg(angle);
            hero.Movement.SnapToOrbitPosition();
        }

        private async UniTask SpawnPlanetPopulation()
        {
            if (_planetsBuffer.Count == 0)
                return;

            var minRadius = float.MaxValue;
            var maxRadius = 0f;
            for (var i = 0; i < _planetsBuffer.Count; i++)
            {
                var radius = Mathf.Max(0f, _planetsBuffer[i].generator.EstimatedOuterRadiusUnits);
                minRadius = Mathf.Min(minRadius, radius);
                maxRadius = Mathf.Max(maxRadius, radius);
            }

            foreach (var planet in _planetsBuffer)
            {
                var radius = Mathf.Max(0f, planet.generator.EstimatedOuterRadiusUnits);
                var count = ResolvePlanetPopulationCount(radius, minRadius, maxRadius);
                
                var kind = UnityEngine.Random.value < _settings.villainPlanetChance
                    ? SpawnedEntityKind.Villain
                    : SpawnedEntityKind.Peaceful;

                for (var index = 0; index < count; index++)
                {
                    var entity = await SpawnEntity(kind, planet.generator, planet.planetId);
                    if (entity == null)
                        continue;

                    var altitude = UnityEngine.Random.Range(
                        _settings.npcAltitudeMinFromSurface,
                        _settings.npcAltitudeMaxFromSurface);
                    var speed = UnityEngine.Random.Range(
                        _settings.npcAngularSpeedMinDegPerSecond,
                        _settings.npcAngularSpeedMaxDegPerSecond);

                    var direction = _settings.randomizeNpcDirection && UnityEngine.Random.value < 0.5f
                        ? OrbitRotationDirection.Clockwise
                        : OrbitRotationDirection.CounterClockwise;

                    entity.Movement.SetAltitudeFromSurface(altitude);
                    entity.Movement.SetAngularSpeedDegPerSecond(speed);
                    entity.Movement.SetRotationDirection(direction);
                    entity.Movement.SetCurrentAngleDeg(UnityEngine.Random.Range(0f, 360f));
                    entity.Movement.SnapToOrbitPosition();
                }
            }
        }

        private int ResolvePlanetPopulationCount(float radius, float minRadius, float maxRadius)
        {
            var min = Mathf.Max(0, _settings.minEntitiesPerPlanet);
            var max = Mathf.Max(min, _settings.maxEntitiesPerPlanet);
            if (max <= min)
                return min;

            var normalized = Mathf.Approximately(maxRadius, minRadius)
                ? 0.5f
                : Mathf.InverseLerp(minRadius, maxRadius, radius);

            var baseCount = Mathf.RoundToInt(Mathf.Lerp(min, max, normalized));
            var jitter = Mathf.Max(0, _settings.perPlanetCountJitter);
            var withJitter = baseCount + UnityEngine.Random.Range(-jitter, jitter + 1);
            return Mathf.Clamp(withJitter, min, max);
        }

        private async UniTask<SpawnedEntityRuntime> SpawnEntity(SpawnedEntityKind kind, PlanetGenerator planetGenerator,
            string planetId)
        {
            if (!planetGenerator)
                return null;

            var reference = GetEntityAssetByKind(kind);

            var instance = await Addressables.InstantiateAsync(reference);

            var movement = instance.GetComponent<PlanetOrbitMovement>();

            movement.SetOrbitCenter(planetGenerator.transform, planetGenerator.EstimatedOuterRadiusUnits);

            var runtime = new SpawnedEntityRuntime
            {
                Kind = kind,
                PlanetId = planetId,
                GameObject = instance,
                Movement = movement
            };
            AttachOrbitCenterTracking(runtime);
            _spawnedEntities.Add(runtime);

            if (kind != SpawnedEntityKind.Hero)
                return runtime;

            _currentHero = instance.GetComponent<EntityHeroTag>();
            _currentHeroOrbitMovement = movement;
            HeroSpawned?.Invoke(_currentHero);
            NotifyHeroOrbitChanged();

            return runtime;
        }

        private AssetReferenceGameObject GetEntityAssetByKind(SpawnedEntityKind kind) =>
            kind switch
            {
                SpawnedEntityKind.Hero => _settings.heroAssetReference,
                SpawnedEntityKind.Peaceful => _settings.peacefulAssetReference,
                SpawnedEntityKind.Villain => _settings.villainAssetReference,
                _ => null
            };

        private void DestroySpawnedEntities()
        {
            var hadHero = _currentHero != null || _currentHeroOrbitMovement != null;

            _currentHero = null;
            _currentHeroOrbitMovement = null;
            if (hadHero)
                NotifyHeroOrbitChanged();

            for (var i = _spawnedEntities.Count - 1; i >= 0; i--)
            {
                var entity = _spawnedEntities[i];
                if (entity == null || !entity.GameObject)
                    continue;

                DetachOrbitCenterTracking(entity);
                Addressables.Release(entity.GameObject);
            }

            _spawnedEntities.Clear();
        }

        private void SaveCurrentWorldEntitiesState()
        {
            if (!_worldManager || string.IsNullOrWhiteSpace(_worldManager.CurrentWorldId))
                return;

            SaveEntitiesState(_worldManager.CurrentWorldId);
        }

        private void SaveEntitiesState(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId) || !_worldService)
                return;

            var worldRoot = _worldService.GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
                return;

            Directory.CreateDirectory(worldRoot);
            var statePath = Path.Combine(worldRoot, _settings.entitiesStateFileName);

            var state = new WorldEntitiesState();
            var planetIdsByCenter = BuildPlanetIdsByCenterMap();

            foreach (var entity in _spawnedEntities)
            {
                if (entity == null || entity.Movement == null)
                    continue;

                var planetId = ResolveCurrentPlanetId(entity, planetIdsByCenter);
                entity.PlanetId = planetId;

                state.entities.Add(new EntityStateEntry
                {
                    kind = (int)entity.Kind,
                    planetId = planetId,
                    altitudeFromSurface = entity.Movement.AltitudeFromSurface,
                    angularSpeedDegPerSecond = entity.Movement.AngularSpeedDegPerSecond,
                    angleDeg = entity.Movement.CurrentAngleDeg,
                    direction = (int)entity.Movement.RotationDirection
                });
            }

            File.WriteAllText(statePath, JsonUtility.ToJson(state, true));
        }

        private Dictionary<Transform, string> BuildPlanetIdsByCenterMap()
        {
            var result = new Dictionary<Transform, string>();
            if (!_worldManager)
                return result;

            _planetsBuffer.Clear();
            if (_worldManager.GetActivePlanets(_planetsBuffer) <= 0)
                return result;

            for (var i = 0; i < _planetsBuffer.Count; i++)
            {
                var binding = _planetsBuffer[i];
                if (!binding.generator || string.IsNullOrWhiteSpace(binding.planetId))
                    continue;

                result[binding.generator.transform] = binding.planetId;
            }

            return result;
        }

        private static string ResolveCurrentPlanetId(
            SpawnedEntityRuntime entity,
            Dictionary<Transform, string> planetIdsByCenter)
        {
            var orbitCenter = entity.Movement.OrbitCenter;
            if (orbitCenter && planetIdsByCenter != null && planetIdsByCenter.TryGetValue(orbitCenter, out var currentPlanetId))
                return currentPlanetId;

            return entity.PlanetId;
        }

        private void OnEntityOrbitCenterChanged(SpawnedEntityRuntime entity, Transform orbitCenter)
        {
            if (entity == null || orbitCenter == null)
                return;

            var planetId = ResolvePlanetIdByCenter(orbitCenter);
            if (!string.IsNullOrWhiteSpace(planetId))
                entity.PlanetId = planetId;
        }

        private string ResolvePlanetIdByCenter(Transform orbitCenter)
        {
            if (!orbitCenter || !_worldManager)
                return null;

            _planetsBuffer.Clear();
            if (_worldManager.GetActivePlanets(_planetsBuffer) <= 0)
                return null;

            for (var i = 0; i < _planetsBuffer.Count; i++)
            {
                var binding = _planetsBuffer[i];
                if (!binding.generator || binding.generator.transform != orbitCenter)
                    continue;

                return binding.planetId;
            }

            return null;
        }

        private void AttachOrbitCenterTracking(SpawnedEntityRuntime runtime)
        {
            if (runtime == null || runtime.Movement == null)
                return;

            runtime.OrbitCenterChangedHandler = orbitCenter => OnEntityOrbitCenterChanged(runtime, orbitCenter);
            runtime.Movement.OrbitCenterChanged += runtime.OrbitCenterChangedHandler;
        }

        private static void DetachOrbitCenterTracking(SpawnedEntityRuntime runtime)
        {
            if (runtime == null || runtime.Movement == null || runtime.OrbitCenterChangedHandler == null)
                return;

            runtime.Movement.OrbitCenterChanged -= runtime.OrbitCenterChangedHandler;
            runtime.OrbitCenterChangedHandler = null;
        }

        private bool TryLoadState(string worldId, out WorldEntitiesState state)
        {
            state = null;
            if (string.IsNullOrWhiteSpace(worldId) || !_worldService)
                return false;

            var worldRoot = _worldService.GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
                return false;

            var statePath = Path.Combine(worldRoot, _settings.entitiesStateFileName);
            if (!File.Exists(statePath))
                return false;

            var json = File.ReadAllText(statePath);
            state = JsonUtility.FromJson<WorldEntitiesState>(json);
            return state != null;
        }

        private static bool TryParseKind(int value, out SpawnedEntityKind kind)
        {
            if (Enum.IsDefined(typeof(SpawnedEntityKind), value))
            {
                kind = (SpawnedEntityKind)value;
                return true;
            }

            kind = SpawnedEntityKind.Peaceful;
            return false;
        }

        private static OrbitRotationDirection ParseDirection(int value, OrbitRotationDirection fallback)
        {
            if (Enum.IsDefined(typeof(OrbitRotationDirection), value))
                return (OrbitRotationDirection)value;

            return fallback;
        }

        private void NotifyHeroOrbitChanged() =>
            HeroOrbitChanged?.Invoke(_currentHero, _currentHeroOrbitMovement);

        [Serializable]
        private class WorldEntitiesState
        {
            public List<EntityStateEntry> entities = new();
        }

        [Serializable]
        private class EntityStateEntry
        {
            public int kind;
            public string planetId;
            public float altitudeFromSurface;
            public float angularSpeedDegPerSecond;
            public float angleDeg;
            public int direction;
        }

        private sealed class SpawnedEntityRuntime
        {
            public SpawnedEntityKind Kind;
            public string PlanetId;
            public GameObject GameObject;
            public PlanetOrbitMovement Movement;
            public Action<Transform> OrbitCenterChangedHandler;
        }
    }
}
