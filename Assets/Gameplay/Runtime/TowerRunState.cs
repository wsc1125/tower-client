/// <summary>
/// 一局爬塔的局内状态：当前层数与是否已结束。
/// 纯 C# 类，不继承 MonoBehaviour，便于在 EditMode 直接验证。
/// 战利品清单不在这里 —— 它属于局内携带（<see cref="RaidBag"/>），因为「拾取」与「带出」是同一份数据。
/// </summary>
public sealed class TowerRunState
{
    /// <summary>当前层数，从 1 开始。</summary>
    public int CurrentFloor { get; private set; } = 1;

    /// <summary>本局是否已结束（撤退或死亡）。结束后传送点与死亡回调都不再动作。</summary>
    public bool IsFinished { get; private set; }

    /// <summary>开始新的一局：层数归 1，清空结束标记。</summary>
    public void BeginRun()
    {
        CurrentFloor = 1;
        IsFinished = false;
    }

    /// <summary>进入下一层。</summary>
    public void AdvanceFloor()
    {
        CurrentFloor++;
    }

    /// <summary>标记本局结束。幂等。</summary>
    public void Finish()
    {
        IsFinished = true;
    }
}
