using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace App.Planets.GfxGen.Persistence
{
    public class PlanetGenerationPersistenceService : MonoBehaviour
    {
        [Header("Planet Discovery")]
        [SerializeField] private bool collectGeneratorsOnEnable = true;
        [SerializeField] private bool includeInactiveGenerators = true;
        [SerializeField] private bool autoAttachPersistenceIfMissing = true;
        [SerializeField] private List<PlanetGenerator> planets = new List<PlanetGenerator>();

        [Header("Planet Persistence")]
        [SerializeField] private string generatedRootName = "__GeneratedPlanet";
        [SerializeField] private string cacheFolderName = "PlanetCache";
        [SerializeField] private string cacheIdPrefix = "generation";
        [SerializeField] private bool runtimeSaveOnlyIfChanged = true;
        [SerializeField] private bool verboseLogging = true;

        [Header("Runtime")]
        [SerializeField] private bool autoSaveAfterRuntimeGeneration = true;

        private readonly HashSet<PlanetGenerator> _subscribedPlanets = new HashSet<PlanetGenerator>();

        private void OnEnable()
        {
            if (collectGeneratorsOnEnable)
                CollectPlanetsFromScene();

            SubscribeToGenerators();
        }

        private void OnDisable()
        {
            UnsubscribeFromGenerators();
        }

        [ContextMenu("Collect Planets From Scene")]
        public void CollectPlanetsFromScene()
        {
            var collected = FindObjectsByType<PlanetGenerator>(
                includeInactiveGenerators ? FindObjectsInactive.Include : FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            planets.Clear();
            foreach (var generator in collected)
                planets.Add(generator);

            if (isActiveAndEnabled)
                SubscribeToGenerators();

            if (verboseLogging)
                Debug.Log($"Collected {planets.Count} planets for persistence service.", this);
        }

        [ContextMenu("Save All Caches (Force)")]
        public void SaveAllForce() => SaveAll(runtimeAware: false);

        [ContextMenu("Save All Caches (Runtime If Changed)")]
        public void SaveAllRuntimeAware() => SaveAll(runtimeAware: true);

        private void SaveAll(bool runtimeAware)
        {
            var savedCount = 0;
            var skippedCount = 0;

            for (var i = 0; i < planets.Count; i++)
            {
                var generator = planets[i];
                if (!generator)
                {
                    skippedCount++;
                    continue;
                }

                var persistence = GetOrCreatePersistence(generator);
                if (!persistence)
                {
                    skippedCount++;
                    continue;
                }

                ConfigurePersistenceForGenerator(persistence, generator, i);
                if (runtimeAware)
                    persistence.SaveGeneratedCacheRuntimeAware();
                else
                    persistence.SaveGeneratedCache();

                savedCount++;
            }

            if (verboseLogging)
            {
                Debug.Log(
                    $"Batch cache save completed. Saved: {savedCount}, Skipped: {skippedCount}, RuntimeAware: {runtimeAware}.",
                    this);
            }
        }

        private void SubscribeToGenerators()
        {
            UnsubscribeFromGenerators();

            foreach (var generator in planets.Where(generator => generator))
            {
                generator.PlanetGenerated += OnGeneratorPlanetGenerated;
                _subscribedPlanets.Add(generator);
            }
        }

        private void UnsubscribeFromGenerators()
        {
            foreach (var generator in _subscribedPlanets.Where(generator => generator)) 
                generator.PlanetGenerated -= OnGeneratorPlanetGenerated;

            _subscribedPlanets.Clear();
        }

        private void OnGeneratorPlanetGenerated(PlanetGenerator _)
        {
            if (!Application.isPlaying || !autoSaveAfterRuntimeGeneration)
                return;

            SaveAllRuntimeAware();
        }

        private PlanetGenerationPersistence GetOrCreatePersistence(PlanetGenerator generator)
        {
            var persistence = generator.GetComponent<PlanetGenerationPersistence>();
            if (persistence)
                return persistence;

            if (autoAttachPersistenceIfMissing)
                return generator.gameObject.AddComponent<PlanetGenerationPersistence>();
            
            if (verboseLogging)
                Debug.LogWarning($"Missing {nameof(PlanetGenerationPersistence)} on '{generator.name}'. Skipping.", generator);
            return null;
        }

        private void ConfigurePersistenceForGenerator(
            PlanetGenerationPersistence persistence,
            PlanetGenerator generator,
            int index)
        {
            persistence.Configure(
                generatedRootName,
                cacheFolderName,
                BuildCacheId(generator, index),
                autoSaveAfterRuntimeGenerationValue: false,
                runtimeSaveOnlyIfChanged,
                verboseLogging);
        }

        private string BuildCacheId(PlanetGenerator generator, int index)
        {
            var sceneName = generator.gameObject.scene.name;
            var hierarchyPath = GetHierarchyPath(generator.transform);
            var normalizedPath = NormalizeForPath(hierarchyPath);
            return $"{cacheIdPrefix}_{sceneName}_{index:D3}_{normalizedPath}";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var names = new List<string>();
            var current = transform;
            while (current)
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
            
            foreach (var c in input)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                {
                    builder.Append(c);
                    continue;
                }

                builder.Append('_');
            }

            return builder.ToString();
        }
    }
}
