using UnityEngine;

namespace App.Planets.Core
{
    internal static class PlanetHierarchyUtility
    {
        public static Transform GetOrCreateGeneratedRoot(Transform owner, string generatedRootName)
        {
            var generatedRoot = owner.Find(generatedRootName);
            if (generatedRoot)
                return generatedRoot;

            var rootObject = new GameObject(generatedRootName);
            rootObject.transform.SetParent(owner, false);
            return rootObject.transform;
        }

        public static void ClearChildren(Transform parent, bool isPlaying)
        {
            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (isPlaying)
                    Object.Destroy(child);
                else
                    Object.DestroyImmediate(child);
            }
        }

        public static GameObject CreateObjectFromProfile(string objectName, Transform parent, PlanetSegmentProfile profile)
        {
            GameObject instance;
            if (profile != null)
            {
                instance = Object.Instantiate(profile.gameObject, parent, false);
                instance.name = objectName;
            }
            else
            {
                instance = new GameObject(objectName);
                instance.transform.SetParent(parent, false);
            }

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            return instance;
        }

        public static SpriteRenderer GetOrAddSpriteRenderer(GameObject gameObject)
        {
            var renderer = gameObject.GetComponent<SpriteRenderer>();
            if (renderer == null)
                renderer = gameObject.AddComponent<SpriteRenderer>();

            return renderer;
        }
    }
}
