using UnityEngine;

/// <summary>
/// 迫击炮弹体：从炮口高抛飞向落点，落地时结算范围伤害。
/// 弹体朝向跟随飞行方向：上升段朝上，下落段朝下。
/// 弹体只做表现与伤害结算，预警圈由 <see cref="ArtilleryWarning2D"/> 单独负责。
/// </summary>
public sealed class ArtilleryShell2D : MonoBehaviour
{
    [SerializeField] private float spriteScale = 0.35f;

    private Vector2 start;
    private Vector2 target;
    private float arcHeight;
    private float flightTime;
    private float blastRadius;
    private float blastDamage;
    private float elapsed;
    private bool launched;

    /// <summary>发射。start 为炮口位置，target 为落点。</summary>
    public void Launch(
        Vector2 startPosition,
        Vector2 targetPosition,
        float arc,
        float duration,
        float radius,
        float damage)
    {
        start = startPosition;
        target = targetPosition;
        arcHeight = Mathf.Max(0f, arc);
        flightTime = Mathf.Max(0.05f, duration);
        blastRadius = Mathf.Max(0.1f, radius);
        blastDamage = Mathf.Max(0f, damage);
        elapsed = 0f;
        launched = true;

        transform.position = start;
        transform.localScale = Vector3.one * spriteScale;
        transform.rotation = Quaternion.identity;
    }

    private void Update()
    {
        if (!launched)
            return;

        elapsed += Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / flightTime);

        // 水平线性插值 + 垂直抛物线，形成高抛弧线
        var horizontal = Vector2.Lerp(start, target, t);
        var height = 4f * arcHeight * t * (1f - t);
        transform.position = new Vector3(horizontal.x, horizontal.y + height, 0f);

        // 朝向跟随飞行方向：上升段朝上，顶点后转为朝下。
        // 弹体贴图按【纵向、尖端朝上】绘制，所以直接用速度方向对齐 up 轴。
        var velocity = GetVelocity(t);
        if (velocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.FromToRotation(Vector3.up, velocity.normalized);

        if (t >= 1f)
            Explode();
    }

    /// <summary>抛物线在归一化时间 t 处的速度方向（水平分量恒定，垂直分量随 t 线性变化）。</summary>
    private Vector2 GetVelocity(float t)
    {
        var horizontal = (target - start) / flightTime;
        var vertical = 4f * arcHeight * (1f - 2f * t) / flightTime;
        return new Vector2(horizontal.x, vertical);
    }

    private void Explode()
    {
        launched = false;

        // 范围伤害：落点半径内的玩家受伤
        var hits = Physics2D.OverlapCircleAll(target, blastRadius);
        foreach (var hit in hits)
        {
            var playerHealth = hit.GetComponentInParent<PlayerHealth2D>();
            if (playerHealth != null)
            {
                playerHealth.TakeDamage(blastDamage);
                break;
            }
        }

        Destroy(gameObject);
    }
}
