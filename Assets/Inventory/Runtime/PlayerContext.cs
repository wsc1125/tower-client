/// <summary>
/// 默认的进程内玩家上下文实现。
/// 网络接入前由 GameSession 按 PlayerId 创建；网络层接入后可替换为带连接状态的实现，
/// 而不影响 Inventory 的数据容器。
/// </summary>
public sealed class PlayerContext : IPlayerContext
{
    public PlayerContext(string playerId)
    {
        PlayerId = playerId;
        Loadout = new PlayerLoadout();
        Raid = new RaidBag();
    }

    public string PlayerId { get; }
    public PlayerLoadout Loadout { get; }
    public RaidBag Raid { get; }
}
