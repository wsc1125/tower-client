using UnityEngine;

public sealed class RifleShooter2D : MonoBehaviour
{
    [Tooltip("主手挂点。武器装在主手时用它开火。")]
    [SerializeField] private WeaponMountSlot mainHandSlot;

    [Tooltip("副手挂点。武器装在副手时用它开火。")]
    [SerializeField] private WeaponMountSlot offHandSlot;

    [Tooltip("当前使用的武器槽来源。留空则固定使用主手挂点。")]
    [SerializeField] private PlayerWeaponSelector selector;

    [SerializeField] private HoldButton attackButton;
    [SerializeField] private Projectile2D projectilePrefab;
    [SerializeField] private Sprite[] flashFrames;
    [SerializeField] private PlayerMovement2D movement;
    [SerializeField] private float aimRadius = 4f;
    [SerializeField] private float shotsPerSecond = 6f;
    [SerializeField] private float bulletSpeed = 14f;
    [SerializeField] private float damage = 10f;

    /// <summary>弹丸在游戏里的目标世界长度（取贴图最大边）。各武器弹丸素材分辨率差异大，按此归一。</summary>
    [SerializeField] private float bulletWorldLength = 0.52f;
    [SerializeField] private float recoilDistance = 0.12f;
    [SerializeField] private float recoilDuration = 0.07f;
    [SerializeField] private float flashDuration = 0.05f;
    [SerializeField] private float flashFramesPerSecond = 40f;

    private float cooldownRemaining;
    private float recoilRemaining;
    private float flashRemaining;
    private Vector2 lastAimDirection = Vector2.right;
    private SpriteFrameAnimation flashAnimation;
    private Sprite[] activeFlashFrames;
    private IDamageable[] enemies = System.Array.Empty<IDamageable>();
    private bool hasTarget;

    public bool IsRecoiling => recoilRemaining > 0f;

    /// <summary>
    /// 当前使用的挂点：由 <see cref="PlayerWeaponSelector.ActiveSlot"/> 决定。
    /// 主手与副手完全等价，武器装在哪只手上就用哪只手开火。
    /// </summary>
    private WeaponMountSlot ActiveMount
    {
        get
        {
            if (selector == null)
                return mainHandSlot;

            return selector.ActiveSlot == EquipmentSlotType.OffHand ? offHandSlot : mainHandSlot;
        }
    }

    /// <summary>当前挂点的枪口位置。</summary>
    private Transform MuzzlePoint => ActiveMount != null ? ActiveMount.Muzzle : null;

    /// <summary>当前挂点的枪口火光渲染器。</summary>
    private SpriteRenderer FlashRenderer => ActiveMount != null ? ActiveMount.MuzzleFlash : null;

    public bool CanFire =>
        attackButton != null && attackButton.IsHeld &&
        ActiveMount != null && ActiveMount.HasWeapon &&
        cooldownRemaining <= 0f;

    /// <summary>
    /// 当前手上这把武器的定义；未装备时为 null。
    /// 读的是当前挂点所在槽位，因此主手 / 副手完全等价，副手武器同样能开火。
    /// </summary>
    private ItemDefinition CurrentWeapon
    {
        get
        {
            var mount = ActiveMount;
            var instance = mount != null ? GameSession.Loadout.Equipment.Get(mount.MountedSlot) : null;
            return instance != null ? instance.Definition : null;
        }
    }

    /// <summary>射速：优先取武器定义，未定义时用本组件上的默认值。</summary>
    private float ResolvedShotsPerSecond
    {
        get
        {
            var weapon = CurrentWeapon;
            return weapon != null && weapon.ShotsPerSecond > 0f ? weapon.ShotsPerSecond : shotsPerSecond;
        }
    }

    /// <summary>弹速：优先取武器定义，未定义时用本组件上的默认值。</summary>
    private float ResolvedBulletSpeed
    {
        get
        {
            var weapon = CurrentWeapon;
            return weapon != null && weapon.BulletSpeed > 0f ? weapon.BulletSpeed : bulletSpeed;
        }
    }

    /// <summary>伤害：优先取武器定义，未定义时用本组件上的默认值。</summary>
    private float ResolvedDamage
    {
        get
        {
            var weapon = CurrentWeapon;
            return weapon != null && weapon.Damage > 0f ? weapon.Damage : damage;
        }
    }

    /// <summary>每次开火的弹丸数：优先取武器定义，未定义时为 1（单发）。</summary>
    private int ResolvedProjectilesPerShot
    {
        get
        {
            var weapon = CurrentWeapon;
            return weapon != null ? weapon.ProjectilesPerShot : 1;
        }
    }

    /// <summary>多发时的总散射角度：优先取武器定义，未定义时为 0（全部沿枪口方向）。</summary>
    private float ResolvedSpreadAngle
    {
        get
        {
            var weapon = CurrentWeapon;
            return weapon != null ? weapon.SpreadAngle : 0f;
        }
    }

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

