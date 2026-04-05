using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using App.Infrastructure.DI.Base;
using App.Planets.Generation;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Zenject;

namespace App.Planets.Persistence
{
    public class PlanetWorldManager : MonoBehaviour, IGameService
    {
        public event Func<string, UniTask> WorldCreated;
        public event Func<string, UniTask> WorldLoaded;
        public event Func<string, UniTask> WorldUnloaded;

        public enum WorldSize
        {
            Small,
            Medium,
            Large
        }

        [Header("Dependencies")]
        [SerializeField] private PlanetWorldService worldService;
        [SerializeField] private PlanetRuntimeGenerationFactory runtimeGenerationFactory;
        [SerializeField] private Transform planetsRoot;

        [Header("World Config")]
        [SerializeField] private int smallWorldPlanetCount = 4;
        [SerializeField] private int mediumWorldPlanetCount = 8;
        [SerializeField] private int largeWorldPlanetCount = 12;
        [SerializeField] [Min(0f)] private float smallWorldSpawnRadius = 15f;
        [SerializeField] [Min(0f)] private float mediumWorldSpawnRadius = 30f;
        [SerializeField] [Min(0f)] private float largeWorldSpawnRadius = 50f;
        [SerializeField] [Min(0f)] private float minPlanetSeparationPadding = 1f;
        [SerializeField] [Min(1)] private int maxSpawnPositionAttemptsPerPlanet = 256;
        [SerializeField] private string worldManifestFileName = "planet_world_manifest.json";

        [Header("Flow")]
        [SerializeField] private bool saveCurrentBeforeCreate = true;
        [SerializeField] private bool saveAfterCreate = true;
        [SerializeField] private bool yieldBetweenSequentialPlanetOperations = true;
        [SerializeField] private bool clearWorldOnStart = true;
        [SerializeField] private bool verboseLogging = true;

        private readonly List<ActivePlanetEntry> _activePlanets = new();
        private bool _isBusy;
        private bool _isLoadingWorld;
        private float _currentWorldLoadProgress01 = 1f;

        public bool IsBusy => _isBusy;
        public bool IsLoadingWorld => _isLoadingWorld;
        public float CurrentWorldLoadProgress01 => _currentWorldLoadProgress01;
        public int ActivePlanetCount => _activePlanets.Count;
        public string CurrentWorldId => worldService ? worldService.CurrentWorldId : string.Empty;

        [Inject]
        public void Construct(
            [InjectOptional] PlanetWorldService injectedWorldService,
            [InjectOptional] PlanetRuntimeGenerationFactory injectedRuntimeGenerationFactory)
        {
            if (worldService == null)
                worldService = injectedWorldService;

            if (runtimeGenerationFactory == null)
                runtimeGenerationFactory = injectedRuntimeGenerationFactory;
        }

        public UniTask Initialize()
        {
            if (Application.isPlaying && clearWorldOnStart)
                UnloadCurrentWorldInternal(saveBeforeUnload: false);

            return UniTask.CompletedTask;
        }

        public void CreateWorld(string worldId, WorldSize worldSize)
        {
            CreateWorldAsync(worldId, worldSize).Forget(LogOperationException);
        }

        public void LoadWorld(string worldId, bool saveAndUnloadCurrent = true)
        {
            LoadWorldAsync(worldId, saveAndUnloadCurrent).Forget(LogOperationException);
        }

        public void UnloadCurrentWorld(bool saveBeforeUnload = true)
        {
            UnloadCurrentWorldAsync(saveBeforeUnload).Forget(LogOperationException);
        }

