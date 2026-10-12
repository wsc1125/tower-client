using System;
using UnityEngine;

namespace Tower.Netcode
{
    /// <summary>
    /// 「出发」到联机启动的桥接层。
    ///
    /// 存在的理由：UI 层（Assembly-CSharp）的 <c>DepartFlow</c> 无法引用 Gameplay 程序集的
    /// <see cref="MultiplayerCoordinator"/>，否则会把 UI 与 Gameplay 绑死。
    /// 这里用静态事件做单向桥接：UI 层只负责「喊一声要出发了」，
    /// 由 Gameplay 侧的 <see cref="MultiplayerCoordinator"/> 订阅并真正启动 NGO。
    ///
    /// 与 <c>NetworkSession.SignalForwarded</c> 是同一套分层思路：
    /// 跨程序集通信只走事件，不互相引用。
    /// </summary>
    public static class MultiplayerDepartBridge
    {
        /// <summary>
        /// 请求启动联机出发流程。参数为 true 表示本端是房主（启动 Host），
        /// false 表示成员（启动 Client）。
        ///
        /// 由 UI 层在联机模式下点「出发」时调用；无订阅者时静默忽略
        /// （例如单机场景里没有挂 MultiplayerCoordinator）。
        /// </summary>
        public static event Action<bool> DepartRequested;

        /// <summary>
        /// 联机出发失败。参数为可展示的原因。
        ///
        /// UI 层据此恢复出发按钮、隐藏加载页，让玩家可以重试 ——
        /// 否则失败后按钮永久禁用，玩家只能重启。
        /// </summary>
        public static event Action<string> DepartFailed;

        /// <summary>触发出发请求。UI 层调用。</summary>
        public static void RequestDepart(bool isHost)
        {
            DepartRequested?.Invoke(isHost);
        }

        /// <summary>通知出发失败。Gameplay 侧调用。</summary>
        public static void NotifyDepartFailed(string reason)
        {
            DepartFailed?.Invoke(reason);
        }
    }
}
