using System.Collections.Generic;
using UnityEngine;

namespace App.Planets.GfxGen
{
    internal sealed class PlanetSegmentProbabilitySelector
    {
        private readonly Dictionary<int, PlanetSegmentSpawnRule> _cachedRulesByProfileId = new Dictionary<int, PlanetSegmentSpawnRule>();

        public PlanetSegmentProfile PickProfile(IReadOnlyList<PlanetSegmentProfile> profiles, PlanetPlacementContext context)
        {
            if (profiles == null || profiles.Count == 0)
                return null;

            var totalWeight = 0f;
            for (var i = 0; i < profiles.Count; i++)
                totalWeight += GetProfileWeight(profiles[i], context);

            if (totalWeight <= 0f)
                return PlanetSegmentProfilePicker.PickRandomProfile(profiles);

            var randomPoint = Random.value * totalWeight;
            var cumulativeWeight = 0f;
            for (var i = 0; i < profiles.Count; i++)
            {
                var profile = profiles[i];
                var weight = GetProfileWeight(profile, context);
                if (weight <= 0f)
                    continue;

                cumulativeWeight += weight;
                if (randomPoint <= cumulativeWeight)
                    return profile;
            }

            return PlanetSegmentProfilePicker.PickRandomProfile(profiles);
        }

        public void ClearCache()
        {
            _cachedRulesByProfileId.Clear();
        }

        private float GetProfileWeight(PlanetSegmentProfile profile, PlanetPlacementContext context)
        {
            if (profile == null)
                return 0f;

            var rule = GetRule(profile);
            if (rule == null)
                return 1f;

            return Mathf.Max(0f, rule.GetWeight(context));
        }

        private PlanetSegmentSpawnRule GetRule(PlanetSegmentProfile profile)
        {
            var profileId = profile.GetInstanceID();
            if (_cachedRulesByProfileId.TryGetValue(profileId, out var cachedRule))
                return cachedRule;

            profile.TryGetComponent<PlanetSegmentSpawnRule>(out var rule);
            _cachedRulesByProfileId[profileId] = rule;
            return rule;
        }
    }
}
