using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 局内背包里的单个格子：底图（frame）+ 品质底板 + 物品图标。
/// 品质底板仅在格内有物品时显示，颜色按物品品质取，与局外装备配置面板同一套规则。
/// 支持点击选中（详情区据此刷新）与拖拽搬运。
/// </summary>
public sealed class BagItemView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;

    [Header("品质底板")]
    [Tooltip("底板根节点；格内有物品时显示。")]
    [SerializeField] private GameObject qualityPlate;
    [Tooltip("品质描边层，按品质着色。")]
    [SerializeField] private Image qualityBorder;
    [Tooltip("底板内层，深底与品质色的混合。")]
    [SerializeField] private Image qualityFill;

    [Header("耐久条")]
    [Tooltip("耐久条根节点；仅带耐久的物品（防具 / 消耗品）显示。")]
    [SerializeField] private GameObject durabilityBar;
    [Tooltip("耐久条填充层，按剩余比例着色。")]
    [SerializeField] private Image durabilityFill;

    private BagPanelController controller;
    private ItemInstance item;
    private int bagIndex;
    private bool selected;

    public int BagIndex => bagIndex;

    /// <summary>当前格内的物品实例；空槽时为 null。</summary>
    public ItemInstance Item => item;

    public void Bind(BagPanelController owner, int index)
    {
        controller = owner;
        bagIndex = index;
        SetSelected(false);
    }

    public void SetItem(ItemInstance value)
    {
        item = value;
        ApplyQualityPlate(value);
        ApplyDurability(value);

        if (icon == null)
            return;

        var definition = value != null ? value.Definition : null;
        icon.sprite = definition != null ? definition.Icon : null;
        icon.enabled = definition != null && definition.Icon != null;
    }

    /// <summary>
    /// 刷新耐久条。带耐久的物品（防具 / 消耗品）显示，其余隐藏。
    /// 消耗品的耐久是「剩余可用量池」，因此条越短表示还能用的量越少。
    /// </summary>
    private void ApplyDurability(ItemInstance value)
    {
        if (durabilityBar == null || durabilityFill == null)
            return;

        var show = value != null && value.HasDurability;
        durabilityBar.SetActive(show);
        if (!show)
            return;

        var ratio = Mathf.Clamp01((float)value.CurrentDurability / value.MaximumDurability);
        durabilityFill.fillAmount = ratio;
        durabilityFill.color = DurabilityColor(ratio);
    }

    /// <summary>耐久越低越红：&gt;50% 绿、&gt;20% 黄、其余红。与装备槽同一套规则。</summary>
    private static Color DurabilityColor(float ratio)
    {
        if (ratio > 0.5f)
            return new Color(0.42f, 0.85f, 0.45f, 1f);

        if (ratio > 0.2f)
            return new Color(0.95f, 0.78f, 0.30f, 1f);

        return new Color(0.90f, 0.32f, 0.28f, 1f);
    }

    /// <summary>有物品才显示品质底板；颜色按物品品质取。</summary>
    private void ApplyQualityPlate(ItemInstance value)
    {
        if (qualityPlate == null)
            return;

        var definition = value != null ? value.Definition : null;
        var hasItem = definition != null;
        qualityPlate.SetActive(hasItem);

        if (!hasItem)
            return;

        var rarityColor = UiTokens.RarityColor(definition.Rarity);
        if (qualityBorder != null)
            qualityBorder.color = rarityColor;

        if (qualityFill != null)
            qualityFill.color = Color.Lerp(UiTokens.RarityPlateBackground, rarityColor, UiTokens.RarityPlateTintAlpha);
    }

    public void SetSelected(bool value)
    {
        selected = value;
        if (frame != null)
            frame.color = value ? SelectedTint : Color.white;
    }

    /// <summary>拖拽悬停反馈；离开时调用 false 会恢复选中态。</summary>
    public void SetDropHighlight(bool value)
    {
        if (frame == null)
            return;

        frame.color = value ? DropTint : (selected ? SelectedTint : Color.white);
    }

    /// <summary>
    /// 本格能否作为拖拽源：格内有物品即可。
    /// 消耗品（血包 / 甲修）同样可拖拽，用于在携带格之间整理摆放；
    /// 它们不占装备槽，拖到装备槽会被 <see cref="BagPanelController.CanDrop"/> 拒绝
    /// （甲修拖到防具槽是「使用」而非「装备」）。
    /// </summary>
    public bool TryGetDragSource(out SlotRef source)
    {
        source = SlotRef.Carry(bagIndex);
        return item != null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        controller?.SelectBag(bagIndex);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (TryGetDragSource(out var source))
            controller?.BeginDrag(source, icon);
    }

    public void OnDrag(PointerEventData eventData) => controller?.UpdateDrag(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => controller?.EndDrag();
    public void OnDrop(PointerEventData eventData) => controller?.Drop(SlotRef.Carry(bagIndex));
    public void OnPointerEnter(PointerEventData eventData) => controller?.SetTargetHighlight(this, SlotRef.Carry(bagIndex), true);
    public void OnPointerExit(PointerEventData eventData) => controller?.SetTargetHighlight(this, SlotRef.Carry(bagIndex), false);

    /// <summary>选中态着色：青蓝，与局外装备配置面板一致。</summary>
    private static readonly Color SelectedTint = new Color(0.55f, 0.92f, 1f, 1f);

    /// <summary>拖拽悬停时的高亮色。刻意用暖金，与任何品质色都不重叠。</summary>
    private static readonly Color DropTint = new Color(1f, 0.82f, 0.42f, 1f);
}
