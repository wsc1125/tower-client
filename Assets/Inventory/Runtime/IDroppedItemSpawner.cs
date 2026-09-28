/// <summary>
/// 把一件物品实例「放回地面」的能力。由 Gameplay 侧的掉落物生成器实现。
///
/// 抽成接口是为了让 <see cref="BagPanelController"/>（BattleHUD 程序集）能丢弃物品到地面，
/// 而不必反向依赖 Gameplay 的 <c>DroppedItem2D</c> —— 与 <see cref="IConsumableTarget"/> 同一套做法。
/// </summary>
public interface IDroppedItemSpawner
{
    /// <summary>
    /// 在指定世界坐标生成一件地面掉落物，物品实例（含耐久）原样带走，可被再次拾取。
    /// 生成失败（未接线 / 实例为空）时返回 false。
    /// </summary>
    bool Spawn(ItemInstance item, UnityEngine.Vector2 position);
}
