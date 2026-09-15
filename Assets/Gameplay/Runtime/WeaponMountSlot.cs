using UnityEngine;

public sealed class WeaponMountSlot : MonoBehaviour
{
    [SerializeField] private SpriteRenderer weaponRenderer;
    [SerializeField] private InventoryHolder holder;
    [SerializeField] private EquipmentSlotType mountedSlot = EquipmentSlotType.MeleeWeapon;

    private Inventory subscribed;

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
        if (subscribed != null || holder == null)
            return;

        subscribed = holder.Model;
        subscribed.Changed += Refresh;
    }

    private void Unsubscribe()
    {
        if (subscribed == null)
            return;

        subscribed.Changed -= Refresh;
        subscribed = null;
    }

    private void Refresh()
    {
        if (weaponRenderer == null)
            return;

        var item = holder != null ? holder.Model.GetEquippedItem(mountedSlot) : null;
        var sprite = item != null ? item.WorldSprite : null;

        weaponRenderer.sprite = sprite;
        weaponRenderer.enabled = sprite != null;
    }
}
