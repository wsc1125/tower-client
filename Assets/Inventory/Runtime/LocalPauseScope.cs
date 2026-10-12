using UnityEngine;

/// <summary>
/// 本地冻结：弹窗期间只停住「本机玩家」的输入与表现，不冻结世界。
///
/// 单机下弹窗（死亡 / 传送确认）原本用 <c>Time.timeScale = 0</c> 暂停整个世界，
/// 这在联机下会连其他玩家一起冻住 —— 房主弹个窗，全队都动不了。
/// 因此把「暂停」拆成两种语义：
/// ① 单机：仍然用 <c>Time.timeScale = 0</c>，行为与改造前完全一致；
/// ② 联机：只冻结本地玩家（输入 + 表现），世界照常推进。
///
/// 本类只负责「冻结本地玩家」这一件事，不决定何时冻结 —— 那是调用方（死亡流程 / 传送确认）的事。
///
/// 放在 Inventory 程序集（最底层、无依赖）而不是 Gameplay：
/// BattleHUD 需要读它（受击红边、拾取提示、甲修蓄力），而 BattleHUD 不依赖 Gameplay。
/// </summary>
public static class LocalPauseScope
{
    /// <summary>
    /// 本机玩家是否被本地冻结（输入 + 本机表现）。联机下由弹窗 / 死亡置位，单机下恒为 false。
    ///
    /// 只应被「本机玩家自己的组件」读取（移动输入、开火、本机角色动画、受击红边、背包面板）。
    /// 世界 / 主机权威逻辑（敌人 AI、相机、弹幕、传送点等）**不得**读它 ——
    /// 否则联机时房主一死，主机上的敌人 AI 与相机全停，客户端收不到同步，表现为「全队画面冻结」。
    /// 那类组件应改读 <see cref="IsWorldFrozen"/>。
    /// </summary>
    private static bool locallyFrozen;
    private static bool spectateFrozen;

    public static bool IsFrozen => locallyFrozen || spectateFrozen;

    /// <summary>
    /// 世界是否被冻结。仅单机暂停（<c>Time.timeScale = 0</c>）时为 true；
    /// 联机下恒为 false —— 联机世界永不停，只冻结本机玩家。
    ///
    /// 供敌人 AI / 相机 / 弹幕 / 传送点等「世界逻辑」读取：
    /// 单机暂停时它们应停住（与改造前一致），联机时它们必须照常推进。
    /// </summary>
    public static bool IsWorldFrozen { get; private set; }

    /// <summary>冻结本地玩家。幂等，重复调用无副作用。</summary>
    public static void Freeze()
    {
        locallyFrozen = true;
    }

    /// <summary>解除普通本地冻结，不影响死亡观战持有的冻结。</summary>
    public static void Unfreeze()
    {
        locallyFrozen = false;
    }

    /// <summary>设置死亡观战冻结，与弹窗等普通冻结相互独立。</summary>
    public static void SetSpectating(bool value)
    {
        spectateFrozen = value;
    }

    /// <summary>冻结世界（仅单机暂停使用）。</summary>
    public static void FreezeWorld()
    {
        IsWorldFrozen = true;
    }

    /// <summary>解除世界冻结。</summary>
    public static void UnfreezeWorld()
    {
        IsWorldFrozen = false;
    }
}
