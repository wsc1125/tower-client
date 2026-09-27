using System;
using UnityEngine;

/// <summary>
/// 跨场景的玩家会话：持有局外资产（<see cref="PlayerLoadout"/>）与当前局内携带（<see cref="RaidBag"/>）。
///
/// 旧实现让主菜单与战斗场景各挂一个 <see cref="InventoryHolder"/>，各自从 PlayerPrefs 读一份内存副本，
/// 靠「切场景前 Flush」来同步 —— 任何一次漏 Flush 就表现为「装备带不进去」。
/// 这里改为进程内单例：两个场景拿到的是同一个对象，跨场景天然一致，PlayerPrefs 只做持久化备份。
/// </summary>
public static class GameSession
{
    private static PlayerLoadout loadout;
    private static RaidBag raidBag;
    private static ItemDatabase database;
    private static bool initialized;

    /// <summary>局外资产：仓库 + 装备槽。跨场景常驻。</summary>
    public static PlayerLoadout Loadout => loadout ??= CreateLoadout();

    /// <summary>当前局内携带。未出征时为空包。</summary>
    public static RaidBag Raid => raidBag ??= CreateRaidBag();

    /// <summary>是否已经初始化过（用于判断是否需要注入初始装备）。</summary>
    public static bool Initialized => initialized;

    /// <summary>局外数据发生变化时触发，供存档与 UI 订阅。</summary>
    public static event Action LoadoutChanged;

    /// <summary>局内数据发生变化时触发。</summary>
    public static event Action RaidChanged;

    /// <summary>
    /// 初始化会话：注入物品登记表，从存档还原局外数据；没有存档时写入初始装备。
    /// 重复调用是安全的（幂等），场景切换时两个场景都会调一次。
    /// </summary>
    public static void EnsureInitialized(ItemDatabase itemDatabase, ItemDefinition[] initialEquipment = null)
    {
        if (initialized)
        {
            // 后进入的场景可能带来更完整的登记表，补上即可。
            if (database == null)
                database = itemDatabase;
            return;
        }

        initialized = true;
        database = itemDatabase;

        if (itemDatabase == null)
            Debug.LogError("[GameSession] 未指定 ItemDatabase，已保存的物品将无法还原。");

        // 先确保两个容器已创建并接好事件，再决定是否从存档还原。
        var target = Loadout;
        var bag = Raid;

        if (PlayerSaveStore.HasSave)
        {
            PlayerSaveStore.Apply(PlayerSaveStore.Load(), target, itemDatabase);
        }
        else
        {
            ApplyInitialEquipment(initialEquipment);
            PlayerSaveStore.Save(target);
            PlayerSaveStore.Flush();
        }

        bag.NotifyChanged();
    }

    /// <summary>
    /// 创建局外容器并接上变更通知。事件在创建时就接好，
    /// 这样即使调用方没走 <see cref="EnsureInitialized"/>，<see cref="LoadoutChanged"/> 也照常触发。
    /// </summary>
    private static PlayerLoadout CreateLoadout()
    {
        var created = new PlayerLoadout();
        created.Changed += HandleLoadoutChanged;
        return created;
    }

    private static RaidBag CreateRaidBag()
    {
        var created = new RaidBag();
        created.Changed += HandleRaidChanged;
        return created;
    }

    /// <summary>
    /// 出征：清空上一局的携带与战利品。
    /// 装备槽不需要搬运 —— 局内外共用同一份 <see cref="PlayerLoadout.Equipment"/>。
    /// </summary>
    public static void BeginRaid()
    {
        LoadoutService.DeployToRaid(Loadout, Raid);
    }

    /// <summary>
    /// 撤离成功：局内携带并入局外仓库，然后落盘。
    /// 返回仓库放不下的物品数量（0 表示全部带出）。
    /// </summary>
    public static int ExtractRaid()
    {
        // 收藏结算：只算本局拾取的战利品，不含带入地牢的初始装备。
        CollectionService.RecordRaidLoot(Raid.Loot);

        var overflow = LoadoutService.ExtractToLoadout(Raid, Loadout);
        PlayerSaveStore.Save(Loadout);
        PlayerSaveStore.Flush();
        return overflow;
    }

    /// <summary>
    /// 死亡：清空局内携带与战利品，**并清空装备槽**。
    ///
    /// 装备槽与局外是同一份，因此「穿在身上的装备」也一并丢失 ——
    /// 这是「死亡丢光随身物品」规则的唯一实现处。仓库不受影响。
    /// </summary>
    public static void AbandonRaid()
    {
        Raid.ClearAll();
        Loadout.Equipment.Clear();
        Raid.NotifyChanged();
        Loadout.NotifyChanged();
    }

    /// <summary>把当前局外数据落盘。</summary>
    public static void Persist()
    {
        PlayerSaveStore.Save(Loadout);
        PlayerSaveStore.Flush();
    }

    /// <summary>清空全部数据并删除存档。仅用于「重新开始」与测试。</summary>
    public static void ResetAll()
    {
        // 先摘掉存档订阅再清空，否则 ClearAll 后的通知会把空数据重新写回存档。
        var target = Loadout;
        target.Changed -= HandleLoadoutChanged;

        target.ClearAll();
        Raid.ClearAll();
        PlayerSaveStore.Delete();

        target.Changed += HandleLoadoutChanged;

        LoadoutChanged?.Invoke();
        RaidChanged?.Invoke();
    }

    /// <summary>测试用：把会话恢复到未初始化状态。</summary>
    public static void ResetForTests()
    {
        // 容器在创建时已接上事件，这里只需丢弃引用即可（旧对象随之被回收）。
        loadout = null;
        raidBag = null;
        database = null;
        initialized = false;
        LoadoutChanged = null;
        RaidChanged = null;
    }

    private static void ApplyInitialEquipment(ItemDefinition[] initialEquipment)
    {
        if (initialEquipment == null)
            return;

        foreach (var item in initialEquipment)
        {
            if (item == null)
                continue;

            if (!EquipmentSlots.IsUsable(item.EquipSlotType))
            {
                Debug.LogWarning($"[GameSession] 初始装备「{item.name}」的目标槽 {item.EquipSlotType} 不可用，已跳过。");
                continue;
            }

            loadout.Equipment.Set(item.EquipSlotType, ItemInstance.Create(item));
        }
    }

    private static void HandleLoadoutChanged()
    {
        PlayerSaveStore.Save(loadout);
        PlayerSaveStore.Flush();
        LoadoutChanged?.Invoke();
    }

    private static void HandleRaidChanged()
    {
        RaidChanged?.Invoke();
    }
}
