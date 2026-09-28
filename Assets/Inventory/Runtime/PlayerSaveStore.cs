using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家存档读写：<see cref="PlayerLoadout"/> ↔ <see cref="PlayerSaveData"/> ↔ PlayerPrefs JSON。
/// 只负责序列化与还原，不持有运行时状态 —— 运行时状态在 <see cref="GameSession"/>。
///
/// v3 起每件物品额外记录实例 ID 与剩余耐久，因此同款装备可以有多件、各自带自己的耐久。
/// </summary>
public static class PlayerSaveStore
{
    public const string PrefsKey = "PlayerSave";

    /// <summary>旧版存档键，仅用于一次性迁移。</summary>
    private const string LegacyPrefsKey = "PlayerInventory";

    public static bool HasSave => PlayerPrefs.HasKey(PrefsKey) || PlayerPrefs.HasKey(LegacyPrefsKey);

    /// <summary>读取存档。缺失或解析失败时返回空数据，并保留诊断信息。</summary>
    public static PlayerSaveData Load()
    {
        if (PlayerPrefs.HasKey(PrefsKey))
        {
            var json = PlayerPrefs.GetString(PrefsKey, string.Empty);
            var data = Parse(json);
            if (data != null)
                return data;
        }

        // 新键不存在或损坏时，尝试从旧格式迁移一次。
        if (PlayerPrefs.HasKey(LegacyPrefsKey))
        {
            var migrated = MigrateLegacy();
            if (migrated != null)
            {
                Debug.Log("[PlayerSaveStore] 已从旧版存档迁移玩家数据。");
                Save(migrated);
                return migrated;
            }
        }

        return PlayerSaveData.CreateEmpty();
    }

    /// <summary>把局外数据写入存档。只调用 PlayerPrefs.SetString，真正落盘由 <see cref="Flush"/> 完成。</summary>
    public static void Save(PlayerLoadout loadout)
    {
        Save(Capture(loadout));
    }

    public static void Save(PlayerSaveData data)
    {
        if (data == null)
            return;

        data.version = PlayerSaveData.CurrentVersion;
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
    }

    /// <summary>立即落盘。切后台、退出或切换场景时调用。</summary>
    public static void Flush() => PlayerPrefs.Save();

