using System.Collections.Generic;
using UnityEngine;

namespace App.Entities
{
    public class ProjectileLoadoutSelector : MonoBehaviour
    {
        [SerializeField] private List<Projectile> availableProjectilePrefabs = new();
        [SerializeField] [Min(0)] private int selectedIndex;

        public Projectile SelectedProjectile => GetProjectileByIndex(selectedIndex);

        public bool SelectProjectile(int index)
        {
            if (index < 0 || index >= availableProjectilePrefabs.Count)
                return false;

            if (availableProjectilePrefabs[index] == null)
                return false;

            selectedIndex = index;
            return true;
        }

        public bool SelectProjectile(Projectile projectilePrefab)
        {
            if (projectilePrefab == null)
                return false;

            for (var i = 0; i < availableProjectilePrefabs.Count; i++)
            {
                if (availableProjectilePrefabs[i] != projectilePrefab)
                    continue;

                selectedIndex = i;
                return true;
            }

            return false;
        }

        public bool TryGetSelectedProjectile(out Projectile projectilePrefab)
        {
            projectilePrefab = SelectedProjectile;
            return projectilePrefab != null;
        }

        private Projectile GetProjectileByIndex(int index)
        {
            if (index < 0 || index >= availableProjectilePrefabs.Count)
                return null;

            return availableProjectilePrefabs[index];
        }
    }
}
