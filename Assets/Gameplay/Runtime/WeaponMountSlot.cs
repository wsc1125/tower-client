using UnityEngine;

/// <summary>
/// 角色的一只手上的武器挂点。只负责「把当前装备且被选中的武器显示出来」——
/// 未被选中的那只手会关掉渲染，攻击逻辑读的 <see cref="HasWeapon"/> 也随之变假，
/// 于是「同一时间只有一把武器显示、也只有它能攻击」由这一个开关统一保证。
/// </summary>
public sealed class WeaponMountSlot : MonoBehaviour
{
    [SerializeField] private SpriteRenderer weaponRenderer;
    [SerializeField] private InventoryHolder holder;
    [SerializeField] private PlayerWeaponSelector selector;
    [SerializeField] private EquipmentSlotType mountedSlot = EquipmentSlotType.MeleeWeapon;

    private Inventory subscribedInventory;
    private PlayerWeaponSelector subscribedSelector;

    /// <summary>手上当前可见、可攻击的武器；被切走或未装备时为 false。</summary>
    public bool HasWeapon => weaponRenderer != null
                          && weaponRenderer.enabled
                          && weaponRenderer.sprite != null;

    /// <summary>该槽位是否是当前使用的武器。没接 selector 时视为始终选中（单武器场景）。</summary>
    public bool IsSelected => selector == null || selector.IsActive(mountedSlot);

    public SpriteRenderer WeaponRenderer => weaponRenderer;

    private void OnEnable()
    {
        Subscribe();
        Refresh();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribedInventory == null && holder != null)
        {
            subscribedInventory = holder.Model;
            subscribedInventory.Changed += Refresh;
        }

        if (subscribedSelector == null && selector != null)
        {
            subscribedSelector = selector;
            subscribedSelector.Changed += Refresh;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedInventory != null)
        {
            subscribedInventory.Changed -= Refresh;
            subscribedInventory = null;
        }

        if (subscribedSelector != null)
        {
            subscribedSelector.Changed -= Refresh;
            subscribedSelector = null;
        }
    }

    private void Refresh()
    {
        if (weaponRenderer == null)
            return;

        var item = holder != null ? holder.Model.GetEquippedItem(mountedSlot) : null;
        var sprite = item != null ? item.WorldSprite : null;

        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = sprite != null && IsSelected;
    }
}
