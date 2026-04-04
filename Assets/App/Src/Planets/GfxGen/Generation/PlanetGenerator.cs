using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using Random = UnityEngine.Random;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace App.Planets.GfxGen
{
    [System.Serializable]
    public class PlanetLayerRotationSetting
    {
        [Range(-180f, 180f)] public float Degrees;
    }

    public class PlanetGenerator : MonoBehaviour
    {
        public event Action<PlanetGenerator> PlanetGenerated;
        public bool GenerateOnStartRuntime => generateOnStartRuntime;
        public float EstimatedOuterRadiusUnits => GetEstimatedOuterRadiusUnits();

        [Header("Circle")]
        [SerializeField] [Range(0.1f, 20f)] private float circleRadiusUnits = 1f;
        [SerializeField] private Color circleColor = Color.white;

        [Header("Segments")]
        [SerializeField] [Range(0.05f, 5f)] private float segmentArcLengthMinUnits = 0.2f;
        [SerializeField] [Range(0.05f, 5f)] private float segmentArcLengthMaxUnits = 0.5f;
        [SerializeField] [Range(0.05f, 5f)] private float layerThicknessUnits = 0.35f;
        [SerializeField] [Range(0f, 0.5f)] private float layerOverlapUnits = 0.03f;
        [SerializeField] [Range(0f, 10f)] private float segmentOverlapDegrees = 0.5f;

        [Header("Per Layer Rotation (degrees)")]
        [SerializeField] private List<PlanetLayerRotationSetting> layerRotations = new List<PlanetLayerRotationSetting>();

        [Header("Prefab Pools")]
        [SerializeField] private List<PlanetSegmentProfile> centerCirclePrefabs = new List<PlanetSegmentProfile>();
        [SerializeField] private List<PlanetSegmentProfile> segmentPrefabs = new List<PlanetSegmentProfile>();
        
        [Header("Sprite Settings")]
        [SerializeField] [Range(16f, 1024f)] private float pixelsPerUnit = 100f;
        [SerializeField] private FilterMode filterMode = FilterMode.Bilinear;

        [Header("Texture Fill")]
        [SerializeField] [Range(0.02f, 5f)] private float textureTileSizeUnits = 0.25f;
        [SerializeField] private Vector2 textureUvOffset = Vector2.zero;

        [Header("Segment Outline")]
        [SerializeField] private bool enableSegmentOutline = true;
        [SerializeField] private Color segmentOutlineColor = Color.black;
        [SerializeField] [Range(1f, 8f)] private float segmentOutlineWidthPixels = 1f;

        [Header("Async Generation")]
        [SerializeField] [Range(0.5f, 25f)] private float editorFrameBudgetMs = 4f;
        [SerializeField] [Range(0.5f, 25f)] private float runtimeFrameBudgetMs = 4f;
        [SerializeField] [Range(1, 256)] private int segmentBatchSize = 32;
        [SerializeField] private bool generateOnStartRuntime = true;

        private const string GeneratedRootName = "__GeneratedPlanet";
        private PlanetSpriteFactory _spriteFactory;
        private readonly PlanetSegmentProbabilitySelector _segmentProbabilitySelector = new PlanetSegmentProbabilitySelector();
        private Coroutine _runtimeGenerationCoroutine;
#if UNITY_EDITOR
        private IEnumerator _editorGenerationRoutine;
        private bool _isGenerating;
#endif

        private void Start()
        {
            if (Application.isPlaying && generateOnStartRuntime)
                GenerateRuntimeAsync();
        }

        private void OnDestroy()
        {
            StopRuntimeGeneration();
#if UNITY_EDITOR
            StopEditorGeneration();
#endif
            _spriteFactory?.DestroyGeneratedTextures();
        }

        private void OnDisable()
        {
            StopRuntimeGeneration();
#if UNITY_EDITOR
            StopEditorGeneration();
#endif
        }

        [ContextMenu("Generate Planet (Editor)")]
        public void Generate()
        {
            if (Application.isPlaying)
            {
                Debug.LogWarning("Planet generation is editor-only. Use the component context menu outside Play Mode.", this);
                return;
            }

#if UNITY_EDITOR
            StopRuntimeGeneration();
            StartEditorGeneration();
#else
            Debug.LogWarning("Async editor generation is available only in Unity Editor.", this);
#endif
        }

        [ContextMenu("Generate Planet (Runtime Coroutine)")]
        public void GenerateRuntimeAsync()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("Runtime coroutine generation is available only in Play Mode.", this);
                return;
            }

#if UNITY_EDITOR
            StopEditorGeneration();
#endif
            StopRuntimeGeneration();
            _runtimeGenerationCoroutine = StartCoroutine(GenerateRuntimeRoutine());
        }

        public void SetGenerateOnStartRuntime(bool enabled)
        {
            generateOnStartRuntime = enabled;
        }

        public void StopRuntimeGeneration()
        {
            if (_runtimeGenerationCoroutine == null)
                return;

            StopCoroutine(_runtimeGenerationCoroutine);
            _runtimeGenerationCoroutine = null;
        }

        private IEnumerator GenerateRuntimeRoutine()
        {
            var routine = GenerateIncrementalRoutine();
            var budgetSeconds = Mathf.Max(0.0005f, runtimeFrameBudgetMs / 1000f);

            while (true)
            {
                var startTime = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - startTime < budgetSeconds)
                {
                    if (!routine.MoveNext())
                    {
                        _runtimeGenerationCoroutine = null;
                        yield break;
                    }

                    if (routine.Current != null)
                    {
                        yield return routine.Current;
                        startTime = Time.realtimeSinceStartup;
                    }
                }

                yield return null;
            }
        }

