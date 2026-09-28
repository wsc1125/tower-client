using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 局内背包里的装备槽：底图（frame）+ 品质底板 + 物品图标 + 耐久条。
/// 品质底板仅在槽内有物品时显示，用于盖住底图上的类型剪影，与局外装备配置面板同一套规则。
/// 耐久条仅防具（耐久上限 &gt; 0）显示，按「每件装备自己的耐久」着色。
/// 支持点击选中（详情区据此刷新）与拖拽搬运。
/// </summary>
public sealed class EquipmentSlotView : MonoBehaviour,
    IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image icon;
    [SerializeField] private Image frame;
    [SerializeField] private EquipmentSlotType slotType;

    [Header("品质底板")]
    [Tooltip("底板根节点；槽内有物品时显示。")]
    [SerializeField] private GameObject qualityPlate;
    [Tooltip("品质描边层，按品质着色。")]
    [SerializeField] private Image qualityBorder;
    [Tooltip("底板内层，深底与品质色的混合。")]
    [SerializeField] private Image qualityFill;

    [Header("耐久条")]
    [Tooltip("耐久条根节点；仅防具且耐久上限 > 0 时显示。")]
    [SerializeField] private GameObject durabilityBar;
    [Tooltip("耐久条填充层，按剩余比例着色。")]
    [SerializeField] private Image durabilityFill;

    [Header("修甲进度条")]
    [Tooltip("修甲蓄力进度条根节点；甲修拖到本槽并按住时显示。")]
    [SerializeField] private GameObject repairProgressBar;
    [Tooltip("修甲蓄力进度填充层，0→1 表示蓄力进度。")]
    [SerializeField] private Image repairProgressFill;

    private BagPanelController controller;
    private ItemInstance item;
    private bool selected;

    public EquipmentSlotType SlotType => slotType;
    public bool IsEnabled => EquipmentSlots.IsUsable(slotType);

    /// <summary>当前槽内的物品实例；空槽时为 null。</summary>
    public ItemInstance Item => item;

    /// <summary>
    /// 显示 / 更新修甲蓄力进度条。progress 为 0~1；传 null 表示隐藏。
    /// 进度条与耐久条共用槽位下沿，因此显示进度条时临时隐藏耐久条，避免两条叠在一起。
    /// </summary>
    public void SetRepairProgress(float? progress)
    {
        if (repairProgressBar == null || repairProgressFill == null)
            return;

        var show = progress.HasValue;
        repairProgressBar.SetActive(show);

        if (!show)
        {
            // 蓄力结束：把耐久条恢复成「按当前耐久」的正常显示。
            RefreshDurabilityVisibility();
            return;
        }

        repairProgressFill.fillAmount = Mathf.Clamp01(progress.Value);
        repairProgressFill.color = RepairProgressColor;

        // 蓄力期间隐藏耐久条，避免两条重叠。
        if (durabilityBar != null)
            durabilityBar.SetActive(false);
    }

    /// <summary>蓄力进度条颜色：暖金，与耐久条的红/黄/绿都不重叠。</summary>
    private static readonly Color RepairProgressColor = new Color(1f, 0.82f, 0.42f, 1f);

    /// <summary>按当前物品重新决定耐久条是否显示（蓄力结束后调用）。</summary>
    private void RefreshDurabilityVisibility()
    {
        if (durabilityBar == null)
            return;

        var show = item != null && item.MaximumDurability > 0;
        durabilityBar.SetActive(show);
    }

    public void Bind(BagPanelController owner, EquipmentSlotType slot)
    {
        controller = owner;
        slotType = slot;
        SetSelected(false);
    }

    public void SetItem(ItemInstance value)
    {
        item = value;
        ApplyQualityPlate(value);

        if (icon == null)
            return;

        var definition = value != null ? value.Definition : null;
        icon.sprite = definition != null ? definition.Icon : null;
        icon.enabled = definition != null && definition.Icon != null;
    }

    /// <summary>
    /// 刷新该槽装备的耐久条。直接读物品实例，因此受击磨损后无需额外接线即可反映。
    /// 非防具（上限为 0）时隐藏耐久条。
    /// </summary>
    public void SetDurability(int current, int maximum)
    {
        if (durabilityBar == null || durabilityFill == null)
            return;

        var show = maximum > 0;
        durabilityBar.SetActive(show);
        if (!show)
            return;

        var ratio = Mathf.Clamp01((float)current / maximum);
        durabilityFill.fillAmount = ratio;
        durabilityFill.color = DurabilityColor(ratio);
    }

    /// <summary>耐久越低越红：&gt;50% 绿、&gt;20% 黄、其余红。</summary>
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
        ApplyVisual();
    }

    /// <summary>拖拽悬停反馈；离开时调用 false 会恢复选中 / 禁用态。</summary>
    public void SetDropHighlight(bool value)
    {
        if (frame == null)
            return;

        frame.color = value ? DropTint : NormalTint;
        if (!value)
            ApplyVisual();
    }

    public bool TryGetDragSource(out SlotRef source)
    {
        source = SlotRef.Equipment(slotType);
        return IsEnabled && item != null;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        controller?.SelectEquipment(slotType);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (TryGetDragSource(out var source))
            controller?.BeginDrag(source, icon);
    }

    public void OnDrag(PointerEventData eventData) => controller?.UpdateDrag(eventData.position);
    public void OnEndDrag(PointerEventData eventData) => controller?.EndDrag();
    public void OnDrop(PointerEventData eventData) => controller?.Drop(SlotRef.Equipment(slotType));

    public void OnPointerEnter(PointerEventData eventData)
    {
        var target = SlotRef.Equipment(slotType);
        controller?.SetTargetHighlight(this, target, true);
        // 拖着甲修停在防具槽上即开始蓄力（「按住不放」的语义）。
        controller?.BeginRepairHoldIfRepairing(target);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        var target = SlotRef.Equipment(slotType);
        controller?.SetTargetHighlight(this, target, false);
        // 指针移开即取消未完成的蓄力，不消耗甲修。
        controller?.CancelRepairHoldFor(target);
    }

    private void ApplyVisual()
    {
        if (frame == null)
            return;

        frame.color = !IsEnabled ? DisabledTint : (selected ? SelectedTint : NormalTint);
    }

    private static readonly Color NormalTint = Color.white;

    /// <summary>选中态着色：青蓝，与局外装备配置面板一致。</summary>
    private static readonly Color SelectedTint = new Color(0.55f, 0.92f, 1f, 1f);

    /// <summary>未启用的装备槽（如未解锁的副手）整体压暗。</summary>
    private static readonly Color DisabledTint = new Color(1f, 1f, 1f, 0.32f);

    /// <summary>拖拽悬停时的高亮色。刻意用暖金，与任何品质色都不重叠。</summary>
    private static readonly Color DropTint = new Color(1f, 0.82f, 0.42f, 1f);
}
