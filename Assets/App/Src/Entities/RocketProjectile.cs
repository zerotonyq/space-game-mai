using App.Planets.Core;
using UnityEngine;

namespace App.Entities
{
    public class RocketProjectile : Projectile
    {
        [SerializeField] private bool destroyOnAnySurfaceHit = true;
        [SerializeField] private bool canDestroyHero = true;

        private bool _consumed;

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandleHit(other, other != null ? other.ClosestPoint(transform.position) : transform.position);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            var point = collision.contactCount > 0 ? collision.GetContact(0).point : (Vector2)transform.position;
            HandleHit(collision.collider, point);
        }

        private void HandleHit(Collider2D other, Vector2 _)
        {
            if (_consumed || other == null)
                return;

            if (!CanInteractWith(other))
                return;

            _consumed = true;

            if (TryDestroyEntity(other, canDestroyHero))
            {
                DestroySelf();
                return;
            }

            var segment = other.GetComponentInParent<PlanetSegment>();
            if (segment != null && !segment.IsDestroyed)
            {
                var damage = Mathf.Max(1, segment.CurrentMaterialPoints);
                segment.ApplyDamage(damage);
                DestroySelf();
                return;
            }

            if (destroyOnAnySurfaceHit)
                DestroySelf();
            else
                _consumed = false;
        }

        private static bool TryDestroyEntity(Collider2D other, bool canDestroyHero)
        {
            var hero = other.GetComponentInParent<EntityHeroTag>();
            if (hero != null)
            {
                if (!canDestroyHero)
                    return false;

                hero.gameObject.SetActive(false);
                Destroy(hero.gameObject);
                return true;
            }

            var peaceful = other.GetComponentInParent<EntityPeacefulTag>();
            if (peaceful != null)
            {
                peaceful.gameObject.SetActive(false);
                Destroy(peaceful.gameObject);
                return true;
            }

            var villain = other.GetComponentInParent<EntityVillainTag>();
            if (villain != null)
            {
                villain.gameObject.SetActive(false);
                Destroy(villain.gameObject);
                return true;
            }

            return false;
        }
    }
}