        public async UniTask CreateWorldAsync(string worldId, WorldSize worldSize)
        {
            if (!TryBeginOperation())
                return;

            try
            {
                SetWorldLoadProgress(0f);

                if (!ValidateDependencies(worldId))
                {
                    SetWorldLoadProgress(1f);
                    return;
                }

                if (saveCurrentBeforeCreate && HasManagedPlanetsInScene())
                    worldService.SaveCurrentWorldRuntimeAware();

                DestroyAllActivePlanets();
                worldService.SwitchWorld(worldId, saveCurrentBeforeSwitch: false, loadAfterSwitch: false);

                var targetPlanetCount = Mathf.Max(0, GetPlanetCount(worldSize));
                var spawnRadius = GetSpawnRadius(worldSize);
                var processedPlanetCount = 0;

                for (var i = 0; i < targetPlanetCount; i++)
                {
                    var planetId = BuildPlanetId(worldId, i);
                    var generator = runtimeGenerationFactory.CreatePlanet(planetId, planetsRoot, runGeneration: false);

                    if (generator == null)
                    {
                        processedPlanetCount++;
                        UpdateProgress(processedPlanetCount, targetPlanetCount);
                        continue;
                    }

                    if (!TryApplyNonOverlappingSpawnPosition(generator, spawnRadius))
                    {
                        if (verboseLogging)
                        {
                            Debug.LogWarning(
                                $"Could not place planet '{planetId}' without overlap inside radius {spawnRadius}. " +
                                $"Attempts: {Mathf.Max(1, maxSpawnPositionAttemptsPerPlanet)}. Planet will be skipped.",
                                this);
                        }

                        DestroyPlanet(generator);
                        processedPlanetCount++;
                        UpdateProgress(processedPlanetCount, targetPlanetCount);
                        continue;
                    }

                    EnsureRegistration(generator, planetId);
                    _activePlanets.Add(new ActivePlanetEntry(generator, planetId));

                    await GenerateSinglePlanetSequentiallyAsync(generator);

                    processedPlanetCount++;
                    UpdateProgress(processedPlanetCount, targetPlanetCount);

                    if (yieldBetweenSequentialPlanetOperations)
                        await UniTask.Yield();
                }

                if (saveAfterCreate)
                    worldService.SaveCurrentWorldForce();

                var manifestPath = SaveManagerWorldManifest(worldId, worldSize, _activePlanets);
                LogLoadPayload(worldId, worldSize, manifestPath, _activePlanets.Count);

                if (verboseLogging)
                    Debug.Log($"World '{worldId}' created. Planets: {_activePlanets.Count}.", this);

                if (targetPlanetCount <= 0)
                    SetWorldLoadProgress(1f);

                await InvokeWorldEventAsync(WorldCreated, worldId);
            }
            finally
            {
                EndOperation();
            }
        }

        public async UniTask LoadWorldAsync(string worldId, bool saveAndUnloadCurrent = true)
        {
            if (!TryBeginOperation())
                return;

            try
            {
                SetWorldLoadProgress(0f);

                if (!ValidateDependencies(worldId))
                {
                    SetWorldLoadProgress(1f);
                    return;
                }

                if (saveAndUnloadCurrent)
                    UnloadCurrentWorldInternal(saveBeforeUnload: true);
                else
                    DestroyAllActivePlanets();

                List<PlanetLoadEntry> planetEntries;
                if (TryReadPlanetIds(worldId, out var manifest))
                    planetEntries = BuildLoadEntries(manifest);
                else
                    planetEntries = new List<PlanetLoadEntry>();

                worldService.SwitchWorld(worldId, saveCurrentBeforeSwitch: false, loadAfterSwitch: false);

                var loadedOnSceneCount = 0;
                var totalPlanetCount = Mathf.Max(0, planetEntries.Count);
                for (var i = 0; i < planetEntries.Count; i++)
                {
                    var loadEntry = planetEntries[i];
                    var planetId = loadEntry.planetId;
                    var generator = runtimeGenerationFactory.CreatePlanet(planetId, planetsRoot, runGeneration: false);
                    if (!generator)
                    {
                        UpdateProgress(loadedOnSceneCount, totalPlanetCount);
                        continue;
                    }

                    if (loadEntry.hasWorldPosition)
                        generator.transform.position = loadEntry.worldPosition;

                    EnsureRegistration(generator, planetId);
                    _activePlanets.Add(new ActivePlanetEntry(generator, planetId));
                    worldService.LoadPlanetCache(generator, planetId);

                    loadedOnSceneCount++;
                    UpdateProgress(loadedOnSceneCount, totalPlanetCount);

                    if (yieldBetweenSequentialPlanetOperations)
                        await UniTask.Yield();
                }

                if (verboseLogging)
                    Debug.Log($"World '{worldId}' loaded. Planets restored: {_activePlanets.Count}.", this);

                if (totalPlanetCount <= 0)
                    SetWorldLoadProgress(1f);

                await InvokeWorldEventAsync(WorldLoaded, worldId);
            }
            finally
            {
                EndOperation();
            }
        }

