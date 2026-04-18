using UnityEngine;

namespace App.Entities
{
    public class Projectile : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] [Min(0.01f)] private float speedUnitsPerSecond = 12f;
        [SerializeField] [Min(0f)] private float maxLifetimeSeconds = 8f;

        [Header("Interaction")]
        [SerializeField] private LayerMask interactionLayers = Physics2D.DefaultRaycastLayers;

        private Vector3 _direction;
        private float _lifetime;
        private bool _isLaunched;

        public float SpeedUnitsPerSecond => speedUnitsPerSecond;
        protected bool IsLaunched => _isLaunched;
        protected Vector3 Direction => _direction;

        public void Launch(Vector3 direction)
        {
            var normalizedDirection = direction.sqrMagnitude > 0.000001f
                ? direction.normalized
                : Vector3.up;

            _direction = normalizedDirection;
            _lifetime = 0f;
            _isLaunched = true;
        }

        public void SetSpeed(float speed)
        {
            speedUnitsPerSecond = Mathf.Max(0.01f, speed);
        }

        protected virtual void Update()
        {
            if (!_isLaunched)
                return;

            transform.position += _direction * (speedUnitsPerSecond * Time.deltaTime);

            if (maxLifetimeSeconds <= 0f)
                return;

            _lifetime += Time.deltaTime;
            if (_lifetime < maxLifetimeSeconds)
                return;

            DestroySelf();
        }

        protected void StopMovement()
        {
            _isLaunched = false;
        }

        protected bool CanInteractWith(Collider2D other)
        {
            if (other == null)
                return false;

            var otherLayerMask = 1 << other.gameObject.layer;
            return (interactionLayers.value & otherLayerMask) != 0;
        }

        protected void DestroySelf()
        {
            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }
    }
}
