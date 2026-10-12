using UnityEngine;

/// <summary>
/// 战斗 HUD 就绪通知：挂在 HUDCanvas 上，在 Start 时广播 <see cref="BattleHudReady.NotifyReady"/>。
///
/// 为什么用 Start 而不是 Awake：Start 在所有对象的 Awake 之后执行，
/// 此时 HUD 内的按钮等组件已全部初始化完毕，订阅方补接引用时能拿到有效对象。
///
/// 场景卸载时重置就绪标志，避免下次进入战斗时残留「已就绪」导致订阅方误判。
/// </summary>
[DisallowMultipleComponent]
public sealed class BattleHudReadyNotifier : MonoBehaviour
{
    private void Start()
    {
        BattleHudReady.NotifyReady();
    }

    private void OnDestroy()
    {
        BattleHudReady.Reset();
    }
}
