using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 装备配置面板里的单个格子：底图（frame）+ 品质底板 + 有效高亮 + 物品图标。
/// 品质底板仅在格内有物品时显示：装备槽靠它盖住底图上的类型剪影，
/// 仓库格靠它表达物品品质，两类格子共用同一套规则。
/// 支持点击选中与拖拽搬运：拖出时通知控制器生成拖拽图标，
/// 拖入 / 悬停时询问控制器能否接受，用于高亮反馈。
/// </summary>
public sealed class LoadoutSlotView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image frame;
    [SerializeField] private Image highlight;
    [SerializeField] private Image icon;

    [Header("品质底板")]
    [Tooltip("底板根节点；格内有物品时显示。")]
    [SerializeField] private GameObject qualityPlate;
    [Tooltip("品质描边层，按品质着色。")]
    [SerializeField] private Image qualityBorder;
    [Tooltip("底板内层，深底与品质色的混合。")]
    [SerializeField] private Image qualityFill;

    [Tooltip("该格对应的装备槽；仅 Equipment 分区使用。")]
    [SerializeField] private EquipmentSlotType equipmentSlot = EquipmentSlotType.None;

    [Header("耐久条")]
    [Tooltip("耐久条根节点；仅带耐久的物品（防具 / 消耗品）显示。")]
    [SerializeField] private GameObject durabilityBar;

    [Tooltip("耐久条填充层，按剩余比例着色。")]
    [SerializeField] private Image durabilityFill;

    private static readonly Color NormalTint = Color.white;
    private static readonly Color SelectedTint = new Color(0.55f, 0.92f, 1f, 1f);
    private static readonly Color DisabledTint = new Color(1f, 1f, 1f, 0.32f);

    /// <summary>拖拽悬停时的高亮色。刻意用暖金，与任何品质色都不重叠。</summary>
    private static readonly Color DropTint = new Color(1f, 0.82f, 0.42f, 1f);

    private LoadoutPanelController controller;
    private LoadoutSection section;
    private int index;
    private ItemInstance item;
    private bool interactable = true;
    private bool selected;

    public LoadoutSection Section => section;
    public int Index => index;
    public EquipmentSlotType EquipmentSlot => equipmentSlot;

    /// <summary>当前格子里的物品实例；空槽时为 null。拖拽来源判定靠它。</summary>
    public ItemInstance Item => item;

    public void Bind(LoadoutPanelController owner, LoadoutSection ownerSection, int slotIndex)
    {
        controller = owner;
        section = ownerSection;
        index = slotIndex;
        SetInteractable(true);
        SetSelected(false);
    }

    /// <summary>
    /// 更新本格对应的真实索引。仓库分页时格子是复用的，
    /// 翻页后必须由控制器把「页内格号 → 仓库真实索引」的结果推下来，
    /// 否则拖拽 / 点击会命中上一页的格子（显示与交互索引不一致）。
    /// </summary>
    public void SetSlotIndex(int slotIndex)
    {
        index = slotIndex;
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
    /// 与局内背包面板同一套规则，避免两处表现漂移。
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

    /// <summary>耐久越低越红：&gt;50% 绿、&gt;20% 黄、其余红。与局内背包面板同一套规则。</summary>
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

    public void SetInteractable(bool value)
    {
        interactable = value;
        ApplyVisual();
    }

    public void SetSelected(bool value)
    {
        selected = value;
        ApplyVisual();
    }

    /// <summary>拖拽悬停反馈；离开时调用 false 会恢复选中 / 禁用态。</summary>
    public void SetDropHighlight(bool value)
    {
        if (value && CanAcceptDrop())
        {
            if (highlight != null)
            {
                highlight.enabled = true;
                highlight.color = DropTint;
            }
            else if (frame != null)
            {
                frame.color = DropTint;
            }

            return;
        }

        if (highlight != null)
            highlight.color = Color.white;

        ApplyVisual();
    }

    /// <summary>本格能否作为拖拽落点：仓库格 / 携带格看格号是否有效，装备槽看是否启用。</summary>
    private bool CanAcceptDrop()
    {
        if (controller == null)
            return false;

        return section != LoadoutSection.Equipment || interactable;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (controller == null || eventData.button != PointerEventData.InputButton.Left)
            return;

        controller.Select(section, index);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (controller == null || item == null)
            return;

        if (section == LoadoutSection.Equipment && !interactable)
            return;

        controller.BeginDrag(BuildSlotRef(), icon);
    }

    public void OnDrag(PointerEventData eventData)
    {
        controller?.UpdateDrag(eventData.position);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        controller?.EndDrag();
    }

    public void OnDrop(PointerEventData eventData)
    {
        controller?.Drop(BuildSlotRef());
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        controller?.SetTargetHighlight(this, BuildSlotRef(), true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        controller?.SetTargetHighlight(this, BuildSlotRef(), false);
    }

    /// <summary>本格的位置引用。仓库格 / 携带格用格号，装备槽用槽枚举。</summary>
    private SlotRef BuildSlotRef()
    {
        switch (section)
        {
            case LoadoutSection.Equipment:
                return SlotRef.Equipment(equipmentSlot);
            case LoadoutSection.Carry:
                return SlotRef.Carry(index);
            default:
                return SlotRef.Warehouse(index);
        }
    }

    private void ApplyVisual()
    {
        if (frame == null)
            return;

        // 有独立高亮素材的格子（仓库格 / 背包格）用叠高亮表达选中；
        // 没有高亮素材的（装备格）退化为整体着色。
        if (highlight != null)
        {
            highlight.color = Color.white;
            highlight.enabled = selected && interactable;
            frame.color = interactable ? NormalTint : DisabledTint;
        }
        else
        {
            frame.color = !interactable ? DisabledTint : (selected ? SelectedTint : NormalTint);
        }
    }
}
