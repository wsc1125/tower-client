using UnityEngine;

/// <summary>
/// 地面掉落物的生成器：实现 <see cref="IDroppedItemSpawner"/>，供局内背包「丢弃」调用。
///
/// 与 <see cref="LootDropper"/> 的区别：LootDropper 挂在敌人身上、按掉落池掷点；
/// 本组件挂在场景里（玩家或管理器上），只负责把「玩家主动丢出的那一件」放回地面。
/// 两者共用同一个 <c>DroppedItem.prefab</c>，因此丢出的东西和敌人掉的看起来完全一样。
/// </summary>
public sealed class DroppedItemSpawner : MonoBehaviour, IDroppedItemSpawner
{
    [Tooltip("地面掉落物 prefab。留空则自动从场景里找一份 DroppedItem2D 作为模板。")]
    [SerializeField] private DroppedItem2D dropPrefab;

    /// <summary>当前使用的掉落物模板；未接线时为 null。</summary>
    public DroppedItem2D DropPrefab => dropPrefab;

    /// <summary>
    /// 在指定位置生成一件掉落物。物品实例原样带走（耐久不重置），因此丢出再捡回状态不变。
    /// </summary>
    public bool Spawn(ItemInstance item, Vector2 position)
    {
        if (item == null)
            return false;

        var prefab = ResolvePrefab();
        if (prefab == null)
            return false;

        var dropped = Instantiate(prefab, position, Quaternion.identity);
        dropped.Initialize(item);
        return true;
    }

    /// <summary>未接线时从场景里找一份现成的掉落物当模板，避免每个场景都要手动拖引用。</summary>
    private DroppedItem2D ResolvePrefab()
    {
        if (dropPrefab != null)
            return dropPrefab;

        var existing = FindObjectOfType<DroppedItem2D>();
        if (existing != null)
            dropPrefab = existing;

        return dropPrefab;
    }
}