#if UNITY_EDITOR
        private void StartEditorGeneration()
        {
            StopEditorGeneration();
            _editorGenerationRoutine = GenerateIncrementalRoutine();
            _isGenerating = true;
            EditorApplication.update += OnEditorUpdate;
        }

        private void StopEditorGeneration()
        {
            if (!_isGenerating)
                return;

            EditorApplication.update -= OnEditorUpdate;
            _editorGenerationRoutine = null;
            _isGenerating = false;
        }

        private void OnEditorUpdate()
        {
            if (this == null)
            {
                StopEditorGeneration();
                return;
            }

            var budgetSeconds = Mathf.Max(0.0005f, editorFrameBudgetMs / 1000f);
            var startTime = Time.realtimeSinceStartup;

            while (Time.realtimeSinceStartup - startTime < budgetSeconds)
            {
                if (_editorGenerationRoutine == null || !_editorGenerationRoutine.MoveNext())
                {
                    StopEditorGeneration();
                    break;
                }
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

#endif

        private IEnumerator GenerateIncrementalRoutine()
        {
            NormalizeInput();
            ConfigureSpriteFactory();

            var generatedRoot = PlanetHierarchyUtility.GetOrCreateGeneratedRoot(transform, GeneratedRootName);
            PlanetHierarchyUtility.ClearChildren(generatedRoot, Application.isPlaying);
            _spriteFactory.DestroyGeneratedTextures();
            _spriteFactory.ResetWarnings();
            _segmentProbabilitySelector.ClearCache();
            yield return null;

            CreateCenterCircle(generatedRoot);
            yield return null;

            var layersRoutine = CreateLayersIncremental(generatedRoot);
            
            while (layersRoutine.MoveNext())
                yield return layersRoutine.Current;

            PlanetGenerated?.Invoke(this);
        }

        private void CreateCenterCircle(Transform generatedRoot)
        {
            var profile = PlanetSegmentProfilePicker.PickRandomProfile(centerCirclePrefabs) ?? PlanetSegmentProfilePicker.PickRandomProfile(segmentPrefabs);
            var circleObject = PlanetHierarchyUtility.CreateObjectFromProfile("CenterCircle", generatedRoot, profile);
            var renderer = PlanetHierarchyUtility.GetOrAddSpriteRenderer(circleObject);
            renderer.sprite = _spriteFactory.CreateCircleSprite(circleRadiusUnits, circleColor, profile);
            ConfigureCenterCollider(circleObject);
        }

        private IEnumerator CreateLayersIncremental(Transform generatedRoot)
        {
            var layerCount = layerRotations.Count;
            if (layerCount == 0)
            {
                Debug.LogWarning("No planet layers generated: add at least one value in Per Layer Rotation.", this);
                yield break;
            }

            var minArcLengthUnits = Mathf.Min(segmentArcLengthMinUnits, segmentArcLengthMaxUnits);
            var maxArcLengthUnits = Mathf.Max(segmentArcLengthMinUnits, segmentArcLengthMaxUnits);
            var currentInnerRadius = circleRadiusUnits;
            var layerThicknessUnits = this.layerThicknessUnits;

            for (var layerIndex = 0; layerIndex < layerCount; layerIndex++)
            {
                var outerRadius = currentInnerRadius + layerThicknessUnits;
                var middleRadius = (currentInnerRadius + outerRadius) * 0.5f;
                var layerObject = new GameObject($"Layer_{layerIndex}");
                layerObject.transform.SetParent(generatedRoot, false);
                layerObject.transform.localRotation = Quaternion.Euler(0f, 0f, PlanetLayerMath.GetLayerRotation(layerRotations, layerIndex));

                var cachedSpritesByProfileAndAngle = new Dictionary<string, Sprite>();
                var layerInnerRadius = Mathf.Max(0f, currentInnerRadius - layerOverlapUnits * 0.5f);
                var layerOuterRadius = outerRadius + layerOverlapUnits * 0.5f;
                var fallbackColor = PlanetLayerMath.GetLayerColor(layerIndex, layerCount);
                var placementContext = new PlanetPlacementContext(layerCount, circleRadiusUnits, layerIndex, layerCount);
                var currentStartAngleDeg = 0f;
                var segmentIndex = 0;

                while (currentStartAngleDeg < 360f - 0.0001f)
                {
                    var remainingAngleDeg = 360f - currentStartAngleDeg;
                    var randomArcLengthUnits = Random.Range(minArcLengthUnits, maxArcLengthUnits);
                    var randomArcAngleDeg = randomArcLengthUnits / Mathf.Max(0.0001f, middleRadius) * Mathf.Rad2Deg;
                    var minSegmentAngleDeg = Mathf.Min(0.05f, remainingAngleDeg);
                    var segmentArcAngleDeg = Mathf.Clamp(randomArcAngleDeg, minSegmentAngleDeg, remainingAngleDeg);
                    var spriteSegmentAngleDeg = Mathf.Min(359.9f, segmentArcAngleDeg + segmentOverlapDegrees);
                    var centerAngleDeg = currentStartAngleDeg + segmentArcAngleDeg * 0.5f;

                    var profile = _segmentProbabilitySelector.PickProfile(segmentPrefabs, placementContext);
                    var profileId = profile ? profile.GetInstanceID() : 0;
                    var angleKey = Mathf.RoundToInt(spriteSegmentAngleDeg * 1000f);
                    var cacheKey = $"{profileId}:{angleKey}";

                    if (!cachedSpritesByProfileAndAngle.TryGetValue(cacheKey, out var sprite))
                    {
                        sprite = _spriteFactory.CreateRingSegmentSprite(layerInnerRadius, layerOuterRadius, spriteSegmentAngleDeg, fallbackColor, profile);
                        cachedSpritesByProfileAndAngle[cacheKey] = sprite;
                        yield return null;
                    }

                    var segmentObject = PlanetHierarchyUtility.CreateObjectFromProfile($"Segment_{segmentIndex}", layerObject.transform, profile);
                    segmentObject.transform.localRotation = Quaternion.Euler(0f, 0f, centerAngleDeg);

                    var renderer = PlanetHierarchyUtility.GetOrAddSpriteRenderer(segmentObject);
                    renderer.sprite = sprite;
                    ConfigureSegmentCollider(segmentObject);

                    currentStartAngleDeg += segmentArcAngleDeg;
                    segmentIndex++;

                    if (segmentIndex % segmentBatchSize == 0)
                        yield return null;
                }

                currentInnerRadius = outerRadius;
                yield return null;
            }
        }

        private void ConfigureSpriteFactory()
        {
            var settings = new PlanetSpriteFactory.Settings(
                pixelsPerUnit,
                filterMode,
                textureTileSizeUnits,
                textureUvOffset,
                enableSegmentOutline,
                segmentOutlineColor,
                segmentOutlineWidthPixels
            );

            if (_spriteFactory == null)
                _spriteFactory = new PlanetSpriteFactory(this, gameObject.name, settings);
            else
                _spriteFactory.Reconfigure(this, gameObject.name, settings);
        }

        private void NormalizeInput()
        {
            circleRadiusUnits = Mathf.Max(0.01f, circleRadiusUnits);
            segmentArcLengthMinUnits = Mathf.Max(0.05f, segmentArcLengthMinUnits);
            segmentArcLengthMaxUnits = Mathf.Max(0.05f, segmentArcLengthMaxUnits);
            layerThicknessUnits = Mathf.Max(0.05f, layerThicknessUnits);
            layerOverlapUnits = Mathf.Max(0f, layerOverlapUnits);
            segmentOverlapDegrees = Mathf.Max(0f, segmentOverlapDegrees);
            pixelsPerUnit = Mathf.Max(16f, pixelsPerUnit);
            textureTileSizeUnits = Mathf.Max(0.02f, textureTileSizeUnits);
            segmentOutlineWidthPixels = Mathf.Max(1f, segmentOutlineWidthPixels);
            editorFrameBudgetMs = Mathf.Max(0.5f, editorFrameBudgetMs);
            runtimeFrameBudgetMs = Mathf.Max(0.5f, runtimeFrameBudgetMs);
            segmentBatchSize = Mathf.Max(1, segmentBatchSize);
        }

        private float GetEstimatedOuterRadiusUnits()
        {
            var normalizedCircleRadius = Mathf.Max(0.01f, circleRadiusUnits);
            var normalizedLayerThickness = Mathf.Max(0.05f, layerThicknessUnits);
            var normalizedLayerOverlap = Mathf.Max(0f, layerOverlapUnits);
            var layerCount = layerRotations?.Count ?? 0;

            if (layerCount <= 0)
                return normalizedCircleRadius;

            return normalizedCircleRadius + normalizedLayerThickness * layerCount + normalizedLayerOverlap * 0.5f;
        }

        private void ConfigureCenterCollider(GameObject target)
        {
            var collider = target.GetComponent<CircleCollider2D>();
            if (!collider)
                collider = target.AddComponent<CircleCollider2D>();

            collider.radius = Mathf.Max(0.01f, circleRadiusUnits);
            collider.offset = Vector2.zero;
            collider.isTrigger = false;
        }

        private static void ConfigureSegmentCollider(GameObject target)
        {
            var collider = target.GetComponent<PolygonCollider2D>();
            if (!collider)
                collider = target.AddComponent<PolygonCollider2D>();

            collider.isTrigger = false;
        }
    }
}