        public async UniTask UnloadCurrentWorldAsync(bool saveBeforeUnload = true)
        {
            if (!TryBeginOperation())
                return;

            try
            {
                SetWorldLoadProgress(0f);
                var unloadedWorldId = CurrentWorldId;

                UnloadCurrentWorldInternal(saveBeforeUnload);
                await InvokeWorldEventAsync(WorldUnloaded, unloadedWorldId);

                SetWorldLoadProgress(1f);
                await UniTask.Yield();
            }
            finally
            {
                EndOperation();
            }
        }

        [ContextMenu("Clear All Worlds Data")]
        public void ClearAllWorldsData()
        {
            if (!worldService)
            {
                Debug.LogWarning("PlanetWorldService reference is missing. Clear data canceled.", this);
                return;
            }

            var worldsRoot = worldService.GetWorldsRootPath();
            if (string.IsNullOrWhiteSpace(worldsRoot))
                return;

            if (!Directory.Exists(worldsRoot))
            {
                if (verboseLogging)
                    Debug.Log($"Worlds data folder does not exist: {worldsRoot}", this);
                return;
            }

            Directory.Delete(worldsRoot, true);

            if (verboseLogging)
                Debug.Log($"All saved worlds data was removed: {worldsRoot}", this);
        }

        private bool TryBeginOperation()
        {
            if (_isBusy)
            {
                Debug.LogWarning("PlanetWorldManager is busy. Wait until current operation completes.", this);
                return false;
            }

            _isBusy = true;
            _isLoadingWorld = true;
            return true;
        }

        private void EndOperation()
        {
            _isLoadingWorld = false;
            _isBusy = false;
        }

        private static void LogOperationException(Exception exception)
        {
            Debug.LogException(exception);
        }

        private static async UniTask InvokeWorldEventAsync(Func<string, UniTask> worldEvent, string worldId)
        {
            if (worldEvent == null)
                return;

            var handlers = worldEvent.GetInvocationList();
            for (var i = 0; i < handlers.Length; i++)
            {
                if (handlers[i] is not Func<string, UniTask> callback)
                    continue;

                await callback(worldId);
            }
        }

        private async UniTask GenerateSinglePlanetSequentiallyAsync(PlanetGenerator generator)
        {
            if (!generator)
                return;

            var isCompleted = false;
            var completionSource = new UniTaskCompletionSource();

            void OnPlanetGenerated(PlanetGenerator generatedPlanet)
            {
                if (generatedPlanet != generator)
                    return;

                isCompleted = true;
                completionSource.TrySetResult();
            }

            generator.PlanetGenerated += OnPlanetGenerated;
            try
            {
                if (Application.isPlaying)
                {
                    generator.GenerateRuntimeAsync();
                    if (!isCompleted)
                        await completionSource.Task;
                }
                else
                {
                    generator.Generate();
                }
            }
            finally
            {
                generator.PlanetGenerated -= OnPlanetGenerated;
            }
        }

        private bool ValidateDependencies(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
            {
                Debug.LogWarning("World id is empty. Operation canceled.", this);
                return false;
            }

            if (!worldService)
            {
                Debug.LogWarning("PlanetWorldService reference is missing. Operation canceled.", this);
                return false;
            }

            if (!runtimeGenerationFactory)
            {
                Debug.LogWarning("PlanetRuntimeGenerationFactory reference is missing. Operation canceled.", this);
                return false;
            }

            return true;
        }

        private void UnloadCurrentWorldInternal(bool saveBeforeUnload)
        {
            if (!worldService)
                return;

            if (saveBeforeUnload && HasManagedPlanetsInScene())
                worldService.SaveCurrentWorldRuntimeAware();

            DestroyAllActivePlanets();

            if (verboseLogging)
                Debug.Log("Current world unloaded.", this);
        }

        private bool HasManagedPlanetsInScene()
        {
            return _activePlanets.Count > 0;
        }

        private void DestroyAllActivePlanets()
        {
            for (var i = _activePlanets.Count - 1; i >= 0; i--)
            {
                var generator = _activePlanets[i].generator;
                if (generator == null)
                    continue;

                DestroyPlanet(generator);
            }

            _activePlanets.Clear();
        }

        private void DestroyPlanet(PlanetGenerator generator)
        {
            if (generator == null)
                return;

            if (runtimeGenerationFactory != null && runtimeGenerationFactory.DestroyPlanet(generator))
                return;

            if (Application.isPlaying)
                Destroy(generator.gameObject);
            else
                DestroyImmediate(generator.gameObject);
        }