        // 本地冻结（联机弹窗）期间不开火：世界照常推进，但本机玩家不能攻击。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        if (LocalPauseScope.IsFrozen)
        {
            ResetPose();
            return;
        }

        // 枪不在手上（没装备或被切到近战）时不瞄准、不开火，并把姿态与朝向收干净，
        // 否则收起枪后人物仍会「面朝敌人」，移动朝向会被带偏。
        if (ActiveMount == null || !ActiveMount.HasWeapon)
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

    /// <summary>收起武器时把后坐偏移、枪口火光与瞄准姿态一并还原。</summary>
    private void ResetPose()
    {
        recoilRemaining = 0f;
        flashRemaining = 0f;

        // 火光必须无条件关闭：场景里 MuzzleFlash 可能带着贴图且 enabled=true，
        // 只依赖「开火后倒计时归零」来关会让它在未开火时也常驻显示。
        HideFlash();

        // 瞄准旋转与贴图翻转一并还原，避免下次掏枪沿用上次的旧角度。
        var pivot = ResolveAimPivot(ActiveMount);
        if (pivot != null)
            pivot.localRotation = Quaternion.identity;

        var renderer = ActiveMount != null ? ActiveMount.WeaponRenderer : null;
        if (renderer != null)
            renderer.flipY = false;

        if (movement != null && movement.Visual != null)
            movement.Visual.SetFacingOverride(null);
    }

    /// <summary>关闭枪口火光渲染器。所有关闭路径都走这里，避免遗漏。</summary>
    private void HideFlash()
    {
        var flash = FlashRenderer;
        if (flash != null && flash.enabled)
            flash.enabled = false;
    }

    public void Fire()
    {
        if (!EnsureReady())
            return;

        cooldownRemaining = 1f / Mathf.Max(ResolvedShotsPerSecond, 0.01f);
        recoilRemaining = Mathf.Max(recoilDuration, 0f);
        flashRemaining = Mathf.Max(flashDuration, 0f);
        flashAnimation.Reset();

        var flash = FlashRenderer;
        if (flash != null)
        {
            flash.enabled = true;
            flash.sprite = flashAnimation.CurrentFrame;
        }

        if (projectilePrefab != null && MuzzlePoint != null)
            SpawnProjectiles();

        // 后坐与火光只结算一次，不随弹丸数叠加。
    }

    /// <summary>
    /// 按武器的弹丸数与散射角生成弹丸。单发时全部沿瞄准方向；
    /// 多发时在该角度内均匀铺开（首尾各占一半角度），霰弹枪即靠此形成扇面。
    /// </summary>
    private void SpawnProjectiles()
    {
        var muzzle = MuzzlePoint;
        if (muzzle == null)
            return;

        var count = ResolvedProjectilesPerShot;
        var spread = ResolvedSpreadAngle;

        // 枪口挂在武器节点下并随瞄准轴旋转，muzzle.right 就是当前枪管朝向。
        // 以它为准生成弹丸，保证「看到的枪口方向」与「子弹飞行方向」永远一致。
        var baseDirection = muzzle.right;

        // 发射者碰撞体：子弹必须忽略它，否则朝下开火时枪口落在玩家碰撞体内，
        // 子弹生成的第一帧就撞到玩家并被销毁（表现为「子弹打不出去」）。
        var ownerCollider = GetComponent<Collider2D>();

        var spawned = new Projectile2D[count];
        for (var i = 0; i < count; i++)
        {
            var direction = baseDirection;
            if (count > 1 && spread > 0f)
            {
                var t = i / (float)(count - 1);
                var offset = Mathf.Lerp(-spread * 0.5f, spread * 0.5f, t);
                direction = Rotate(baseDirection, offset);
            }

            var projectile = Instantiate(projectilePrefab, muzzle.position, Quaternion.identity);
            ApplyWeaponVisuals(projectile);
            projectile.Launch(direction, ResolvedBulletSpeed, ResolvedDamage, ownerCollider);
            spawned[i] = projectile;
        }

        // 多发弹丸从同一枪口生成、碰撞体互相重叠，必须互相忽略，
        // 否则第一帧就会彼此触发 OnCollisionEnter2D 而全部销毁（霰弹枪打不出子弹）。
        // 先全部生成再统一配对，避免边生成边忽略时后生成的弹丸还没被收录而漏配。
        for (var i = 0; i < spawned.Length; i++)
        {
            if (spawned[i] == null)
                continue;

            for (var j = i + 1; j < spawned.Length; j++)
            {
                if (spawned[j] == null)
                    continue;

                foreach (var collider in spawned[i].GetComponentsInChildren<Collider2D>())
                    spawned[j].IgnoreCollisionWith(collider);
            }
        }
    }

