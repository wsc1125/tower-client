using System;
using UnityEngine;

/// <summary>
/// 玩家库存存档：把 <see cref="Inventory"/> 转成只含物品 ID 的数据并写入 PlayerPrefs，
/// 读取时再经 <see cref="ItemDatabase"/> 还原。主菜单仓库与战斗背包都从这里取同一份库存。
/// </summary>
public static class PlayerInventoryStore
{
    public const string PrefsKey = "PlayerInventory";

    private const int CurrentVersion = 2;

    public static bool HasSave => PlayerPrefs.HasKey(PrefsKey);

    /// <summary>读取存档。存档缺失或解析失败时返回空数据，并保留诊断信息。</summary>
    public static PlayerInventoryData Load()
    {
        if (!HasSave)
            return PlayerInventoryData.CreateEmpty();

        var json = PlayerPrefs.GetString(PrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
            return PlayerInventoryData.CreateEmpty();

        try
        {
            var data = JsonUtility.FromJson<PlayerInventoryData>(json);
            if (data == null)
            {
                Debug.LogError($"玩家库存存档为空，已改用空库存。原始内容：{Truncate(json)}");
                return PlayerInventoryData.CreateEmpty();
            }

            data.Validate();
            if (data.version > CurrentVersion)
                Debug.LogWarning($"玩家库存存档版本 {data.version} 高于当前支持的 {CurrentVersion}，只读取可识别的字段。");

            return data;
        }
        catch (Exception exception)
        {
            Debug.LogError($"玩家库存存档解析失败（{exception.Message}），已改用空库存。原始内容：{Truncate(json)}");
            return PlayerInventoryData.CreateEmpty();
        }
    }

    /// <summary>把当前库存转成存档数据；无法在登记表中解析的物品会被记录，但仍以 ID 形式保留。</summary>
    public static PlayerInventoryData Capture(Inventory model, ItemDatabase database)
    {
        var data = PlayerInventoryData.CreateEmpty();
        data.version = CurrentVersion;

        if (model == null)
            return data;

        for (var i = 0; i < Inventory.BagCapacity; i++)
        {
            var item = model.GetBagItem(i);
            if (item == null)
                continue;

            data.bag[i] = item.Id;
            WarnIfNotRegistered(item, database);
        }

        for (var i = 0; i < Inventory.CarryCapacity; i++)
        {
            var item = model.GetCarryItem(i);
            if (item == null)
                continue;

            data.carry[i] = item.Id;
            WarnIfNotRegistered(item, database);
        }

        for (var i = 0; i < PlayerInventoryData.EquippedSlotCount; i++)
        {
            var item = model.GetEquippedItem((EquipmentSlotType)i);
            if (item == null)
                continue;

            data.equipped[i] = item.Id;
            WarnIfNotRegistered(item, database);
        }

        return data;
    }

    /// <summary>把存档数据写回库存。返回是否成功应用（数据为空也算失败）。</summary>
    public static bool Apply(PlayerInventoryData data, Inventory model, ItemDatabase database)
    {
        if (model == null)
            return false;

        model.ClearAll();

        if (data == null)
        {
            model.NotifyChanged();
            return false;
        }

        data.Validate();

        for (var i = 0; i < Inventory.BagCapacity; i++)
        {
            var id = data.bag[i];
            var item = Resolve(id, database, $"背包第 {i} 格");
            model.RestoreBagItem(i, item);
        }

        for (var i = 0; i < Inventory.CarryCapacity; i++)
        {
            var id = data.carry[i];
            model.RestoreCarryItem(i, Resolve(id, database, $"携带第 {i} 格"));
        }

        for (var i = 0; i < PlayerInventoryData.EquippedSlotCount; i++)
        {
            var id = data.equipped[i];
            if (string.IsNullOrEmpty(id))
                continue;

            var slot = (EquipmentSlotType)i;
            if (!Inventory.IsSlotUsable(slot))
            {
                Debug.LogError($"存档中的装备槽「{slot}」当前不可用，已忽略该件物品（Id「{id}」）。");
                continue;
            }

            model.RestoreEquippedItem(slot, Resolve(id, database, $"装备槽 {slot}"));
        }

        model.NotifyChanged();
        return true;
    }

    /// <summary>写入存档。只调用 PlayerPrefs.SetString，真正落盘由 <see cref="Flush"/> 完成。</summary>
    public static void Save(Inventory model, ItemDatabase database)
    {
        var data = Capture(model, database);
        PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(data));
    }

    /// <summary>立即落盘。切后台、退出或切换场景时调用。</summary>
    public static void Flush()
    {
        PlayerPrefs.Save();
    }

    /// <summary>删除存档，主要用于测试与「重新开始」。</summary>
    public static void Delete()
    {
        PlayerPrefs.DeleteKey(PrefsKey);
        PlayerPrefs.Save();
    }

    private static ItemDefinition Resolve(string id, ItemDatabase database, string location)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        var item = database != null ? database.Find(id) : null;
        if (item == null)
            Debug.LogError($"存档中的物品 Id「{id}」无法在 ItemDatabase 中解析，{location} 将保持为空。");

        return item;
    }

    private static void WarnIfNotRegistered(ItemDefinition item, ItemDatabase database)
    {
        if (database == null || database.Find(item.Id) != null)
            return;

        Debug.LogWarning($"物品「{item.name}」未登记到 ItemDatabase，重新进入游戏后可能无法还原。", item);
    }

    private static string Truncate(string value)
    {
        const int limit = 200;
        return value.Length <= limit ? value : value.Substring(0, limit) + "…";
    }
}
