using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆面板：把已登记的收藏品以图标形式平铺在底板上。
///
/// 设计要点：
///   - 无格子底框，图标直接浮在底板星空区上。
///   - 4 列网格，图标尽可能大，超出视口靠 ScrollRect 纵向滑动。
///   - 不显示任何进度文字，只做展品陈列。
///
/// 奖励发放：订阅 <see cref="CollectionService.RewardGranted"/> 后转交 <see cref="PlayerCurrency.Add"/>。
/// </summary>
public sealed class CollectionPanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("展品网格")]
    [Tooltip("预置的展品视图（一屏格数）。图鉴超出时按需克隆最后一个作为模板。")]
    [SerializeField] private CollectionSlotView[] slots;

    [Tooltip("展品容器。其高度由本脚本按 GridLayoutGroup 的 preferredHeight 驱动。")]
    [SerializeField] private RectTransform slotContainer;

    [Tooltip("展品容器上的网格布局。用于计算内容总高度。")]
    [SerializeField] private GridLayoutGroup slotGrid;

    [Header("数据")]
    [SerializeField] private ItemDatabase database;
    [SerializeField] private CollectionCatalog catalog;
    [SerializeField] private PlayerCurrency currency;

    [Header("动画")]
    [SerializeField] private float openDuration = 0.18f;
    [SerializeField] private float closeDuration = 0.12f;

    private Coroutine animationRoutine;
    private readonly List<CollectionSlotView> runtimeSlots = new List<CollectionSlotView>();

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    private void Awake()
    {
        CollectionService.Configure(database, catalog);
        BindButtons();

        if (modalRoot != null)
            modalRoot.SetActive(false);
    }

    private void OnEnable()
    {
        CollectionService.RewardGranted += HandleRewardGranted;
    }

    private void OnDisable()
    {
        CollectionService.RewardGranted -= HandleRewardGranted;
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (modalRoot == null || IsOpen)
            return;

        modalRoot.SetActive(true);
        PlayAnimation(open: true);
        Refresh();
    }

    public void Close()
    {
        if (modalRoot == null || !IsOpen)
            return;

        PlayAnimation(open: false);
    }

    /// <summary>按当前进度刷新全部展品。</summary>
    public void Refresh()
    {
        var items = CollectionService.Collectibles;

        EnsureSlotCount(items.Count);

        for (var i = 0; i < runtimeSlots.Count; i++)
        {
            var slot = runtimeSlots[i];
            if (slot == null)
                continue;

            if (i < items.Count)
            {
                var item = items[i];
                slot.gameObject.SetActive(true);
                slot.Bind(item, CollectionService.StateOf(item.Id), HandleSlotClicked);
            }
            else
            {
                // 图鉴不足一屏时隐藏多余展位。
                slot.gameObject.SetActive(false);
            }
        }

        ApplyContainerHeight();
    }

    /// <summary>
    /// 保证展位数量不少于图鉴条目数：预置展位不够时，克隆最后一个作为模板继续追加。
    /// 这样图鉴新增物品后无需改 prefab 即可自动多出展位（超出视口靠 ScrollRect 滑动）。
    /// </summary>
    private void EnsureSlotCount(int required)
    {
        if (runtimeSlots.Count == 0 && slots != null)
            runtimeSlots.AddRange(slots);

        if (required <= runtimeSlots.Count)
            return;

        var template = runtimeSlots.Count > 0 ? runtimeSlots[runtimeSlots.Count - 1] : null;
        if (template == null)
            return;

        var parent = template.transform.parent;
        while (runtimeSlots.Count < required)
        {
            var clone = Instantiate(template, parent);
            clone.name = "Slot_" + runtimeSlots.Count.ToString("00");
            runtimeSlots.Add(clone);
        }
    }

    /// <summary>
    /// 按网格实际内容高度撑开容器，供 ScrollRect 滚动。
    ///
    /// 不用 ContentSizeFitter：它与 GridLayoutGroup 会形成循环依赖
    /// （fitter 要 grid 的 preferredHeight，grid 要 rect 的 width，而 rect 的 height 又由 fitter 决定），
    /// 实测 preferredHeight 恒为 0、所有展位堆在原点。改为显式赋值即可打破循环。
    ///
    /// 注意：刚 SetActive(true) 的同一帧，容器宽度可能尚未算出，preferredHeight 会是 0。
    /// 因此本方法在面板打开期间由 LateUpdate 持续校正，直到高度稳定。
    /// </summary>
    private void ApplyContainerHeight()
    {
        if (slotContainer == null || slotGrid == null)
            return;

        // 先自下而上重建父级链，让 rect.width 在布局系统内部真正生效。
        var parent = slotContainer.parent as RectTransform;
        if (parent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);

        // 容器宽度为 0 时网格无法换行，算不出高度，等下一帧。
        if (slotContainer.rect.width <= 0f)
            return;

        var height = slotGrid.preferredHeight;
        if (height <= 0f)
            return;

        if (Mathf.Approximately(slotContainer.sizeDelta.y, height))
            return;

        slotContainer.sizeDelta = new Vector2(slotContainer.sizeDelta.x, height);
        LayoutRebuilder.ForceRebuildLayoutImmediate(slotContainer);
    }

    private void LateUpdate()
    {
        // 面板打开期间持续校正容器高度：分辨率变化、展位增删都能自适应。
        if (IsOpen)
            ApplyContainerHeight();
    }

    private void HandleSlotClicked(string id)
    {
        if (!CollectionService.TryLight(id, out _))
            return;

        Refresh();
    }

    private void HandleRewardGranted(int amount, CollectionRewardKind kind)
    {
        if (currency != null)
            currency.Add(amount);
    }

    private void BindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (maskButton != null)
            maskButton.onClick.AddListener(Close);
    }

    private void PlayAnimation(bool open)
    {
        if (animationRoutine != null)
            StopCoroutine(animationRoutine);

        animationRoutine = StartCoroutine(Animate(open));
    }

    private IEnumerator Animate(bool open)
    {
        var duration = open ? openDuration : closeDuration;
        var from = open ? 0f : 1f;
        var to = open ? 1f : 0f;

        if (modalGroup != null)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                modalGroup.alpha = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            modalGroup.alpha = to;
        }

        if (!open && modalRoot != null)
            modalRoot.SetActive(false);

        animationRoutine = null;
    }
}
