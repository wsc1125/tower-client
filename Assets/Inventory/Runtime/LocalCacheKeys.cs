/// <summary>
/// PlayerPrefs 缓存键的统一命名规则：与 <see cref="PlayerSaveStore"/> 一致 ——
/// 未登录（null / <see cref="GameSession.LocalPlayerId"/>）用全局键，
/// 登录后用「&lt;baseKey&gt;:account:&lt;id&gt;」做账号隔离。
///
/// 仓库/装备、收藏、星空币、同步 pending 标记四类缓存都走这里，
/// 保证同一规则只有一处定义。
/// </summary>
public static class LocalCacheKeys
{
    public static string For(string baseKey, string playerId = null) =>
        string.IsNullOrEmpty(playerId) || playerId == GameSession.LocalPlayerId
            ? baseKey : baseKey + ":account:" + playerId;
}
