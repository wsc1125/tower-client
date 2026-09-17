using UnityEngine;

public sealed class RifleShooter2D : MonoBehaviour
{
    [SerializeField] private Transform weaponPivot;
    [SerializeField] private WeaponMountSlot mountSlot;
    [SerializeField] private HoldButton attackButton;
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private Projectile2D projectilePrefab;
    [SerializeField] private SpriteRenderer flashRenderer;
    [SerializeField] private Sprite[] flashFrames;
    [SerializeField] private PlayerMovement2D movement;
    [SerializeField] private float aimRadius = 4f;
    [SerializeField] private float shotsPerSecond = 6f;
    [SerializeField] private float bulletSpeed = 14f;
    [SerializeField] private float recoilDistance = 0.12f;
    [SerializeField] private float recoilDuration = 0.07f;
    [SerializeField] private float flashDuration = 0.05f;
    [SerializeField] private float flashFramesPerSecond = 40f;

    private float cooldownRemaining;
    private float recoilRemaining;
    private float flashRemaining;
    private Vector3 restLocalPosition;
    private Quaternion restLocalRotation = Quaternion.identity;
    private Vector2 lastAimDirection = Vector2.right;
    private bool restCaptured;
    private SpriteFrameAnimation flashAnimation;
    private EnemyBrain2D[] enemies = System.Array.Empty<EnemyBrain2D>();
    private bool hasTarget;

    public bool IsRecoiling => recoilRemaining > 0f;

    public bool CanFire =>
        attackButton != null && attackButton.IsHeld &&
        mountSlot != null && mountSlot.HasWeapon &&
        cooldownRemaining <= 0f;

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
            return;

        // 枪不在手上（没装备或被切到近战）时不瞄准、不开火，并把姿态与朝向收干净，
        // 否则收起枪后人物仍会「面朝敌人」，移动朝向会被带偏。
        if (!mountSlot.HasWeapon)
        {
            ResetPose();
            return;
        }

        cooldownRemaining = Mathf.Max(0f, cooldownRemaining - deltaTime);
        StepRecoil(deltaTime);
        StepFlash(deltaTime);
        StepAim();

        if (CanFire)
            Fire();
    }

    /// <summary>收起武器时把后坐偏移、枪口火光、瞄准姿态与朝向覆盖一并还原。</summary>
    private void ResetPose()
    {
        recoilRemaining = 0f;
        flashRemaining = 0f;

        if (weaponPivot != null)
        {
            weaponPivot.localPosition = restLocalPosition;
            weaponPivot.localRotation = restLocalRotation;
        }

        if (flashRenderer != null)
            flashRenderer.enabled = false;

        if (movement != null && movement.Visual != null)
            movement.Visual.SetFacingOverride(null);
    }

    public void Fire()
    {
        if (!EnsureReady())
            return;

        cooldownRemaining = 1f / Mathf.Max(shotsPerSecond, 0.01f);
        recoilRemaining = Mathf.Max(recoilDuration, 0f);
        flashRemaining = Mathf.Max(flashDuration, 0f);
        flashAnimation.Reset();

        if (flashRenderer != null)
        {
            flashRenderer.enabled = true;
            flashRenderer.sprite = flashAnimation.CurrentFrame;
        }

        if (projectilePrefab != null && muzzlePoint != null)
        {
            var projectile = Instantiate(projectilePrefab, muzzlePoint.position, muzzlePoint.rotation);
            projectile.Launch(muzzlePoint.right, bulletSpeed);
        }
    }

    private void StepAim()
    {
        var direction = ResolveAimDirection();
        if (direction.sqrMagnitude < 0.0001f)
            return;

        lastAimDirection = direction.normalized;
        var angle = Mathf.Atan2(lastAimDirection.y, lastAimDirection.x) * Mathf.Rad2Deg;
        weaponPivot.localRotation = restLocalRotation * Quaternion.Euler(0f, 0f, angle);

        // 朝左（180°）旋转会让枪体上下颠倒；用 flipY 让枪身保持直立，仅左右镜像。
        if (mountSlot != null && mountSlot.WeaponRenderer != null)
            mountSlot.WeaponRenderer.flipY = lastAimDirection.x < 0f;

        // 锁定敌人时，人物朝向跟随枪口方向的 x 轴（可表现为后退移动）；否则跟随移动方向。
        if (movement != null && movement.Visual != null)
            movement.Visual.SetFacingOverride(hasTarget ? (Vector2?)lastAimDirection : null);
    }

    private Vector2 ResolveAimDirection()
    {
        var target = FindNearestEnemy();
        if (target != null)
        {
            hasTarget = true;
            var toTarget = (Vector2)(target.position - weaponPivot.position);
            if (toTarget.sqrMagnitude > 0.0001f)
                return toTarget.normalized;
        }

        hasTarget = false;

        if (movement != null)
        {
            var move = movement.ResolvedInput;
            if (move.sqrMagnitude > 0.0001f)
                return move.normalized;
        }

        return lastAimDirection;
    }

    private Transform FindNearestEnemy()
    {
        enemies = FindObjectsOfType<EnemyBrain2D>(false);

        Transform best = null;
        var bestSqr = aimRadius * aimRadius;
        var origin = weaponPivot.position;

        for (var i = 0; i < enemies.Length; i++)
        {
            var enemy = enemies[i];
            if (enemy == null || enemy.IsDead)
                continue;

            var toEnemy = (Vector2)(enemy.transform.position - origin);
            var sqr = toEnemy.sqrMagnitude;
            if (sqr <= bestSqr)
            {
                bestSqr = sqr;
                best = enemy.transform;
            }
        }

        return best;
    }

    private void StepRecoil(float deltaTime)
    {
        if (recoilRemaining <= 0f)
            return;

        recoilRemaining -= deltaTime;
        if (recoilRemaining <= 0f)
        {
            recoilRemaining = 0f;
            weaponPivot.localPosition = restLocalPosition;
            return;
        }

        var progress = recoilDuration <= 0f ? 1f : 1f - recoilRemaining / recoilDuration;
        var offset = recoilDistance * Mathf.Sin(progress * Mathf.PI);
        weaponPivot.localPosition = restLocalPosition + new Vector3(-offset, 0f, 0f);
    }

    private void StepFlash(float deltaTime)
    {
        if (flashRemaining <= 0f)
            return;

        flashRemaining -= deltaTime;
        flashAnimation.Tick(deltaTime);
        if (flashRenderer != null)
            flashRenderer.sprite = flashAnimation.CurrentFrame;

        if (flashRemaining <= 0f && flashRenderer != null)
            flashRenderer.enabled = false;
    }

    private bool EnsureReady()
    {
        if (weaponPivot == null || mountSlot == null || attackButton == null)
            return false;

        if (!restCaptured)
        {
            restLocalPosition = weaponPivot.localPosition;
            restLocalRotation = weaponPivot.localRotation;
            restCaptured = true;
        }

        if (flashAnimation == null)
            flashAnimation = new SpriteFrameAnimation(flashFrames, flashFramesPerSecond);

        return true;
    }
}