    private static Vector2 Rotate(Vector2 direction, float degrees)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var sin = Mathf.Sin(radians);
        var cos = Mathf.Cos(radians);
        return new Vector2(direction.x * cos - direction.y * sin, direction.x * sin + direction.y * cos);
    }

    /// <summary>把当前武器的弹丸贴图与命中特效套到刚生成的弹丸上；武器未定义时保持 prefab 原样。</summary>
    private void ApplyWeaponVisuals(Projectile2D projectile)
    {
        var weapon = CurrentWeapon;
        if (weapon == null)
            return;

        if (weapon.BulletSprite != null)
            projectile.SetBulletSprite(weapon.BulletSprite, bulletWorldLength);

        if (weapon.ImpactSprite != null)
            projectile.SetImpactSprite(weapon.ImpactSprite);
    }

    /// <summary>
    /// 把枪口对准指定方向。有敌人时指最近敌人，没有时跟随移动方向（判定见 <see cref="ResolveAimDirection"/>）。
    /// 旋转的是 <see cref="WeaponMountSlot.AimPivot"/>，枪口与火光都是它的子节点，位置随之自动正确；
    /// 武器朝左（|角度|>90°）时垂直翻转贴图，避免枪身倒扣。
    /// </summary>
    public void AimAt(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f)
            return;

        lastAimDirection = direction.normalized;

        var angle = Mathf.Atan2(lastAimDirection.y, lastAimDirection.x) * Mathf.Rad2Deg;

        var pivot = ResolveAimPivot(ActiveMount);
        if (pivot != null)
            pivot.rotation = Quaternion.Euler(0f, 0f, angle);

        var renderer = ActiveMount != null ? ActiveMount.WeaponRenderer : null;
        if (renderer != null)
            renderer.flipY = Mathf.Abs(angle) > 90f;

        // 锁定敌人时，人物朝向跟随枪口方向的 x 轴（可表现为后退移动）；否则跟随移动方向。
        if (movement != null && movement.Visual != null)
            movement.Visual.SetFacingOverride(hasTarget ? (Vector2?)lastAimDirection : null);
    }

    /// <summary>当前挂点的瞄准旋转轴：优先用挂点上配置的 aimPivot，没配则退回枪口的父节点（武器节点）。</summary>
    private static Transform ResolveAimPivot(WeaponMountSlot mount)
    {
        if (mount == null)
            return null;

        if (mount.AimPivot != null)
            return mount.AimPivot;

        return mount.Muzzle != null ? mount.Muzzle.parent : null;
    }

    /// <summary>解析瞄准方向并驱动枪械姿态。方向来源见 <see cref="ResolveAimDirection"/>。</summary>
    private void StepAim()
    {
        var direction = ResolveAimDirection();
        if (direction.sqrMagnitude < 0.0001f)
            return;

        AimAt(direction);
    }

    private Vector2 ResolveAimDirection()
    {
        var target = FindNearestEnemy();
        if (target != null)
        {
            hasTarget = true;
            var toTarget = (Vector2)(target.position - transform.position);
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
        var registered = WorldRegistry.Damageables;
        enemies = new IDamageable[registered.Count];
        var index = 0;
        foreach (var damageable in registered)
            enemies[index++] = damageable;

        Transform best = null;
        var bestSqr = aimRadius * aimRadius;
        var origin = transform.position;

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
            return;
        }
    }

    private void StepFlash(float deltaTime)
    {
        var flash = FlashRenderer;

        if (flashRemaining <= 0f)
        {
            // 兜底：火光已结束但渲染器仍开着（例如切武器/重进 Play 模式留下的残留），这里补关一次。
            HideFlash();
            return;
        }

        flashRemaining -= deltaTime;
        flashAnimation.Tick(deltaTime);
        if (flash != null)
            flash.sprite = flashAnimation.CurrentFrame;

        if (flashRemaining <= 0f)
            HideFlash();
    }

    private bool EnsureReady()
    {
        if (attackButton == null)
            return false;

        if (flashAnimation == null || activeFlashFrames != ResolvedFlashFrames)
        {
            activeFlashFrames = ResolvedFlashFrames;
            flashAnimation = new SpriteFrameAnimation(activeFlashFrames, flashFramesPerSecond);
        }

        // 未开火时确保火光关闭。场景里 MuzzleFlash 可能被序列化成 enabled=true 且带贴图，
        // 若不在这里主动关一次，进场景就会看到一团常驻火光跟着玩家跑。
        if (flashRemaining <= 0f)
            HideFlash();

        return true;
    }

    /// <summary>枪口火光帧：优先取武器定义，未定义时用本组件上的默认序列。</summary>
    private Sprite[] ResolvedFlashFrames
    {
        get
        {
            var weapon = CurrentWeapon;
            return weapon != null && weapon.FlashFrames != null && weapon.FlashFrames.Length > 0
                ? weapon.FlashFrames
                : flashFrames;
        }
    }
}
