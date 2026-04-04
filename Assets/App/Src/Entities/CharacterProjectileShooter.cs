using UnityEngine;

namespace App.Entities
{
    public class CharacterProjectileShooter : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private ProjectileLoadoutSelector projectileSelector;
        [SerializeField] private PlanetOrbitMovement orbitMovement;

        [Header("Spawn")]
        [SerializeField] private Transform projectileSpawnPoint;

        public bool ShootAtPlanetCenter()
        {
            if (projectileSelector == null)
                return false;

            if (!projectileSelector.TryGetSelectedProjectile(out var projectilePrefab))
                return false;

            if (orbitMovement == null || orbitMovement.OrbitCenter == null)
                return false;

            var spawnPosition = projectileSpawnPoint ? projectileSpawnPoint.position : transform.position;
            var centerPosition = orbitMovement.OrbitCenter.position;
            var direction = centerPosition - spawnPosition;
            if (direction.sqrMagnitude <= 0.000001f)
                return false;

            return SpawnAndLaunch(projectilePrefab, spawnPosition, direction.normalized);
        }

        private bool SpawnAndLaunch(Projectile projectilePrefab, Vector3 position, Vector3 direction)
        {
            if (projectilePrefab == null)
                return false;

            var instance = Instantiate(projectilePrefab, position, Quaternion.identity, null);
            instance.Launch(direction);
            return true;
        }
    }
}
