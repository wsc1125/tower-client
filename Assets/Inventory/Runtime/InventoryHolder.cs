using UnityEngine;

/// <summary>
/// 场景中唯一的玩家库存宿主：惰性创建 <see cref="Inventory"/>、首次建档，并在变更后写入存档。
/// 主菜单仓库与战斗背包共用同一个宿主，即可共享同一份库存数据。
/// </summary>
public sealed class InventoryHolder : MonoBehaviour
{
    [Header("存档")]
    [Tooltip("开启后从 PlayerPrefs 读取库存，并在每次变更后写入存档。")]
    [SerializeField] private bool loadFromSave;

    [Tooltip("存档还原物品所需的登记表，留空则无法还原已保存的物品。")]
    [SerializeField] private ItemDatabase database;

    [Tooltip("仅在首次建档（没有存档）时写入背包的初始物品。")]
    [SerializeField] private ItemDefinition[] initialItems;

    [Tooltip("仅在首次建档时放入装备槽的初始物品。")]
    [SerializeField] private ItemDefinition[] initialEquippedItems;

    private Inventory model;

    public Inventory Model => model ??= CreateModel();

    /// <summary>本轮是否新建了库存（即此前没有存档）。用于只对首次建档的流程补充初始装备。</summary>
    public bool CreatedFresh { get; private set; }

    public ItemDatabase Database => database;

    private void OnDestroy()
    {
        if (model == null)
            return;

        model.Changed -= OnModelChanged;

        if (loadFromSave)
            PlayerInventoryStore.Flush();
    }

    private void OnApplicationPause(bool pause)
    {
        if (pause && loadFromSave)
            PlayerInventoryStore.Flush();
    }

    private void OnApplicationQuit()
    {
        if (loadFromSave)
            PlayerInventoryStore.Flush();
    }

    private Inventory CreateModel()
    {
        var created = new Inventory();

        if (!loadFromSave)
        {
            CreatedFresh = true;
            return created;
        }

        if (database == null)
            Debug.LogError("InventoryHolder 未指定 ItemDatabase，已保存的物品将无法还原。", this);

        if (PlayerInventoryStore.HasSave)
        {
            CreatedFresh = false;
            PlayerInventoryStore.Apply(PlayerInventoryStore.Load(), created, database);
        }
        else
        {
            CreatedFresh = true;
            AddInitialItems(created);
            EquipInitialItems(created);
            PlayerInventoryStore.Save(created, database);
        }

        created.Changed += OnModelChanged;
        return created;
    }

    private void AddInitialItems(Inventory target)
    {
        if (initialItems == null)
            return;

        foreach (var item in initialItems)
        {
            if (item == null)
                continue;

            if (!target.TryAdd(item))
                Debug.LogWarning($"首次建档时背包已满，初始物品「{item.name}」未能放入。", this);
        }
    }

    private void EquipInitialItems(Inventory target)
    {
        if (initialEquippedItems == null)
            return;

        foreach (var item in initialEquippedItems)
        {
            if (item == null)
                continue;

            if (!Inventory.IsSlotUsable(item.EquipSlotType))
            {
                Debug.LogWarning($"初始装备「{item.name}」的目标槽 {item.EquipSlotType} 当前不可用，已跳过。", this);
                continue;
            }

            target.RestoreEquippedItem(item.EquipSlotType, item);
        }
    }

    private void OnModelChanged()
    {
        PlayerInventoryStore.Save(model, database);
    }
}
