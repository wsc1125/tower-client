using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class Projectile2D : MonoBehaviour
{
    [SerializeField] private float lifetime = 2.5f;
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private float impactLifetime = 0.15f;
    [SerializeField] private float damage = 10f;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;
    private float speed = 10f;
    private float age;
    private Sprite impactSpriteOverride;

    public Vector2 Direction => direction;

    public bool IsExpired => age >= lifetime;

    /// <summary>单发伤害。由发射方（武器）在 <see cref="Launch"/> 时写入。</summary>
    public float Damage => damage;

    /// <summary>
    /// 覆盖命中特效贴图。只作用于本次发射生成的实例，绝不修改共享的 impactPrefab 资产。
    /// </summary>
    public void SetImpactSprite(Sprite sprite)
    {
        if (sprite == null)
            return;

        impactSpriteOverride = sprite;
    }

    /// <summary>
    /// 覆盖弹丸贴图并按「贴图最大边 ÷ 目标世界长度」归一缩放。
    /// 各武器弹丸素材分辨率差异很大（步枪 52px、霰弹枪 174px），不归一会让霰弹枪子弹变成巨球。
    /// </summary>
    public void SetBulletSprite(Sprite sprite, float targetWorldLength)
    {
        if (sprite == null)
            return;

        var renderer = GetComponent<SpriteRenderer>();
        if (renderer == null)
            return;

        renderer.sprite = sprite;

        var bounds = sprite.bounds.size;
        var maxSide = Mathf.Max(bounds.x, bounds.y);
        if (maxSide > 0f && targetWorldLength > 0f)
        {
            var scale = targetWorldLength / maxSide;
            transform.localScale = new Vector3(scale, scale, 1f);
        }
    }

    public void Launch(Vector2 dir, float launchSpeed)
    {
        Launch(dir, launchSpeed, damage);
    }

    /// <summary>带伤害的发射。伤害由武器决定，弹丸 prefab 上的值只作默认。</summary>
    public void Launch(Vector2 dir, float launchSpeed, float launchDamage)
    {
        Launch(dir, launchSpeed, launchDamage, null);
    }

    /// <summary>
    /// 带伤害与发射者的发射。ownerCollider 为发射者（玩家）的碰撞体，
    /// 传入后会忽略与它的碰撞 —— 否则朝下开火时枪口落在玩家碰撞体内，
    /// 子弹生成的第一帧就会撞到玩家并被销毁，表现为「子弹打不出去」。
    /// </summary>
    public void Launch(Vector2 dir, float launchSpeed, float launchDamage, Collider2D ownerCollider)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        speed = launchSpeed;
        damage = launchDamage;
        age = 0f;

        // 弹丸贴图是水平长条，必须按飞行方向旋转，否则朝上/斜向飞时会「横着飞」。
        var angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (ownerCollider != null)
            IgnoreCollisionWith(ownerCollider);
    }

    /// <summary>
    /// 忽略与另一个碰撞体的碰撞。霰弹枪的多发弹丸从同一枪口生成、触发体互相重叠，
    /// 若不忽略，OnCollisionEnter2D 会在生成的第一帧把彼此全部销毁（表现为「霰弹枪打不出子弹」）。
    /// 由发射方在整批弹丸都生成完毕后统一调用，避免依赖 FindObjectsOfType 的注册时机。
    /// </summary>
    public void IgnoreCollisionWith(Collider2D other)
    {
        if (other == null)
            return;

        foreach (var projectileCollider in GetComponentsInChildren<Collider2D>())
            Physics2D.IgnoreCollision(projectileCollider, other, true);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        Step(Time.fixedDeltaTime);
    }

    public void Step(float deltaTime)
    {
        if (IsExpired)
            return;

        // 世界冻结（仅单机暂停）期间弹体停住：联机下世界永不停，弹体照常飞行。
        // 单机下 IsWorldFrozen 与改造前的 timeScale=0 语义一致。
        if (LocalPauseScope.IsWorldFrozen)
            return;

        age += deltaTime;

        if (body != null)
            body.MovePosition(body.position + direction * (speed * deltaTime));

        if (IsExpired)
            Destroy(gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        var target = collision.collider.GetComponentInParent<IDamageable>();
        if (target != null)
            DealDamage(target);

        if (impactPrefab != null)
        {
            var impact = Instantiate(impactPrefab, transform.position, Quaternion.identity);

            // 只改本次生成的实例，不动共享的 impactPrefab 资产。
            if (impactSpriteOverride != null)
            {
                var renderer = impact.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer != null)
                    renderer.sprite = impactSpriteOverride;
            }

            Destroy(impact, Mathf.Max(impactLifetime, 0f));
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// 结算伤害。联机下敌人血量是主机权威，客户端命中必须转成 ServerRpc 请求主机结算，
    /// 否则只改本地血量、主机不知道（表现为「客户端打怪不掉血」）。
    ///
    /// 敌人对象上挂有 <see cref="Tower.Netcode.EnemyNetworkSync"/> 时走它转发；
    /// 没有（单机 / 非网络敌人）则退回直接结算，行为与改造前一致。
    /// </summary>
    private void DealDamage(IDamageable target)
    {
        var sync = target.transform.GetComponent<Tower.Netcode.EnemyNetworkSync>();
        if (sync != null)
        {
            sync.ApplyDamage(damage);
            return;
        }

        target.TakeDamage(damage);
    }
}
