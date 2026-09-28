using UnityEngine;

/// <summary>
/// 地面预警圈：标记落点，从生成到爆炸期间持续收缩，给玩家躲避窗口。
/// 只做表现，伤害由 <see cref="ArtilleryShell2D"/> 在落地时结算。
/// </summary>
public sealed class ArtilleryWarning2D : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private float startScale = 1.6f;
    [SerializeField] private float endScale = 0.85f;
    [SerializeField] private float spriteScale = 0.55f;

    private float duration;
    private float elapsed;
    private float baseScale = 1f;
    private bool playing;

    private void Awake()
    {
        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
    }

    /// <summary>开始播放。radius 为爆炸半径，用于把预警圈缩放到对应世界尺寸。</summary>
    public void Play(float playDuration, float radius)
    {
        duration = Mathf.Max(0.01f, playDuration);
        elapsed = 0f;
        playing = true;

        // 预警圈直径对齐爆炸直径。baseScale 只算一次，避免每帧累乘导致缩放失控。
        var diameter = Mathf.Max(0.1f, radius * 2f);
        var sprite = spriteRenderer != null ? spriteRenderer.sprite : null;
        var spriteWidth = sprite != null ? sprite.bounds.size.x : 1f;
        baseScale = spriteWidth > 0.0001f ? diameter / spriteWidth : 1f;
        baseScale *= spriteScale;

        transform.localScale = Vector3.one * (baseScale * startScale);
    }

    private void Update()
    {
        if (!playing)
            return;

        // 本地冻结（联机弹窗）期间预警圈停住：世界照常推进，但本机看到的倒计时暂停。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        if (LocalPauseScope.IsFrozen)
            return;

        elapsed += Time.deltaTime;
        var t = Mathf.Clamp01(elapsed / duration);

        // 从 startScale 收缩到 endScale（基于固定的 baseScale，不读当前缩放）
        var scale = Mathf.Lerp(startScale, endScale, t);
        transform.localScale = Vector3.one * (baseScale * scale);

        if (t >= 1f)
        {
            playing = false;
            Destroy(gameObject);
        }
    }
}
