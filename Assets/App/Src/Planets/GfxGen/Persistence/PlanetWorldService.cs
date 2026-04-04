using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace App.Planets.GfxGen.Persistence
{
    public class PlanetWorldService : MonoBehaviour
    {
        [Serializable]
        private sealed class GeneratorBinding
        {
            public string generatorId;
            public PlanetGenerator generator;
        }

        [Header("World")]
        [SerializeField] private string currentWorldId = "world_001";
        [SerializeField] private string pendingWorldId = "world_002";
        [SerializeField] private string worldsRootFolderName = "Worlds";
        [SerializeField] private string cacheFolderName = "PlanetCache";

        [Header("Registered Planets")]
        [SerializeField] private bool autoAttachPersistenceIfMissing = true;
        [SerializeField] private List<GeneratorBinding> generatorBindings = new List<GeneratorBinding>();

        [Header("Planet Save/Load")]
        [SerializeField] private string generatedRootName = "__GeneratedPlanet";
        [SerializeField] private bool runtimeSaveOnlyIfChanged = true;
        [SerializeField] private bool autoSaveAfterRuntimeGeneration = true;
        [SerializeField] private bool autoLoadOnStart = false;
        [SerializeField] private bool verboseLogging = true;

        private readonly HashSet<PlanetGenerator> _subscribedGenerators = new HashSet<PlanetGenerator>();

        public string CurrentWorldId => currentWorldId;
        public int RegisteredPlanetCount => generatorBindings.Count;

        public string GetWorldRootPath(string worldId)
        {
            if (string.IsNullOrWhiteSpace(worldId))
                return null;

            return Path.Combine(Application.persistentDataPath, worldsRootFolderName, worldId);
        }

        public string GetWorldsRootPath()
        {
            return Path.Combine(Application.persistentDataPath, worldsRootFolderName);
        }

        public string GetWorldManifestPath(string worldId)
        {
            var worldRoot = GetWorldRootPath(worldId);
            if (string.IsNullOrWhiteSpace(worldRoot))
                return null;

            return Path.Combine(worldRoot, "world_manifest.json");
        }

        private void OnEnable()
        {
            SubscribeToGenerators();
        }

        private void Start()
        {
            if (autoLoadOnStart)
                LoadCurrentWorld();
        }

        private void OnDisable()
        {
            UnsubscribeFromGenerators();
        }

        public bool RegisterGenerator(PlanetGenerator generator, string generatorId = null)
        {
            if (!generator)
                return false;

            var normalizedId = NormalizeGeneratorId(generatorId, generator);
            
            for (var i = 0; i < generatorBindings.Count; i++)
            {
                var binding = generatorBindings[i];
                if (binding == null)
                    continue;

                if (binding.generator == generator)
                {
                    binding.generatorId = normalizedId;
                    SubscribeToGenerators();
                    return true;
                }
            }

            generatorBindings.Add(new GeneratorBinding
            {
                generator = generator,
                generatorId = normalizedId
            });

            SubscribeToGenerators();
            return true;
        }

        public bool UnregisterGenerator(PlanetGenerator generator)
        {
            if (generator == null)
                return false;

            var removed = false;
            for (var i = generatorBindings.Count - 1; i >= 0; i--)
            {
                var binding = generatorBindings[i];
                if (binding == null || binding.generator != generator)
                    continue;

                generatorBindings.RemoveAt(i);
                removed = true;
            }

            SubscribeToGenerators();
            return removed;
        }

        [ContextMenu("Save Current World (Force)")]
        public void SaveCurrentWorldForce()
        {
            SaveCurrentWorld(runtimeAware: false);
        }

        [ContextMenu("Save Current World (Runtime If Changed)")]
        public void SaveCurrentWorldRuntimeAware()
        {
            SaveCurrentWorld(runtimeAware: true);
        }

        [ContextMenu("Load Current World")]
        public void LoadCurrentWorld()
        {
            if (string.IsNullOrWhiteSpace(currentWorldId))
            {
                Debug.LogWarning("Current world id is empty. Load canceled.", this);
                return;
            }

            CleanupBindings();

            var loaded = 0;
            var missing = 0;

            foreach (var binding in generatorBindings)
            {
                if (binding == null || !binding.generator)
                    continue;

                var ok = LoadPlanetCache(binding.generator, binding.generatorId);

                if (ok)
                    loaded++;
                else
                    missing++;
            }

            if (verboseLogging)
                Debug.Log($"World '{currentWorldId}' loaded. Planets loaded: {loaded}, missing cache: {missing}.", this);
        }

        public bool LoadPlanetCache(PlanetGenerator generator, string generatorId)
        {
            if (!generator || string.IsNullOrWhiteSpace(generatorId) || string.IsNullOrWhiteSpace(currentWorldId))
                return false;

            var worldFolder = BuildWorldCacheFolder(currentWorldId);
            return PlanetGenerationCacheLoader.TryLoad(
                generator.transform,
                generatedRootName,
                worldFolder,
                generatorId,
                this,
                verboseLogging);
        }

        [ContextMenu("Switch To Pending World (Save+Load)")]
        public void SwitchToPendingWorld()
        {
            SwitchWorld(pendingWorldId, saveCurrentBeforeSwitch: true, loadAfterSwitch: true);
        }

        public void SwitchWorld(string worldId, bool saveCurrentBeforeSwitch, bool loadAfterSwitch)
        {
            if (string.IsNullOrWhiteSpace(worldId))
            {
                Debug.LogWarning("Target world id is empty. Switch canceled.", this);
                return;
            }

            if (saveCurrentBeforeSwitch && !string.IsNullOrWhiteSpace(currentWorldId))
                SaveCurrentWorld(runtimeAware: true);

            currentWorldId = worldId;

            if (loadAfterSwitch)
                LoadCurrentWorld();
        }

        private void SaveCurrentWorld(bool runtimeAware)
        {
            if (string.IsNullOrWhiteSpace(currentWorldId))
            {
                Debug.LogWarning("Current world id is empty. Save canceled.", this);
                return;
            }

            CleanupBindings();

            var worldFolder = BuildWorldCacheFolder(currentWorldId);
            var saved = 0;
            var skipped = 0;

            for (var i = 0; i < generatorBindings.Count; i++)
            {
                var binding = generatorBindings[i];
                if (binding == null || binding.generator == null)
                {
                    skipped++;
                    continue;
                }

                var persistence = GetOrCreatePersistence(binding.generator);
                if (persistence == null)
                {
                    skipped++;
                    continue;
                }

                persistence.Configure(
                    generatedRootName,
                    worldFolder,
                    binding.generatorId,
                    autoSaveAfterRuntimeGenerationValue: false,
                    runtimeSaveOnlyIfChanged,
                    verboseLogging);

                if (runtimeAware)
                    persistence.SaveGeneratedCacheRuntimeAware();
                else
                    persistence.SaveGeneratedCache();

                saved++;
            }

            SaveWorldManifest(currentWorldId);

            if (verboseLogging)
                Debug.Log($"World '{currentWorldId}' saved. Planets saved: {saved}, skipped: {skipped}, runtimeAware: {runtimeAware}.", this);
        }

        private void OnGeneratorPlanetGenerated(PlanetGenerator _)
        {
            if (!Application.isPlaying || !autoSaveAfterRuntimeGeneration)
                return;

            SaveCurrentWorld(runtimeAware: true);
        }

        private void SubscribeToGenerators()
        {
            UnsubscribeFromGenerators();
            CleanupBindings();

            for (var i = 0; i < generatorBindings.Count; i++)
            {
                var binding = generatorBindings[i];
                if (binding == null || !binding.generator)
                    continue;

                binding.generator.PlanetGenerated += OnGeneratorPlanetGenerated;
                _subscribedGenerators.Add(binding.generator);
            }
        }

        private void UnsubscribeFromGenerators()
        {
            foreach (var generator in _subscribedGenerators)
            {
                if (generator == null)
                    continue;

                generator.PlanetGenerated -= OnGeneratorPlanetGenerated;
            }

            _subscribedGenerators.Clear();
        }

        private void CleanupBindings()
        {
            for (var i = generatorBindings.Count - 1; i >= 0; i--)
            {
                var binding = generatorBindings[i];
                if (binding == null || binding.generator == null)
                {
                    generatorBindings.RemoveAt(i);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(binding.generatorId))
                    binding.generatorId = NormalizeGeneratorId(null, binding.generator);
            }
        }

        private static string NormalizeGeneratorId(string requestedId, PlanetGenerator generator)
        {
            if (!string.IsNullOrWhiteSpace(requestedId))
                return NormalizeForPath(requestedId);

            var sceneName = generator.gameObject.scene.name;
            var hierarchyPath = GetHierarchyPath(generator.transform);
            var normalizedPath = NormalizeForPath(hierarchyPath);
            return $"{sceneName}_{normalizedPath}";
        }

        private PlanetGenerationPersistence GetOrCreatePersistence(PlanetGenerator generator)
        {
            var persistence = generator.GetComponent<PlanetGenerationPersistence>();
            if (persistence != null)
                return persistence;

            if (!autoAttachPersistenceIfMissing)
                return null;

            return generator.gameObject.AddComponent<PlanetGenerationPersistence>();
        }

        private string BuildWorldCacheFolder(string worldId)
        {
            return Path.Combine(worldsRootFolderName, worldId, cacheFolderName);
        }

        private void SaveWorldManifest(string worldId)
        {
            var worldRoot = Path.Combine(Application.persistentDataPath, worldsRootFolderName, worldId);
            Directory.CreateDirectory(worldRoot);

            var manifest = new WorldManifest
            {
                worldId = worldId
            };

            for (var i = 0; i < generatorBindings.Count; i++)
            {
                var binding = generatorBindings[i];
                if (binding == null || binding.generator == null)
                    continue;

                manifest.generators.Add(new WorldGeneratorEntry
                {
                    generatorId = binding.generatorId,
                    hierarchyPath = GetHierarchyPath(binding.generator.transform)
                });
            }

            var manifestPath = Path.Combine(worldRoot, "world_manifest.json");
            File.WriteAllText(manifestPath, JsonUtility.ToJson(manifest, true), Encoding.UTF8);
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current != null)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string NormalizeForPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "unnamed";

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

        [Serializable]
        private class WorldManifest
        {
            public string worldId;
            public List<WorldGeneratorEntry> generators = new List<WorldGeneratorEntry>();
        }

        [Serializable]
        private class WorldGeneratorEntry
        {
            public string generatorId;
            public string hierarchyPath;
        }
    }
}
