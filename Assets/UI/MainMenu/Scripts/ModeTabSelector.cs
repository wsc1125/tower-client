using System;
using UnityEngine;

/// <summary>
/// 主界面「单机 / 联机」二选一：选中项显示金色图标，另一项恢复原图标。
/// 本组件只负责选中状态与显示，具体模式逻辑（进入单机/联机）由外部订阅 <see cref="ModeChanged"/>。
/// </summary>
public sealed class ModeTabSelector : MonoBehaviour
{
    /// <summary>单机模式索引。</summary>
    public const int SingleMode = 0;

    /// <summary>联机模式索引。</summary>
    public const int OnlineMode = 1;

    [Header("金色图标覆盖层")]
    [SerializeField] private GameObject singleHighlight;
    [SerializeField] private GameObject onlineHighlight;

    [Header("默认选中")]
    [SerializeField] private int defaultMode = SingleMode;

    /// <summary>选中项变化时触发一次，参数为 0(单机) 或 1(联机)。</summary>
    public event Action<int> ModeChanged;

    /// <summary>当前选中项，-1 表示尚未初始化。</summary>
    public int CurrentMode { get; private set; } = -1;

    /// <summary>当前是否为联机模式。</summary>
    public bool IsOnline => CurrentMode == OnlineMode;

    private void Start()
    {
        SetMode(defaultMode);
    }

    // 以下两个方法供两个点击热区的 Button.onClick 调用。
    public void SelectSingle()
    {
        SetMode(SingleMode);
    }

    public void SelectOnline()
    {
        SetMode(OnlineMode);
    }

    public void SetMode(int mode)
    {
        if (mode == CurrentMode)
            return;

        CurrentMode = mode;

        if (singleHighlight != null)
            singleHighlight.SetActive(mode == SingleMode);

        if (onlineHighlight != null)
            onlineHighlight.SetActive(mode == OnlineMode);

        ModeChanged?.Invoke(mode);
    }
}
