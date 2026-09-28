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
    /// <summary>本机玩家是否被本地冻结。联机下由弹窗置位，单机下恒为 false。</summary>
    public static bool IsFrozen { get; private set; }

    /// <summary>冻结本地玩家。幂等，重复调用无副作用。</summary>
    public static void Freeze()
    {
        IsFrozen = true;
    }

    /// <summary>解除本地冻结。幂等。</summary>
    public static void Unfreeze()
    {
        IsFrozen = false;
    }
}
