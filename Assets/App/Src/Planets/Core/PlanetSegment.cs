using UnityEngine;

namespace App.Planets.Core
{
    [DisallowMultipleComponent]
    public class PlanetSegment : MonoBehaviour
    {
        [SerializeField] private PlanetSegmentMaterial material;
        [SerializeField] [Min(0)] private int initialMaterialPoints;
        [SerializeField] [Min(0)] private int currentMaterialPoints;
        [SerializeField] [Min(0f)] private float segmentAreaUnits;
        [SerializeField] private bool isCoreSegment;
        [SerializeField] private bool isDestroyed;

        public PlanetSegmentMaterial Material => material;
        public int InitialMaterialPoints => initialMaterialPoints;
        public int CurrentMaterialPoints => currentMaterialPoints;
        public float SegmentAreaUnits => segmentAreaUnits;
        public bool IsCoreSegment => isCoreSegment;
        public bool IsDestroyed => isDestroyed;

        public void InitializeGenerated(PlanetSegmentMaterial materialType, int initialPoints, float areaUnits, bool coreSegment)
        {
            material = materialType;
            initialMaterialPoints = Mathf.Max(1, initialPoints);
            currentMaterialPoints = initialMaterialPoints;
            segmentAreaUnits = Mathf.Max(0f, areaUnits);
            isCoreSegment = coreSegment;
            isDestroyed = false;
            ApplyDestroyedState();
        }

        public void RestoreState(
            PlanetSegmentMaterial materialType,
            int initialPoints,
            int currentPoints,
            float areaUnits,
            bool coreSegment,
            bool destroyed)
        {
            material = materialType;
            initialMaterialPoints = Mathf.Max(1, initialPoints);
            currentMaterialPoints = Mathf.Clamp(currentPoints, 0, initialMaterialPoints);
            segmentAreaUnits = Mathf.Max(0f, areaUnits);
            isCoreSegment = coreSegment;
            isDestroyed = destroyed || currentMaterialPoints <= 0;
            if (isDestroyed)
                currentMaterialPoints = 0;
            ApplyDestroyedState();
        }

        public bool ApplyDamage(int damagePoints)
        {
            if (isDestroyed || damagePoints <= 0)
                return false;

            currentMaterialPoints = Mathf.Max(0, currentMaterialPoints - damagePoints);
            if (currentMaterialPoints > 0)
                return false;

            isDestroyed = true;
            ApplyDestroyedState();
            return true;
        }

        private void ApplyDestroyedState()
        {
            var renderer = GetComponent<SpriteRenderer>();
            if (renderer)
                renderer.enabled = !isDestroyed;

            var colliders = GetComponents<Collider2D>();
            for (var i = 0; i < colliders.Length; i++)
                colliders[i].enabled = !isDestroyed;
        }
    }
}
