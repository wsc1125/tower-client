/// <summary>
/// 传送点的种类。
///
/// 放在 Inventory 程序集是因为它要同时被 Gameplay（<c>FloorPortal2D</c> / <c>TowerRunManager</c>）
/// 与 BattleHUD（<c>PortalConfirmPanelController</c>）引用，而 BattleHUD 不依赖 Gameplay。
/// </summary>
public enum PortalKind
{
    /// <summary>进入下一层。</summary>
    Next,

    /// <summary>撤退结算。</summary>
    Retreat,
}
