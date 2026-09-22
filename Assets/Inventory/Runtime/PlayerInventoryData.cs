using System;

/// <summary>
/// 玩家库存的持久化载体：只存物品 ID，读取时再经 <see cref="ItemDatabase"/> 还原。
/// </summary>
[Serializable]
public sealed class PlayerInventoryData
{
    public const int EquippedSlotCount = 6;

    public int version = 1;
    public string[] bag;
    public string[] carry;
    public string[] equipped;

    public static PlayerInventoryData CreateEmpty()
    {
        return new PlayerInventoryData
        {
            bag = new string[Inventory.BagCapacity],
            carry = new string[Inventory.CarryCapacity],
            equipped = new string[EquippedSlotCount],
        };
    }

    /// <summary>把 null 或长度不正确的数组补齐到当前版本的长度；不抛异常。</summary>
    public void Validate()
    {
        bag = Resize(bag, Inventory.BagCapacity);
        carry = Resize(carry, Inventory.CarryCapacity);
        equipped = Resize(equipped, EquippedSlotCount);
    }

    private static string[] Resize(string[] source, int length)
    {
        if (source != null && source.Length == length)
            return source;

        var result = new string[length];
        if (source != null)
            Array.Copy(source, result, Math.Min(source.Length, length));
        return result;
    }
}
