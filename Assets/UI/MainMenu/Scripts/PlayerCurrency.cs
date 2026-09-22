using System;
using TMPro;
using UnityEngine;

/// <summary>
/// 玩家星空币余额：负责 PlayerPrefs 持久化与主界面金额显示。
/// 出售/消费统一走 <see cref="Add"/> / <see cref="TrySpend"/>，余额变化时广播 <see cref="Changed"/>。
/// </summary>
public sealed class PlayerCurrency : MonoBehaviour
{
    [SerializeField] private TMP_Text display;
    [SerializeField] private string prefsKey = "StarCoins";
    [SerializeField] private int initialBalance = 1240;

    private int balance;

    public int Balance => balance;

    /// <summary>余额变化时触发，参数为最新余额。</summary>
    public event Action<int> Changed;

    private void Awake()
    {
        balance = PlayerPrefs.GetInt(prefsKey, initialBalance);
        RefreshDisplay();
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

    private void SetBalance(int value)
    {
        balance = value;
        PlayerPrefs.SetInt(prefsKey, balance);
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
