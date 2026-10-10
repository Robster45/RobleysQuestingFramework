using System;
using System.Collections.Generic;
using System.Linq;

namespace RobleysQuestingFramework
{
    public readonly struct HandInStack
    {
        public readonly string Prefab;
        public readonly int Quality;
        public readonly int Quantity;
        public HandInStack(string prefab, int quality, int quantity)
        { Prefab = prefab; Quality = quality; Quantity = quantity; }
    }

    // Plans all removals before mutating inventory; exact-quality requirements reserve first.
    public static class ResourceHandInPlan
    {
        public static int[] Create(IReadOnlyList<ResourceCollectionCriteria> requirements, IReadOnlyList<HandInStack> stacks)
        {
            int[] removal = new int[stacks.Count];
            foreach (ResourceCollectionCriteria requirement in requirements.OrderByDescending(r => r.Quality.HasValue))
            {
                int remaining = requirement.Quantity;
                for (int i = 0; i < stacks.Count && remaining > 0; i++)
                {
                    HandInStack stack = stacks[i];
                    if (stack.Prefab != requirement.PrefabName ||
                        (requirement.Quality.HasValue && stack.Quality != requirement.Quality.Value))
                        continue;
                    int count = Math.Min(remaining, Math.Max(0, stack.Quantity - removal[i]));
                    removal[i] += count;
                    remaining -= count;
                }
                if (remaining != 0)
                    return null;
            }
            return removal;
        }
    }
}
