using UnityEngine;

public sealed class AttackSwing2D : MonoBehaviour
{
    [SerializeField] private Transform weaponPivot;
    [SerializeField] private WeaponMountSlot mountSlot;
    [SerializeField] private HoldButton attackButton;
    [SerializeField] private float duration = 0.3f;
    [SerializeField] private float startAngle = 60f;
    [SerializeField] private float endAngle = -60f;

    private SwingTimeline timeline;
    private Quaternion restRotation = Quaternion.identity;
    private bool restCaptured;
    private bool isSwinging;

    public bool IsSwinging => isSwinging;

    private void Awake()
    {
        EnsureReady();
    }

    private void Update()
    {
        Step(Time.deltaTime);
    }

    public void Step(float deltaTime)
    {
        if (!EnsureReady())
        {
            return;
        }

        if (!isSwinging)
        {
            if (attackButton.IsHeld && mountSlot.HasWeapon)
            {
                BeginSwing();
            }

            return;
        }

        timeline.Tick(deltaTime);
        ApplyAngle(timeline.Angle);

        if (!timeline.IsRunning)
        {
            EndSwing();
        }
    }

    private bool EnsureReady()
    {
        if (weaponPivot == null || mountSlot == null || attackButton == null)
        {
            return false;
        }

        if (!restCaptured)
        {
            restRotation = weaponPivot.localRotation;
            restCaptured = true;
        }

        if (timeline == null)
        {
            timeline = new SwingTimeline(duration, startAngle, endAngle);
        }

        return true;
    }

    private void BeginSwing()
    {
        timeline.Restart();
        isSwinging = true;
        ApplyAngle(timeline.Angle);
    }

    private void EndSwing()
    {
        isSwinging = false;
        weaponPivot.localRotation = restRotation;
    }

    private void ApplyAngle(float angle)
    {
        weaponPivot.localRotation = restRotation * Quaternion.Euler(0f, 0f, angle);
    }
}
