using UnityEngine;

/// <summary>
/// 传送门气流层的垂直循环滚动。
///
/// 气流贴图比门框开口高，滚动时上下两端始终藏在门框后面，
/// 因此不需要贴图本身无缝 —— 接缝永远露不出来。
/// 箭头画在气流里，会跟着一起流动。
/// </summary>
public sealed class PortalFlowScroll2D : MonoBehaviour
{
    [Tooltip("相对初始位置的上下移动幅度（世界单位）。应小于 (气流高 - 开口高) / 2。")]
    [SerializeField] private float travel = 0.25f;

    [Tooltip("滚动方向：+1 向上（前进），-1 向下（撤退）。")]
    [SerializeField] private float direction = 1f;

    [Tooltip("完整往返一次的速度（周期/秒）。")]
    [SerializeField] private float speed = 0.6f;

    private Vector3 basePosition;
    private float phase;

    private void Awake()
    {
        basePosition = transform.localPosition;
    }

    private void OnEnable()
    {
        // 反复启停时基线可能被上一次滚动污染，重新取一次。
        basePosition = transform.localPosition;
    }

    private void Update()
    {
        if (speed <= 0f || travel <= 0f)
            return;

        phase += Time.deltaTime * speed * Mathf.PI * 2f;

        // 三角波：匀速上下，到端点立刻折返，读起来像持续流动的气流。
        var t = Mathf.PingPong(phase / (Mathf.PI * 2f), 1f) * 2f - 1f;

        var position = basePosition;
        position.y += t * travel * Mathf.Sign(direction);
        transform.localPosition = position;
    }
}
