using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 玩家星空币余额：负责 PlayerPrefs 持久化与主界面金额显示。
/// 出售/消费统一走 <see cref="Add"/> / <see cref="TrySpend"/>，余额变化时广播 <see cref="Changed"/>。
/// </summary>
public sealed class PlayerCurrency : MonoBehaviour
{
    /// <summary>场景内活跃实例（Awake 赋值 / OnDestroy 清空），云同步经它刷新运行时显示。</summary>
    public static PlayerCurrency Active { get; private set; }

    [SerializeField] private TMP_Text display;
    [SerializeField] private string prefsKey = "StarCoins";
    [SerializeField] private int initialBalance = 20000;

    private int balance;

    public int Balance => balance;

    /// <summary>余额变化时触发，参数为最新余额。</summary>
    public event Action<int> Changed;

    /// <summary>账号隔离的缓存键：未登录全局键，登录后账号键（规则同 PlayerSaveStore）。</summary>
    private string CacheKey => LocalCacheKeys.For(prefsKey, GameSession.LocalPlayerIdValue);

    private void Awake()
    {
        Active = this;
        balance = PlayerPrefs.GetInt(CacheKey, initialBalance);

        // 立即回写：保证缓存键恒等于运行时余额，云存档打包时直接读键，
        // 不依赖本组件在当前场景存活（战斗场景没有它）。
        PlayerPrefs.SetInt(CacheKey, balance);
        PlayerPrefs.Save();
        RefreshDisplay();
    }

    private void OnDestroy()
    {
        if (Active == this)
            Active = null;
    }

    /// <summary>增加余额（出售获得）。金额小于等于 0 时不生效。</summary>
    public void Add(int amount)
    {
        if (amount <= 0)
            return;

        SetBalance(balance + amount);
    }

    /// <summary>尝试消费余额。余额不足返回 false，不做任何改动。</summary>
    public bool TrySpend(int amount)
    {
        if (amount < 0 || balance < amount)
            return false;

        SetBalance(balance - amount);
        return true;
    }

    /// <summary>
    /// 云同步落地：把服务端快照下发的余额应用到运行时并写缓存。
    /// 金额小于 0 时忽略（防御异常快照）。
    /// </summary>
    public void ApplyExternal(int value)
    {
        if (value < 0)
            return;

        SetBalance(value);
    }

    private void SetBalance(int value)
    {
        balance = value;
        PlayerPrefs.SetInt(CacheKey, balance);
        PlayerPrefs.Save();
        RefreshDisplay();
        Changed?.Invoke(balance);
    }

    private void RefreshDisplay()
    {
        if (display != null)
            display.text = balance.ToString();
    }
}
