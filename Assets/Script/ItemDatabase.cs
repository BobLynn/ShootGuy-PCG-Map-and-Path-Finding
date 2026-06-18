using System.Collections.Generic;
using UnityEngine;

namespace InventorySystem
{
    [CreateAssetMenu(fileName = "NewItemDatabase", menuName = "Inventory System/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        [Tooltip("Drag your ItemInitializer prefabs/assets here directly.")]
        public List<ItemInitializer> globalItems;

        /// <summary>
        /// Looks up a sprite by its type name completely independent of scene states.
        /// </summary>
        public Sprite GetSpriteByTypeName(string typeName)
        {
            if (globalItems == null) return null;

            ItemInitializer match = globalItems.Find(x => x.GetItemType() == typeName);
            return match != null ? match.GetItemImage() : null;
        }
    }
}