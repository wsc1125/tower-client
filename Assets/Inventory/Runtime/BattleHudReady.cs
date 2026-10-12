using System;

/// <summary>
/// 战斗 HUD 就绪信号：HUD 实例化完成后广播一次，供需要引用 HUD 组件的对象订阅。
///
/// 为什么需要：联机玩家对象由 NGO 在**主菜单场景**生成（StartHost 那一刻），
/// 而攻击按钮 / 切换按钮在**战斗场景**的 HUDCanvas 里。玩家对象的 OnNetworkSpawn
/// 只执行一次，那时 HUD 还不存在，FindObjectOfType 必然返回 null，
/// 导致 attackButton 永久为空 —— 表现为「联机下无法攻击、无法瞄准」。
///
/// 因此把「HUD 何时可用」从「猜时机」改为「HUD 主动通知」：
/// HUD 就绪后广播 <see cref="Ready"/>，玩家对象订阅后补接引用，跨场景天然正确。
///
/// 放在 Inventory 程序集（最底层、无依赖）：BattleHUD 与 Gameplay 都能引用它。
/// </summary>
public static class BattleHudReady
{
    /// <summary>HUD 是否已就绪。场景卸载时由通知组件重置为 false。</summary>
    public static bool IsReady { get; private set; }

    /// <summary>HUD 就绪时触发一次。已就绪后再订阅不会补发，订阅方需先查 <see cref="IsReady"/>。</summary>
    public static event Action Ready;

    /// <summary>由 HUD 侧的通知组件在 Start 时调用。</summary>
    public static void NotifyReady()
    {
        IsReady = true;
        Ready?.Invoke();
    }

    /// <summary>由 HUD 侧的通知组件在销毁时调用，避免跨场景残留「已就绪」状态。</summary>
    public static void Reset()
    {
        IsReady = false;
    }
}
