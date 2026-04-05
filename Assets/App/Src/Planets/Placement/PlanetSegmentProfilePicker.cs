using System.Collections.Generic;
using App.Planets.Core;
using UnityEngine;

namespace App.Planets.Placement
{
    internal static class PlanetSegmentProfilePicker
    {
        public static PlanetSegmentProfile PickRandomProfile(IReadOnlyList<PlanetSegmentProfile> pool)
        {
            if (pool == null || pool.Count == 0)
                return null;

            var validCount = 0;
            for (var i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                    validCount++;
            }

            if (validCount == 0)
                return null;

            var randomValidIndex = Random.Range(0, validCount);
            var currentValidIndex = 0;

            for (var i = 0; i < pool.Count; i++)
            {
                var profile = pool[i];
                if (profile == null)
                    continue;

                if (currentValidIndex == randomValidIndex)
                    return profile;

                currentValidIndex++;
            }

            return null;
        }
    }
}
