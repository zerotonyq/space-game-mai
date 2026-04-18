using App.Planets.Core;
using UnityEngine;

namespace App.Entities
{
    public class MinedMaterialChunk : MonoBehaviour
    {
        [Header("Pickup")]
        [SerializeField] [Min(0.01f)] private float pickupRadius = 0.35f;

        [SerializeField] private PlanetSegmentMaterial material;
        [SerializeField] [Min(0)] private int materialPoints;

        private CircleCollider2D _pickupTrigger;
        private bool _pickedUp;

        public PlanetSegmentMaterial Material => material;
        public int MaterialPoints => materialPoints;

        private void Awake()
        {
            EnsurePickupTrigger();
        }

        public void Initialize(PlanetSegmentMaterial chunkMaterial, int points)
        {
            material = chunkMaterial;
            materialPoints = Mathf.Max(0, points);
            EnsurePickupTrigger();
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            TryPickup(other);
        }

        private void OnTriggerStay2D(Collider2D other)
        {
            TryPickup(other);
        }

        private void TryPickup(Collider2D other)
        {
            if (_pickedUp || other == null || materialPoints <= 0)
                return;

            var inventory = other.GetComponentInParent<EntityMaterialInventory>();
            if (!inventory)
                return;

            _pickedUp = true;
            inventory.Add(material, materialPoints);
            Destroy(gameObject);
        }

        private void EnsurePickupTrigger()
        {
            var colliders = GetComponents<Collider2D>();
            for (var i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] is CircleCollider2D)
                    continue;

                Destroy(colliders[i]);
            }

            _pickupTrigger = GetComponent<CircleCollider2D>();
            if (!_pickupTrigger)
                _pickupTrigger = gameObject.AddComponent<CircleCollider2D>();

            _pickupTrigger.isTrigger = true;
            _pickupTrigger.radius = pickupRadius;

            var rb = GetComponent<Rigidbody2D>();
            if (!rb)
                rb = gameObject.AddComponent<Rigidbody2D>();

            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        }
    }
}
