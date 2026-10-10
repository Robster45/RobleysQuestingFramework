using System;
using UnityEngine;

namespace RobleysQuestingFramework
{
    public static class ItemRewardService
    {
        // Call on the receiving character's client from a registered reward handler.
        // False means no effects were applied; successful ground drops count as delivery.
        public static bool GiveOrDrop(Player player, string prefabName, int amount)
            => GiveOrDrop(player, prefabName, amount, 1);

        // Explicit quality permits special quest rewards above the normal crafting cap.
        public static bool GiveOrDrop(Player player, string prefabName, int amount, int quality)
        {
            if (!player || amount <= 0 || quality <= 0 || !ObjectDB.instance)
            {
                return false;
            }
            GameObject prefab = ObjectDB.instance.GetItemPrefab(prefabName);
            ItemDrop template = prefab ? prefab.GetComponent<ItemDrop>() : null;
            if (!template || template.m_itemData.m_shared.m_maxStackSize <= 0)
            {
                return false;
            }
            Inventory inventory = player.GetInventory();
            int remaining = amount;
            while (remaining > 0)
            {
                ItemDrop.ItemData item = template.m_itemData.Clone();
                item.m_dropPrefab = prefab;
                item.m_worldLevel = Game.m_worldLevel;
                item.m_quality = quality;
                item.m_durability = item.GetMaxDurability();
                item.m_stack = Math.Min(remaining, item.m_shared.m_maxStackSize);
                remaining -= item.m_stack;
                // Vanilla AddItem can partially fill existing stacks before returning false.
                // In that case m_stack contains only the remainder that did not fit.
                if (!inventory.AddItem(item))
                {
                    ItemDrop.DropItem(item, item.m_stack, player.GetCenterPoint(), Quaternion.identity);
                }
            }
            return true;
        }
    }
}
