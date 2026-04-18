using System;
using System.Collections;
using System.Collections.Generic;
using App.Planets.Core;
using App.Planets.Placement;
using UnityEditor;
using UnityEngine;
using Random = UnityEngine.Random;

namespace App.Planets.Generation
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
        public float EstimatedOuterRadiusUnits => _generatedOuterRadiusUnits > 0f ? _generatedOuterRadiusUnits : GetEstimatedOuterRadiusUnits();

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
        
        [Header("Layer Count Bounds")]
        [SerializeField] [Min(0)] private int layerCountLeftBoundary;
        [SerializeField] [Min(0)] private int layerCountRightBoundary;

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
        
        [Header("Segment Rendering")]
        [SerializeField] private bool useMaskedSegmentRendering = true;
        [SerializeField] [Range(0.1f, 16f)] private float coreTextureScale = 1f;
        [SerializeField] [Range(0.1f, 16f)] private float segmentTextureScale = 1f;

        [Header("Async Generation")]
        [SerializeField] [Range(0.5f, 25f)] private float editorFrameBudgetMs = 4f;
        [SerializeField] [Range(0.5f, 25f)] private float runtimeFrameBudgetMs = 4f;
        [SerializeField] [Range(1, 256)] private int segmentBatchSize = 32;
        [SerializeField] private bool generateOnStartRuntime = true;

        private const string GeneratedRootName = "__GeneratedPlanet";
        private PlanetSpriteFactory _spriteFactory;
        private readonly PlanetSegmentProbabilitySelector _segmentProbabilitySelector = new PlanetSegmentProbabilitySelector();
        private Coroutine _runtimeGenerationCoroutine;
        private Sprite _maskedSegmentSprite;
        private Texture2D _maskedSegmentTexture;
        private float _generatedOuterRadiusUnits = -1f;
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
            DestroyMaskedSegmentSprite();
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
            SetGeneratedOuterRadiusUnits(-1f);

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
            if (useMaskedSegmentRendering)
            {
                if (_maskedSegmentSprite == null)
                    _maskedSegmentSprite = CreateMaskedSegmentSprite();

                renderer.sprite = _maskedSegmentSprite;
                circleObject.transform.localScale = Vector3.one * (circleRadiusUnits * 2f);
                var mask = circleObject.GetComponent<PlanetSegmentRenderMask>();
                if (!mask)
                    mask = circleObject.AddComponent<PlanetSegmentRenderMask>();

                var fillColor = profile ? profile.Tint : circleColor;
                var coreScale = profile ? profile.TextureScale * coreTextureScale : coreTextureScale;
                mask.Configure(
                    0f,
                    0.5f,
                    180f,
                    profile ? profile.FillTexture : null,
                    fillColor,
                    coreScale,
                    outlineEnabled: false,
                    outline: Color.clear,
                    outlineNorm: 0f,
                    tileSizeUnits: textureTileSizeUnits,
                    uvOffset: textureUvOffset);
                mask.Apply(renderer);
            }
            else
            {
                renderer.sprite = _spriteFactory.CreateCircleSprite(circleRadiusUnits, circleColor, profile);
            }
            ConfigureCenterCollider(circleObject);

            var centerAreaUnits = PlanetSegmentPointsCalculator.CalculateCircleAreaUnits(circleRadiusUnits);
            var centerPoints = PlanetSegmentPointsCalculator.CalculatePoints(centerAreaUnits, PlanetSegmentMaterial.Magma);
            InitializeSegmentData(circleObject, PlanetSegmentMaterial.Magma, centerPoints, centerAreaUnits, isCoreSegment: true);
            SetGeneratedOuterRadiusUnits(circleRadiusUnits);
        }

        private IEnumerator CreateLayersIncremental(Transform generatedRoot)
        {
            var layerCount = ResolveLayerCountForGeneration();
            if (layerCount == 0)
            {
                Debug.LogWarning("No planet layers generated: configure Layer Count Bounds or Per Layer Rotation.", this);
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

                    var segmentObject = PlanetHierarchyUtility.CreateObjectFromProfile($"Segment_{segmentIndex}", layerObject.transform, profile);
                    segmentObject.transform.localRotation = Quaternion.Euler(0f, 0f, centerAngleDeg);

                    var renderer = PlanetHierarchyUtility.GetOrAddSpriteRenderer(segmentObject);
                    if (useMaskedSegmentRendering)
                    {
                        if (_maskedSegmentSprite == null)
                            _maskedSegmentSprite = CreateMaskedSegmentSprite();

                        renderer.sprite = _maskedSegmentSprite;
                        segmentObject.transform.localScale = Vector3.one * (layerOuterRadius * 2f);

                        var outerNorm = 0.5f;
                        var innerNorm = layerOuterRadius <= 0.0001f
                            ? 0f
                            : Mathf.Clamp(layerInnerRadius / (layerOuterRadius * 2f), 0f, outerNorm);
                        var fillColor = profile ? profile.Tint : fallbackColor;
                        var segmentScale = profile ? profile.TextureScale * segmentTextureScale : segmentTextureScale;
                        var outlineNorm = (segmentOutlineWidthPixels / Mathf.Max(1f, pixelsPerUnit * layerOuterRadius * 2f)) * 0.5f;
                        var mask = segmentObject.GetComponent<PlanetSegmentRenderMask>();
                        if (!mask)
                            mask = segmentObject.AddComponent<PlanetSegmentRenderMask>();

                        mask.Configure(
                            innerNorm,
                            outerNorm,
                            spriteSegmentAngleDeg * 0.5f,
                            profile ? profile.FillTexture : null,
                            fillColor,
                            segmentScale,
                            enableSegmentOutline,
                            segmentOutlineColor,
                            outlineNorm,
                            textureTileSizeUnits,
                            textureUvOffset);
                        mask.Apply(renderer);
                        ConfigureSegmentCollider(segmentObject, innerNorm, outerNorm, segmentArcAngleDeg);
                    }
                    else
                    {
                        var profileId = profile ? profile.GetInstanceID() : 0;
                        var angleKey = Mathf.RoundToInt(spriteSegmentAngleDeg * 1000f);
                        var cacheKey = $"{profileId}:{angleKey}";

                        if (!cachedSpritesByProfileAndAngle.TryGetValue(cacheKey, out var sprite))
                        {
                            sprite = _spriteFactory.CreateRingSegmentSprite(layerInnerRadius, layerOuterRadius, spriteSegmentAngleDeg, fallbackColor, profile);
                            cachedSpritesByProfileAndAngle[cacheKey] = sprite;
                            yield return null;
                        }

                        renderer.sprite = sprite;
                        ConfigureSegmentCollider(segmentObject);
                    }

                    var material = profile ? profile.Material : PlanetSegmentMaterial.Stone;
                    var segmentAreaUnits = PlanetSegmentPointsCalculator.CalculateRingSegmentAreaUnits(layerInnerRadius, layerOuterRadius, segmentArcAngleDeg);
                    var segmentPoints = PlanetSegmentPointsCalculator.CalculatePoints(segmentAreaUnits, material);
                    InitializeSegmentData(segmentObject, material, segmentPoints, segmentAreaUnits, isCoreSegment: false);

                    currentStartAngleDeg += segmentArcAngleDeg;
                    segmentIndex++;

                    if (segmentIndex % segmentBatchSize == 0)
                        yield return null;
                }

                currentInnerRadius = outerRadius;
                yield return null;
            }

            SetGeneratedOuterRadiusUnits(currentInnerRadius);
        }

        public void SetGeneratedOuterRadiusUnits(float radiusUnits)
        {
            _generatedOuterRadiusUnits = radiusUnits > 0f ? Mathf.Max(0.01f, radiusUnits) : -1f;
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
            coreTextureScale = Mathf.Clamp(coreTextureScale, 0.1f, 16f);
            segmentTextureScale = Mathf.Clamp(segmentTextureScale, 0.1f, 16f);
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
            var layerCount = ResolveEstimatedLayerCount();

            if (layerCount <= 0)
                return normalizedCircleRadius;

            return normalizedCircleRadius + normalizedLayerThickness * layerCount + normalizedLayerOverlap * 0.5f;
        }

        private int ResolveLayerCountForGeneration()
        {
            var left = Mathf.Max(0, layerCountLeftBoundary);
            var right = Mathf.Max(0, layerCountRightBoundary);

            if (right <= 0)
                return Mathf.Max(0, layerRotations?.Count ?? 0);

            if (left > right)
                (left, right) = (right, left);

            return Random.Range(left, right + 1);
        }

        private int ResolveEstimatedLayerCount()
        {
            var left = Mathf.Max(0, layerCountLeftBoundary);
            var right = Mathf.Max(0, layerCountRightBoundary);

            if (right <= 0)
                return Mathf.Max(0, layerRotations?.Count ?? 0);

            return Mathf.Max(left, right);
        }

        private void ConfigureCenterCollider(GameObject target)
        {
            var collider = target.GetComponent<CircleCollider2D>();
            if (!collider)
                collider = target.AddComponent<CircleCollider2D>();

            var safeScaleX = Mathf.Max(0.0001f, Mathf.Abs(target.transform.localScale.x));
            collider.radius = Mathf.Max(0.01f, circleRadiusUnits / safeScaleX);
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

        private static void ConfigureSegmentCollider(GameObject target, float innerNorm, float outerNorm, float angleDeg)
        {
            var collider = target.GetComponent<PolygonCollider2D>();
            if (!collider)
                collider = target.AddComponent<PolygonCollider2D>();

            var halfAngleRad = Mathf.Deg2Rad * Mathf.Clamp(angleDeg * 0.5f, 0.1f, 179.9f);
            var arcSteps = Mathf.Max(2, Mathf.CeilToInt(angleDeg / 12f));
            var points = new List<Vector2>(arcSteps * 2 + 2);

            for (var i = 0; i <= arcSteps; i++)
            {
                var t = i / (float)arcSteps;
                var angle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
                points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * outerNorm);
            }

            if (innerNorm > 0.0001f)
            {
                for (var i = arcSteps; i >= 0; i--)
                {
                    var t = i / (float)arcSteps;
                    var angle = Mathf.Lerp(-halfAngleRad, halfAngleRad, t);
                    points.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * innerNorm);
                }
            }
            else
            {
                points.Add(Vector2.zero);
            }

            collider.pathCount = 1;
            collider.SetPath(0, points.ToArray());
            collider.isTrigger = false;
        }

        private static Sprite CreateMaskedSegmentSprite()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, mipChain: false)
            {
                name = "PlanetMaskBaseTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[16];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = Color.white;
            texture.SetPixels(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);
        }

        private void DestroyMaskedSegmentSprite()
        {
            if (_maskedSegmentSprite == null)
                return;

            _maskedSegmentTexture = _maskedSegmentSprite.texture;

            if (Application.isPlaying)
                Destroy(_maskedSegmentSprite);
            else
                DestroyImmediate(_maskedSegmentSprite);

            _maskedSegmentSprite = null;

            if (_maskedSegmentTexture == null)
                return;

            if (Application.isPlaying)
                Destroy(_maskedSegmentTexture);
            else
                DestroyImmediate(_maskedSegmentTexture);

            _maskedSegmentTexture = null;
        }

        private static void InitializeSegmentData(
            GameObject segmentObject,
            PlanetSegmentMaterial material,
            int initialPoints,
            float segmentAreaUnits,
            bool isCoreSegment)
        {
            var segment = segmentObject.GetComponent<PlanetSegment>();
            if (!segment)
                segment = segmentObject.AddComponent<PlanetSegment>();

            segment.InitializeGenerated(material, initialPoints, segmentAreaUnits, isCoreSegment);
        }
    }
}