        private void EnsureRegistration(PlanetGenerator generator, string planetId)
        {
            if (!worldService || !generator)
                return;

            worldService.RegisterGenerator(generator, planetId);
        }

        private int GetPlanetCount(WorldSize worldSize)
        {
            switch (worldSize)
            {
                case WorldSize.Small:
                    return smallWorldPlanetCount;
                case WorldSize.Medium:
                    return mediumWorldPlanetCount;
                case WorldSize.Large:
                    return largeWorldPlanetCount;
                default:
                    return mediumWorldPlanetCount;
            }
        }

        private float GetSpawnRadius(WorldSize worldSize)
        {
            switch (worldSize)
            {
                case WorldSize.Small:
                    return Mathf.Max(0f, smallWorldSpawnRadius);
                case WorldSize.Medium:
                    return Mathf.Max(0f, mediumWorldSpawnRadius);
                case WorldSize.Large:
                    return Mathf.Max(0f, largeWorldSpawnRadius);
                default:
                    return Mathf.Max(0f, mediumWorldSpawnRadius);
            }
        }

        private bool TryApplyNonOverlappingSpawnPosition(PlanetGenerator generator, float worldSpawnRadius)
        {
            if (!generator)
                return false;

            var attempts = Mathf.Max(1, maxSpawnPositionAttemptsPerPlanet);
            var candidateRadius = GetPlanetRadius(generator);
            var spawnRadius = Mathf.Max(0f, worldSpawnRadius);

            for (var attempt = 0; attempt < attempts; attempt++)
            {
                var offset = UnityEngine.Random.insideUnitCircle * spawnRadius;
                if (!IsValidPlanetPosition(offset, candidateRadius))
                    continue;

                var currentPosition = generator.transform.position;
                generator.transform.position = new Vector3(offset.x, offset.y, currentPosition.z);
                return true;
            }

            return false;
        }

        private bool IsValidPlanetPosition(Vector2 candidatePosition, float candidateRadius)
        {
            var padding = Mathf.Max(0f, minPlanetSeparationPadding);
            for (var i = 0; i < _activePlanets.Count; i++)
            {
                var existingGenerator = _activePlanets[i].generator;
                if (!existingGenerator)
                    continue;

                var existingPosition3D = existingGenerator.transform.position;
                var existingPosition = new Vector2(existingPosition3D.x, existingPosition3D.y);
                var existingRadius = GetPlanetRadius(existingGenerator);
                var minAllowedDistance = candidateRadius + existingRadius + padding;
                var minAllowedDistanceSqr = minAllowedDistance * minAllowedDistance;

                if ((candidatePosition - existingPosition).sqrMagnitude < minAllowedDistanceSqr)
                    return false;
            }

            return true;
        }

        private static float GetPlanetRadius(PlanetGenerator generator)
        {
            if (!generator)
                return 0f;

            return Mathf.Max(0f, generator.EstimatedOuterRadiusUnits);
        }

        public bool TryGetPlanetGeneratorById(string planetId, out PlanetGenerator generator)
        {
            generator = null;
            if (string.IsNullOrWhiteSpace(planetId))
                return false;

            for (var i = 0; i < _activePlanets.Count; i++)
            {
                var entry = _activePlanets[i];
                if (!string.Equals(entry.planetId, planetId, StringComparison.Ordinal))
                    continue;

                if (!entry.generator)
                    return false;

                generator = entry.generator;
                return true;
            }

            return false;
        }

        public int GetActivePlanets(List<PlanetBinding> output)
        {
            if (output == null)
                return 0;

            output.Clear();

            for (var i = 0; i < _activePlanets.Count; i++)
            {
                var entry = _activePlanets[i];
                if (!entry.generator || string.IsNullOrWhiteSpace(entry.planetId))
                    continue;

                output.Add(new PlanetBinding(entry.planetId, entry.generator));
            }

            return output.Count;
        }

        private void UpdateProgress(int current, int total)
        {
            if (total <= 0)
                return;

            SetWorldLoadProgress(current / (float)total);
        }

        private string BuildPlanetId(string worldId, int index)
        {
            var normalizedWorldId = NormalizeToken(worldId);
            return $"{normalizedWorldId}_planet_{index:D3}";
        }

