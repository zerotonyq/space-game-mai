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

        [Header("Typed Projectiles")]
        [SerializeField] private Projectile rocketProjectilePrefab;
        [SerializeField] private Projectile drillProjectileType1Prefab;
        [SerializeField] private Projectile drillProjectileType2Prefab;
        [SerializeField] private Projectile drillProjectileType3Prefab;

        public DrillProjectile DrillProjectileType1Prefab => drillProjectileType1Prefab as DrillProjectile;
        public DrillProjectile DrillProjectileType2Prefab => drillProjectileType2Prefab as DrillProjectile;
        public DrillProjectile DrillProjectileType3Prefab => drillProjectileType3Prefab as DrillProjectile;

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

        public bool ShootRocket() => ShootSpecificProjectile(rocketProjectilePrefab);
        public bool ShootDrillType1() => ShootSpecificProjectile(drillProjectileType1Prefab);
        public bool ShootDrillType2() => ShootSpecificProjectile(drillProjectileType2Prefab);
        public bool ShootDrillType3() => ShootSpecificProjectile(drillProjectileType3Prefab);
        public bool ShootRocketAtWorldPosition(Vector3 worldPosition) => ShootSpecificProjectileAtWorldPosition(rocketProjectilePrefab, worldPosition);

        private bool SpawnAndLaunch(Projectile projectilePrefab, Vector3 position, Vector3 direction)
        {
            if (projectilePrefab == null)
                return false;

            var instance = Instantiate(projectilePrefab, position, Quaternion.identity, null);
            instance.Launch(direction);
            return true;
        }

        private bool ShootSpecificProjectileAtWorldPosition(Projectile projectilePrefab, Vector3 worldPosition)
        {
            if (projectilePrefab == null)
                return false;

            var spawnPosition = projectileSpawnPoint ? projectileSpawnPoint.position : transform.position;
            var direction = worldPosition - spawnPosition;
            if (direction.sqrMagnitude <= 0.000001f)
                return false;

            return SpawnAndLaunch(projectilePrefab, spawnPosition, direction.normalized);
        }

        private bool ShootSpecificProjectile(Projectile projectilePrefab)
        {
            if (projectilePrefab == null)
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
    }
}
