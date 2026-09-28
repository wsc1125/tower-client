using System;
using System.Collections.Generic;

/// <summary>
/// 局内携带：出征时从局外带进来的物品 + 局内拾取的战利品。
/// 这是「会丢的东西」—— 死亡时整包清空，撤离成功时内容并入仓库。
///
/// 注意：装备槽**不在这里**。装备是玩家资产，局内外共用同一份
/// <see cref="PlayerLoadout.Equipment"/>，因此局内磨损会直接反映到局外那件装备上，
/// 死亡时也一并丢失 —— 与「穿在身上出征」的语义一致。
/// </summary>
public sealed class RaidBag
{
    /// <summary>携带格数。</summary>
    public const int CarryCapacity = 20;

    private readonly ItemInstance[] carry = new ItemInstance[CarryCapacity];

    /// <summary>本局拾取的战利品，按拾取顺序。仅用于结算展示，实际物品在 <see cref="carry"/> 里。</summary>
    private readonly List<ItemInstance> loot = new List<ItemInstance>();

    /// <summary>携带发生变化时触发。</summary>
    public event Action Changed;

    public IReadOnlyList<ItemInstance> Loot => loot;

    public ItemInstance GetCarry(int index)
    {
        return IsCarryIndex(index) ? carry[index] : null;
    }

    public void SetCarry(int index, ItemInstance item)
    {
        if (IsCarryIndex(index))
            carry[index] = item;
    }

    /// <summary>携带已用格数。</summary>
    public int CarryCount()
    {
        var count = 0;
        for (var i = 0; i < carry.Length; i++)
        {
            if (carry[i] != null)
                count++;
        }

        return count;
    }

    /// <summary>把物品放进携带第一个空格。携带满时返回 false（局内拾取失败即走这里）。</summary>
    public bool TryAddToCarry(ItemInstance item)
    {
        if (item == null)
            return false;

        for (var i = 0; i < carry.Length; i++)
        {
            if (carry[i] != null)
                continue;

            carry[i] = item;
            NotifyChanged();
            return true;
        }

        return false;
    }

    /// <summary>移除携带某一格的物品（丢弃）。</summary>
    public bool TryRemoveFromCarry(int index)
    {
        if (!IsCarryIndex(index) || carry[index] == null)
            return false;

        carry[index] = null;
        NotifyChanged();
        return true;
    }

    /// <summary>交换携带内两格。越界或两格都为空时失败。</summary>
    public bool TrySwapInCarry(int first, int second)
    {
        if (!IsCarryIndex(first) || !IsCarryIndex(second))
            return false;

        if (carry[first] == null && carry[second] == null)
            return false;

        var temp = carry[first];
        carry[first] = carry[second];
        carry[second] = temp;
        NotifyChanged();
        return true;
    }

    /// <summary>记一件本局战利品。null 被忽略。</summary>
    public void RecordLoot(ItemInstance item)
    {
        if (item != null)
            loot.Add(item);
    }

    /// <summary>清空本局战利品清单（死亡时调用）。</summary>
    public void ClearLoot() => loot.Clear();

    /// <summary>清空携带与战利品清单。死亡时调用。装备槽不归这里管。</summary>
    public void ClearAll()
    {
        for (var i = 0; i < carry.Length; i++)
            carry[i] = null;

        loot.Clear();
    }

    /// <summary>手动触发变更通知，供批量还原结束后统一通知。</summary>
    public void NotifyChanged() => Changed?.Invoke();

    public static bool IsCarryIndex(int index) => index >= 0 && index < CarryCapacity;
}