    /// <summary>删除存档，主要用于测试与「重新开始」。</summary>
    public static void Delete()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.DeleteKey(LegacyPrefsKey);
        PlayerPrefs.Save();
    }

    /// <summary>把局外数据转成存档数据。</summary>
    public static PlayerSaveData Capture(PlayerLoadout loadout)
    {
        var data = PlayerSaveData.CreateEmpty();
        if (loadout == null)
            return data;

        for (var i = 0; i < PlayerLoadout.WarehouseCapacity; i++)
        {
            var item = loadout.GetWarehouse(i);
            if (item != null)
                data.warehouse.Add(ToEntry(i.ToString(), item));
        }

        foreach (var slot in EquipmentSlots.AllSlots)
        {
            var item = loadout.Equipment.Get(slot);
            if (item != null)
                data.equipment.Add(ToEntry(slot.ToString(), item));
        }

        return data;
    }

    /// <summary>把存档数据写回局外容器。返回是否成功应用。</summary>
    public static bool Apply(PlayerSaveData data, PlayerLoadout loadout, ItemDatabase database)
    {
        if (loadout == null)
            return false;

        loadout.ClearAll();

        if (data == null)
        {
            loadout.NotifyChanged();
            return false;
        }

        data.Validate();

        foreach (var entry in data.warehouse)
        {
            if (!int.TryParse(entry.key, out var index))
            {
                Debug.LogError($"存档中的仓库格号「{entry.key}」无法解析，已忽略。");
                continue;
            }

            loadout.SetWarehouse(index, Resolve(entry, database, $"仓库第 {index} 格"));
        }

        foreach (var entry in data.equipment)
        {
            if (!Enum.TryParse(entry.key, out EquipmentSlotType slot) || !EquipmentSlots.IsUsable(slot))
            {
                Debug.LogError($"存档中的装备槽「{entry.key}」无法识别，已忽略该件物品（Id「{entry.itemId}」）。");
                continue;
            }

            loadout.Equipment.Set(slot, Resolve(entry, database, $"装备槽 {slot}"));
        }

        loadout.NotifyChanged();
        return true;
    }

    private static PlayerSaveData.SlotEntry ToEntry(string key, ItemInstance item)
    {
        return new PlayerSaveData.SlotEntry(
            key,
            item.Definition != null ? item.Definition.Id : string.Empty,
            item.InstanceId,
            item.HasDurability ? item.CurrentDurability : -1);
    }

    private static PlayerSaveData Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            var data = JsonUtility.FromJson<PlayerSaveData>(json);
            if (data == null)
            {
                Debug.LogError($"玩家存档为空，已改用空数据。原始内容：{Truncate(json)}");
                return null;
            }

            data.Validate();
            if (data.version > PlayerSaveData.CurrentVersion)
                Debug.LogWarning($"玩家存档版本 {data.version} 高于当前支持的 {PlayerSaveData.CurrentVersion}，只读取可识别的字段。");

            return data;
        }
        catch (Exception exception)
        {
            Debug.LogError($"玩家存档解析失败（{exception.Message}），已改用空数据。原始内容：{Truncate(json)}");
            return null;
        }
    }

    /// <summary>
    /// 把旧版（定长数组 + 下标映射装备槽）存档迁移到新格式。
    /// 旧格式的 bag 对应新仓库，equipped 的下标按旧枚举顺序映射到槽名。
    /// </summary>
    private static PlayerSaveData MigrateLegacy()
    {
        var json = PlayerPrefs.GetString(LegacyPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return null;

        LegacySave legacy;
        try
        {
            legacy = JsonUtility.FromJson<LegacySave>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"旧版存档解析失败（{exception.Message}），已跳过迁移。");
            return null;
        }

        if (legacy == null)
            return null;

        var data = PlayerSaveData.CreateEmpty();

        if (legacy.bag != null)
        {
            for (var i = 0; i < legacy.bag.Length && i < PlayerLoadout.WarehouseCapacity; i++)
            {
                if (!string.IsNullOrEmpty(legacy.bag[i]))
                    data.warehouse.Add(new PlayerSaveData.SlotEntry(i.ToString(), legacy.bag[i]));
            }
        }

        // 旧格式的 equipped 下标顺序即旧 EquipmentSlotType 的数值顺序。
        var legacySlots = new[]
        {
            EquipmentSlotType.None,
            EquipmentSlotType.MainHand,
            EquipmentSlotType.OffHand,
            EquipmentSlotType.Helmet,
            EquipmentSlotType.BodyArmor,
            EquipmentSlotType.Boots,
        };

        if (legacy.equipped != null)
        {
            for (var i = 0; i < legacy.equipped.Length && i < legacySlots.Length; i++)
            {
                var slot = legacySlots[i];
                if (slot == EquipmentSlotType.None || string.IsNullOrEmpty(legacy.equipped[i]))
                    continue;

                data.equipment.Add(new PlayerSaveData.SlotEntry(slot.ToString(), legacy.equipped[i]));
            }
        }

        // 旧格式的 carry 是「出征携带」，语义上属于局内，迁移时并入仓库避免丢失。
        if (legacy.carry != null)
        {
            var nextIndex = 0;
            foreach (var id in legacy.carry)
            {
                if (string.IsNullOrEmpty(id))
                    continue;

                while (nextIndex < PlayerLoadout.WarehouseCapacity
                       && data.warehouse.Exists(e => e.key == nextIndex.ToString()))
                {
                    nextIndex++;
                }

                if (nextIndex >= PlayerLoadout.WarehouseCapacity)
                    break;

                data.warehouse.Add(new PlayerSaveData.SlotEntry(nextIndex.ToString(), id));
                nextIndex++;
            }
        }

        return data;
    }

    /// <summary>
    /// 把一条存档条目还原成物品实例。
    /// 旧存档没有 instanceId / durability，此时现场生成新 ID、耐久取满值。
    /// </summary>
    private static ItemInstance Resolve(PlayerSaveData.SlotEntry entry, ItemDatabase database, string location)
    {
        if (entry == null || string.IsNullOrEmpty(entry.itemId))
            return null;

        var definition = database != null ? database.Find(entry.itemId) : null;
        if (definition == null)
        {
            Debug.LogError($"存档中的物品 Id「{entry.itemId}」无法在 ItemDatabase 中解析，{location} 将保持为空。");
            return null;
        }

        var instance = new ItemInstance(entry.instanceId, definition);

        // durability < 0 表示旧存档未记录（或非防具），保持满耐久。
        if (entry.durability >= 0)
            instance.RestoreDurability(entry.durability);

        return instance;
    }

    private static string Truncate(string value)
    {
        const int limit = 200;
        return value.Length <= limit ? value : value.Substring(0, limit) + "…";
    }

    /// <summary>旧版存档结构，仅用于迁移读取。</summary>
    [Serializable]
    private sealed class LegacySave
    {
        public int version;
        public string[] bag;
        public string[] carry;
        public string[] equipped;
    }
}
