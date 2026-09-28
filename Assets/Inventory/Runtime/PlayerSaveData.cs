using System;
using System.Collections.Generic;

/// <summary>
/// 存档载体：仓库 / 携带 / 装备槽都存成「槽位键 → 物品」的稀疏字典。
/// 旧实现用定长数组 + <c>(EquipmentSlotType)i</c> 下标映射，槽位枚举一改就整体错位；
/// 这里装备槽用槽名做键，新增 / 调整槽位不会影响已有存档。
///
/// v3 起每件物品额外记录实例 ID 与剩余耐久，因此同款装备可以有多件、各自带自己的耐久。
/// </summary>
[Serializable]
public sealed class PlayerSaveData
{
    /// <summary>当前存档格式版本。1 = 旧的定长数组格式，2 = 本字典格式，3 = 带实例 ID 与耐久。</summary>
    public const int CurrentVersion = 3;

    public int version = CurrentVersion;

    /// <summary>仓库：格号 → 物品。</summary>
    public List<SlotEntry> warehouse = new List<SlotEntry>();

    /// <summary>携带：格号 → 物品。</summary>
    public List<SlotEntry> carry = new List<SlotEntry>();

    /// <summary>装备槽：槽名 → 物品。</summary>
    public List<SlotEntry> equipment = new List<SlotEntry>();

    /// <summary>一个「槽位 → 物品」条目。key 对格位是数字字符串，对装备槽是槽名。</summary>
    [Serializable]
    public sealed class SlotEntry
    {
        public string key;
        public string itemId;

        /// <summary>物品实例 ID。v3 起写入；旧存档为空，读回时现场生成。</summary>
        public string instanceId;

        /// <summary>剩余耐久。仅防具有意义；-1 表示不适用（非防具）。</summary>
        public int durability = -1;

        public SlotEntry() { }

        public SlotEntry(string key, string itemId)
        {
            this.key = key;
            this.itemId = itemId;
        }

        public SlotEntry(string key, string itemId, string instanceId, int durability)
        {
            this.key = key;
            this.itemId = itemId;
            this.instanceId = instanceId;
            this.durability = durability;
        }
    }

    public static PlayerSaveData CreateEmpty() => new PlayerSaveData();

    /// <summary>把 null 列表补齐，避免反序列化出半截数据后到处判空。</summary>
    public void Validate()
    {
        warehouse ??= new List<SlotEntry>();
        carry ??= new List<SlotEntry>();
        equipment ??= new List<SlotEntry>();
    }
}
