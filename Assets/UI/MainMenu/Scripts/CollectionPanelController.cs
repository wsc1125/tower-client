using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 收藏展馆面板：4×4 图标网格 + 文字进度 + 全收集奖励状态。
///
/// 无格子底框、无进度条（进度用文字呈现）。超出 16 件时靠 ScrollRect 纵向滑动。
/// 奖励发放：订阅 <see cref="CollectionService.RewardGranted"/> 后转交 <see cref="PlayerCurrency.Add"/>。
/// </summary>
public sealed class CollectionPanelController : MonoBehaviour
{
    [Header("面板引用")]
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private CanvasGroup modalGroup;
    [SerializeField] private Button maskButton;
    [SerializeField] private Button closeButton;

    [Header("图标")]
    [Tooltip("预置的收藏图标视图（一屏格数）。图鉴超出时按需克隆最后一个作为模板。")]
    [SerializeField] private CollectionSlotView[] slots;

    [Header("文本")]
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text allCollectedText;

    [Header("全收集弹窗")]
    [Tooltip("全收集达成时弹出的提示窗；可为空。")]
    [SerializeField] private GameObject allCollectedPopup;
    [SerializeField] private TMP_Text allCollectedPopupText;
    [SerializeField] private Button allCollectedPopupCloseButton;

    [Header("数据")]
    [SerializeField] private ItemDatabase database;
    [SerializeField] private CollectionCatalog catalog;
    [SerializeField] private PlayerCurrency currency;

    [Header("动画")]
    [SerializeField] private float openDuration = 0.15f;
    [SerializeField] private float closeDuration = 0.1f;

    private Coroutine animationRoutine;
    private readonly List<CollectionSlotView> runtimeSlots = new List<CollectionSlotView>();

    public bool IsOpen => modalRoot != null && modalRoot.activeSelf;

    private void Awake()
    {
        CollectionService.Configure(database, catalog);
        BindButtons();

        if (modalRoot != null)
            modalRoot.SetActive(false);

        // 全收集弹窗默认隐藏。
        if (allCollectedPopup != null)
            allCollectedPopup.SetActive(false);
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

    /// <summary>按当前进度刷新全部图标与文本。</summary>
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
                // 图鉴不足一屏时隐藏多余格子。
                slot.gameObject.SetActive(false);
            }
        }

        if (progressText != null)
        {
            var lit = CollectionService.LitCount;
            var total = CollectionService.TotalCount;
            var pending = CollectionService.ObtainedCount - lit;
            progressText.text = $"已点亮 {lit} / {total} · 待点亮 {pending} 件";
        }

        if (allCollectedText != null)
        {
            var done = CollectionService.Progress.AllCollectedRewarded;
            var amount = catalog != null ? catalog.AllCollectedReward : 0;
            allCollectedText.text = done
                ? $"全收集奖励：{amount} 星币  [已达成]"
                : $"全收集奖励：{amount} 星币  [未达成]";
        }
    }

    /// <summary>
    /// 保证格子数量不少于图鉴条目数：预置格子不够时，克隆最后一个作为模板继续追加。
    /// 这样图鉴新增物品后无需改 prefab 即可自动多出格子（超出视口靠 ScrollRect 滑动）。
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

        // 按奖励类型区分提示方式：全收集奖励单独弹窗。
        if (kind == CollectionRewardKind.AllCollected)
            ShowAllCollectedPopup(amount);
    }

    /// <summary>显示全收集达成弹窗。</summary>
    private void ShowAllCollectedPopup(int amount)
    {
        if (allCollectedPopup == null)
            return;

        if (allCollectedPopupText != null)
            allCollectedPopupText.text = $"全收集达成！获得 {amount} 星币";

        allCollectedPopup.SetActive(true);
    }

    private void BindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Close);

        if (maskButton != null)
            maskButton.onClick.AddListener(Close);

        if (allCollectedPopupCloseButton != null)
            allCollectedPopupCloseButton.onClick.AddListener(CloseAllCollectedPopup);
    }

    /// <summary>关闭全收集弹窗。</summary>
    private void CloseAllCollectedPopup()
    {
        if (allCollectedPopup != null)
            allCollectedPopup.SetActive(false);
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
