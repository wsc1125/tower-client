using UnityEngine;

public sealed class PlayerHealth
{
    public PlayerHealth(float maximum)
    {
        Maximum = Mathf.Max(1f, maximum);
        Current = Maximum;
    }

    public float Maximum { get; }
    public float Current { get; private set; }
    public bool IsDead => Current <= 0f;

    public void TakeDamage(float amount)
    {
        if (IsDead || amount <= 0f)
            return;

        Current = Mathf.Max(0f, Current - amount);
    }
}

public sealed class PlayerHealth2D : MonoBehaviour
{
    [SerializeField] private float maximumHealth = 100f;
    [SerializeField] private StatusPanelController statusPanel;

    private PlayerHealth health;

    public float CurrentHealth => health != null ? health.Current : maximumHealth;
    public bool IsDead => health != null && health.IsDead;

    private void Awake()
    {
        health = new PlayerHealth(maximumHealth);
        RefreshView();
    }

    public void TakeDamage(float amount)
    {
        health.TakeDamage(amount);
        RefreshView();
    }

    private void RefreshView()
    {
        if (statusPanel != null)
            statusPanel.SetHealth(health.Current, health.Maximum);
    }
}