        private string SaveManagerWorldManifest(string worldId, WorldSize worldSize, List<ActivePlanetEntry> planets)
        {
            var worldRoot = worldService.GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
                return null;

            Directory.CreateDirectory(worldRoot);

            var manifest = new PlanetWorldManifest
            {
                worldId = worldId,
                worldSize = worldSize.ToString(),
                savedAtUtc = DateTime.UtcNow.ToString("O")
            };

            for (var i = 0; i < planets.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(planets[i].planetId))
                    continue;

                if (planets[i].generator != null)
                {
                    manifest.planets.Add(new PlanetWorldPlanetEntry
                    {
                        planetId = planets[i].planetId,
                        worldPosition = planets[i].generator.transform.position,
                        hasWorldPosition = true
                    });
                }
            }

            var manifestPath = Path.Combine(worldRoot, worldManifestFileName);
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
            return manifestPath;
        }

        private bool TryReadPlanetIds(string worldId, out PlanetWorldManifest manifest)
        {
            var worldRoot = worldService.GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
            {
                manifest = null;
                return false;
            }

            var managerManifestPath = Path.Combine(worldRoot, worldManifestFileName);
            if (File.Exists(managerManifestPath))
            {
                var managerJson = File.ReadAllText(managerManifestPath);
                manifest = JsonUtility.FromJson<PlanetWorldManifest>(managerJson);
                if (manifest != null && manifest.planets != null && manifest.planets.Count > 0)
                    return true;
            }

            manifest = null;
            return false;
        }

        private static List<PlanetLoadEntry> BuildLoadEntries(PlanetWorldManifest manifest)
        {
            var entries = new List<PlanetLoadEntry>();
            if (manifest == null)
                return entries;

            if (manifest.planets != null && manifest.planets.Count > 0)
            {
                for (var i = 0; i < manifest.planets.Count; i++)
                {
                    var planet = manifest.planets[i];
                    if (planet == null || string.IsNullOrWhiteSpace(planet.planetId))
                        continue;

                    entries.Add(new PlanetLoadEntry(planet.planetId, planet.hasWorldPosition, planet.worldPosition));
                }
            }

            return entries;
        }

        private void LogLoadPayload(string worldId, WorldSize worldSize, string manifestPath, int planetCount)
        {
            var payload = new PlanetWorldLoadPayload
            {
                worldId = worldId,
                worldSize = worldSize.ToString(),
                planetCount = planetCount,
                managerManifestPath = manifestPath
            };

            Debug.Log($"World load payload:\n{JsonUtility.ToJson(payload, true)}", this);
        }

        private static string NormalizeToken(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "world";

            var builder = new StringBuilder(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                    continue;
                }

                builder.Append('_');
            }

            return builder.ToString();
        }

        private void SetWorldLoadProgress(float progress01)
        {
            _currentWorldLoadProgress01 = Mathf.Clamp01(progress01);
        }

        private readonly struct ActivePlanetEntry
        {
            public readonly PlanetGenerator generator;
            public readonly string planetId;

            public ActivePlanetEntry(PlanetGenerator generator, string planetId)
            {
                this.generator = generator;
                this.planetId = planetId;
            }
        }

        public readonly struct PlanetBinding
        {
            public readonly string planetId;
            public readonly PlanetGenerator generator;

            public PlanetBinding(string planetId, PlanetGenerator generator)
            {
                this.planetId = planetId;
                this.generator = generator;
            }
        }

        [Serializable]
        private class PlanetWorldManifest
        {
            public string worldId;
            public string worldSize;
            public string savedAtUtc;
            public List<PlanetWorldPlanetEntry> planets = new();
        }

        [Serializable]
        private class PlanetWorldPlanetEntry
        {
            public string planetId;
            public bool hasWorldPosition;
            public Vector3 worldPosition;
        }

        [Serializable]
        private class PlanetWorldLoadPayload
        {
            public string worldId;
            public string worldSize;
            public int planetCount;
            public string managerManifestPath;
        }

        private readonly struct PlanetLoadEntry
        {
            public readonly string planetId;
            public readonly bool hasWorldPosition;
            public readonly Vector3 worldPosition;

            public PlanetLoadEntry(string planetId, bool hasWorldPosition, Vector3 worldPosition)
            {
                this.planetId = planetId;
                this.hasWorldPosition = hasWorldPosition;
                this.worldPosition = worldPosition;
            }
        }
    }
}
