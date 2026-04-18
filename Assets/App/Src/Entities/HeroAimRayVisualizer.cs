using App.Planets.Persistence;
using UnityEngine;

namespace App.Entities
{
    public class HeroAimRayVisualizer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameplayAimJoystick aimJoystick;
        [SerializeField] private LineRenderer lineRenderer;
        [SerializeField] private Transform heroTransform;
        [SerializeField] private bool autoFindHero = false;

        [Header("Raycast")]
        [SerializeField] [Min(0.1f)] private float maxDistance = 250f;
        [SerializeField] private LayerMask raycastMask = Physics2D.DefaultRaycastLayers;
        [SerializeField] [Min(0f)] private float minAimMagnitude = 0.1f;

        private Vector2 _currentAimDirection;
        private bool _isAiming;

        private void Awake()
        {
            ConfigureLineRenderer();
            HideLine();
        }

        private void OnEnable()
        {
            BindJoystick();
        }

        private void OnDisable()
        {
            UnbindJoystick();
            HideLine();
        }

        private void LateUpdate()
        {
            if (!_isAiming)
                return;

            UpdateRayVisual();
        }

        private void OnValidate()
        {
            ConfigureLineRenderer();
        }

        public void SetAimJoystick(GameplayAimJoystick joystick)
        {
            if (aimJoystick == joystick)
                return;

            UnbindJoystick();
            aimJoystick = joystick;
            if (isActiveAndEnabled)
                BindJoystick();
        }

        private void BindJoystick()
        {
            UnbindJoystick();

            if (!aimJoystick)
                return;

            aimJoystick.DirectionChanged += OnAimDirectionChanged;
            aimJoystick.Released += OnAimReleased;
        }

        private void UnbindJoystick()
        {
            if (!aimJoystick)
                return;

            aimJoystick.DirectionChanged -= OnAimDirectionChanged;
            aimJoystick.Released -= OnAimReleased;
        }

        private void OnAimDirectionChanged(Vector2 direction)
        {
            _currentAimDirection = direction;
            _isAiming = direction.sqrMagnitude >= minAimMagnitude * minAimMagnitude;

            if (!_isAiming)
            {
                HideLine();
                return;
            }

            UpdateRayVisual();
        }

        private void OnAimReleased()
        {
            _isAiming = false;
            _currentAimDirection = Vector2.zero;
            HideLine();
        }

        private void UpdateRayVisual()
        {
            if (!TryResolveHeroTransform(out var hero))
            {
                HideLine();
                return;
            }

            var direction = _currentAimDirection.normalized;
            var origin = (Vector2)hero.position + direction;
            var hit = Physics2D.Raycast(origin, direction, maxDistance, raycastMask);

            var endPoint = hit.collider != null
                ? hit.point
                : origin + direction * maxDistance;

            if (lineRenderer == null)
                return;

            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, new Vector3(origin.x, origin.y, hero.position.z));
            lineRenderer.SetPosition(1, new Vector3(endPoint.x, endPoint.y, hero.position.z));
        }

        private bool TryResolveHeroTransform(out Transform hero)
        {
            if (heroTransform == null)
                heroTransform = transform;

            if (heroTransform == null && autoFindHero)
            {
                var heroTag = FindFirstObjectByType<EntityHeroTag>();
                if (heroTag != null)
                    heroTransform = heroTag.transform;
            }

            hero = heroTransform;
            return hero != null;
        }

        private void ConfigureLineRenderer()
        {
            if (lineRenderer == null)
                return;

            lineRenderer.useWorldSpace = true;
            lineRenderer.positionCount = 2;
        }

        private void HideLine()
        {
            if (lineRenderer != null)
                lineRenderer.enabled = false;
        }
    }
}
