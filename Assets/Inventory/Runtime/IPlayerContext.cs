/// <summary>
/// 一个玩家的局外资产与当前局内数据上下文。
/// 纯数据层接口，不依赖场景对象或网络库。
/// </summary>
public interface IPlayerContext
{
    /// <summary>稳定的玩家标识。单机入口使用 local。</summary>
    string PlayerId { get; }

    /// <summary>该玩家的局外资产：仓库与装备槽。</summary>
    PlayerLoadout Loadout { get; }

    /// <summary>该玩家当前局内携带与战利品。</summary>
    RaidBag Raid { get; }
}
