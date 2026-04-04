using System;
using UnityEngine;

namespace App.Infrastructure.DI
{
    [Serializable]
    public class GameObjectAssetReference
    {
        [SerializeField] private GameObject prefab;

        public GameObject Prefab => prefab;
        public bool IsValid => prefab != null;
    }
}
