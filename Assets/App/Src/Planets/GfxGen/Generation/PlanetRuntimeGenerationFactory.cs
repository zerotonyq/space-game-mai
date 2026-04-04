using System.Collections.Generic;
using App.Planets.GfxGen.Persistence;
using UnityEngine;
using Zenject;

namespace App.Planets.GfxGen
{
    public class PlanetRuntimeGenerationFactory : MonoBehaviour
    {
        [SerializeField] private PlanetWorldService worldService;
        [SerializeField] private PlanetGenerator generatorPrefab;
        [SerializeField] private Transform defaultParent;
        [SerializeField] private string defaultNamePrefix = "RuntimeGeneration";
        [SerializeField] private string defaultIdPrefix = "runtime_gen";
        [SerializeField] private bool registerInWorldService = true;

        private int _runtimeCounter;
        private readonly List<PlanetGenerator> _createdGenerators = new List<PlanetGenerator>();

        [Inject]
        public void Construct([InjectOptional] PlanetWorldService injectedWorldService)
        {
            if (worldService == null)
                worldService = injectedWorldService;
        }

        [ContextMenu("Create Planet (Auto Id)")]
        public void CreatePlanetAutoId()
        {
            CreatePlanet(null, null, true);
        }

        public PlanetGenerator CreatePlanet(string planetId, Transform parent = null)
        {
            return CreatePlanet(planetId, parent, true);
        }

        public PlanetGenerator CreatePlanet(string planetId, Transform parent, bool runGeneration)
        {
            var actualParent = parent ?? defaultParent;
            
            var instance = CreateGeneratorInstance(actualParent);
            
            if (!instance)
                return null;

            _runtimeCounter++;
            instance.gameObject.name = $"{defaultNamePrefix}_{_runtimeCounter:D3}";
            _createdGenerators.Add(instance);

            if (registerInWorldService && worldService)
            {
                var id = string.IsNullOrWhiteSpace(planetId)
                    ? $"{defaultIdPrefix}_{_runtimeCounter:D3}"
                    : planetId;

                worldService.RegisterGenerator(instance, id);
            }

            instance.SetGenerateOnStartRuntime(false);

            if (runGeneration)
                RunGeneration(instance);

            return instance;
        }

        public bool DestroyPlanet(PlanetGenerator generator)
        {
            if (generator == null)
                return false;

            if (worldService != null)
                worldService.UnregisterGenerator(generator);

            _createdGenerators.Remove(generator);

            if (Application.isPlaying)
                Destroy(generator.gameObject);
            else
                DestroyImmediate(generator.gameObject);

            return true;
        }

        private PlanetGenerator CreateGeneratorInstance(Transform parent)
        {
            if (generatorPrefab == null)
            {
                Debug.LogWarning("Generator prefab is not assigned. Runtime generation creation is canceled.", this);
                return null;
            }

            var instance = Instantiate(generatorPrefab, parent, worldPositionStays: false);
            return instance;
        }

        private static void RunGeneration(PlanetGenerator generator)
        {
            if (generator == null)
                return;

            if (Application.isPlaying)
                generator.GenerateRuntimeAsync();
            else
                generator.Generate();
        }
    }
}
