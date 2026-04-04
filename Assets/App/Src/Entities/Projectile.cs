using UnityEngine;

namespace App.Entities
{
    public class Projectile : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] [Min(0.01f)] private float speedUnitsPerSecond = 12f;
        [SerializeField] [Min(0f)] private float maxLifetimeSeconds = 8f;

        private Vector3 _direction;
        private float _lifetime;
        private bool _isLaunched;

        public float SpeedUnitsPerSecond => speedUnitsPerSecond;

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

        private void Update()
        {
            if (!_isLaunched)
                return;

            transform.position += _direction * speedUnitsPerSecond * Time.deltaTime;

            if (maxLifetimeSeconds <= 0f)
                return;

            _lifetime += Time.deltaTime;
            if (_lifetime < maxLifetimeSeconds)
                return;

            if (Application.isPlaying)
                Destroy(gameObject);
            else
                DestroyImmediate(gameObject);
        }
    }
}
