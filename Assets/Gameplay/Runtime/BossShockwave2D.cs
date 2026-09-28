using UnityEngine;

/// <summary>
/// BOSS 阶段二的红色冲击波：以 BOSS 为圆心向外扩散，对半径内的玩家结算一次范围伤害。
/// 与 <see cref="ArtilleryShell2D"/> 的区别：冲击波不飞行、不锁定落点，
/// 而是原地扩散，因此不需要抛物线，只需要「扩散到最大半径 → 结算 → 销毁」。
/// 伤害在扩散到最大半径的那一刻结算，给玩家留出向外跑的反应时间。
/// </summary>
public sealed class BossShockwave2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float expandDuration = 0.45f;
    [SerializeField] private float startScale = 0.2f;

    private float radius;
    private float damage;
    private float elapsed;
    private float baseScale = 1f;
    private bool playing;
    private bool damageDealt;
    private Collider2D ownerCollider;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>
    /// 开始扩散。radius 为最终伤害半径，ownerCollider 为 BOSS 自身碰撞体（用于避免自伤判定）。
    /// </summary>
    public void Play(float blastRadius, float blastDamage, Collider2D owner = null)
    {
        radius = Mathf.Max(0.1f, blastRadius);
        damage = Mathf.Max(0f, blastDamage);
        ownerCollider = owner;
        elapsed = 0f;
        playing = true;
        damageDealt = false;

        // 冲击波贴图直径对齐伤害直径。baseScale 只算一次，避免每帧累乘导致缩放失控。
        var diameter = radius * 2f;
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        var spriteWidth = sprite != null ? sprite.bounds.size.x : 1f;
        baseScale = spriteWidth > 0.0001f ? diameter / spriteWidth : 1f;

        transform.localScale = Vector3.one * (baseScale * startScale);
    }

    private void Update()
    {
        if (!playing)
            return;

        // 本地冻结（联机弹窗）期间冲击波停住：世界照常推进，但本机看到的扩散暂停。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        if (LocalPauseScope.IsFrozen)
            return;

        elapsed += Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, expandDuration));

        // 从 startScale 扩散到 1（基于固定的 baseScale，不读当前缩放）
        var scale = Mathf.Lerp(startScale, 1f, t);
        transform.localScale = Vector3.one * (baseScale * scale);

        if (t < 1f)
            return;

        if (!damageDealt)
        {
            damageDealt = true;
            DealDamage();
        }

        playing = false;
        Destroy(gameObject);
    }

    /// <summary>范围伤害：以 BOSS 为圆心、radius 为半径，命中玩家结算一次。</summary>
    private void DealDamage()
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, radius);
        foreach (var hit in hits)
        {
            if (ownerCollider != null && hit == ownerCollider)
                continue;

            var playerHealth = hit.GetComponentInParent<PlayerHealth2D>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(damage);
                break;
            }
        }
    }
}
