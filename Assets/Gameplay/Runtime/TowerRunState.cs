using System.Collections.Generic;

/// <summary>
/// 一局爬塔的局内状态：当前层数、本局拾取的战利品清单、是否已结束。
/// 纯 C# 类，不继承 MonoBehaviour，便于在 EditMode 直接验证。
/// 只活在本次战斗场景内，退出即丢（本轮不做持久化）。
/// </summary>
public sealed class TowerRunState
{
    private readonly List<ItemDefinition> collectedLoot = new List<ItemDefinition>();

    /// <summary>当前层数，从 1 开始。</summary>
    public int CurrentFloor { get; private set; } = 1;

    /// <summary>本局是否已结束（撤退或死亡）。结束后传送点与死亡回调都不再动作。</summary>
    public bool IsFinished { get; private set; }

    /// <summary>本局拾取的全部战利品，按拾取顺序。实际物品在 Inventory.carry 里，这里只用于结算展示。</summary>
    public IReadOnlyList<ItemDefinition> CollectedLoot => collectedLoot;

    /// <summary>开始新的一局：层数归 1，清空战利品与结束标记。</summary>
    public void BeginRun()
    {
        CurrentFloor = 1;
        IsFinished = false;
        collectedLoot.Clear();
    }

    /// <summary>进入下一层。</summary>
    public void AdvanceFloor()
    {
        CurrentFloor++;
    }

    /// <summary>记一件本局战利品。null 被忽略。</summary>
    public void RecordLoot(ItemDefinition item)
    {
        if (item == null)
            return;

        collectedLoot.Add(item);
    }

    /// <summary>清空本局战利品清单（死亡时调用）。</summary>
    public void ClearLoot()
    {
        collectedLoot.Clear();
    }

    /// <summary>标记本局结束。幂等。</summary>
    public void Finish()
    {
        IsFinished = true;
    }
}
