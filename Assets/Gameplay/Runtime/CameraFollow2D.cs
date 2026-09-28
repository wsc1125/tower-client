using UnityEngine;

/// <summary>
/// 房间相机：平滑跟随目标，并把视野夹在房间可视范围内。
/// 某个轴上房间比视野还小时，该轴直接锁在房间中心，避免画面随人左右晃。
/// 夹取范围取的是房间渲染器的世界包围盒，所以视野不会跑到房间贴图之外。
/// </summary>
public sealed class CameraFollow2D : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Transform roomBounds;
    [SerializeField] private Vector2 targetOffset = new Vector2(0f, 0.5f);
    [SerializeField] private float smoothTime = 0.12f;

    private Camera viewCamera;
    private Vector2 velocity;
    private Rect bounds;
    private bool hasBounds;

    /// <summary>当前跟随目标。</summary>
    public Transform Target => target;

    /// <summary>是否已解析出房间可视范围。</summary>
    public bool HasBounds => hasBounds;

    /// <summary>房间可视范围（世界坐标）。</summary>
    public Rect Bounds => bounds;

    public void SetTarget(Transform value)
    {
        target = value;
    }

    /// <summary>
    /// 设置房间包围盒来源。房间是运行时实例化的，每层换房后必须重新指向新房间，
    /// 否则相机仍按旧房间（或空引用）夹取视野。
    /// </summary>
    public void SetRoomBounds(Transform value)
    {
        roomBounds = value;
        hasBounds = false;
    }

    /// <summary>由房间下的渲染器求世界包围盒；没有可用渲染器时返回 false。</summary>
    public bool CaptureRoomBounds()
    {
        hasBounds = false;

        if (roomBounds == null)
            return false;

        var renderers = roomBounds.GetComponentsInChildren<Renderer>();
        var min = Vector2.zero;
        var max = Vector2.zero;
        var captured = false;

        for (var i = 0; i < renderers.Length; i++)
        {
            var candidate = renderers[i];
            if (candidate == null || !candidate.enabled)
                continue;

            var low = (Vector2)candidate.bounds.min;
            var high = (Vector2)candidate.bounds.max;

            if (!captured)
            {
                min = low;
                max = high;
                captured = true;
                continue;
            }

            min = Vector2.Min(min, low);
            max = Vector2.Max(max, high);
        }

        if (!captured)
            return false;

        bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        hasBounds = true;
        return true;
    }

    /// <summary>
    /// 把期望的相机位置夹进房间可视范围；房间在该轴上小于视野时取房间中心。
    /// </summary>
    public static Vector2 ClampToRoom(Vector2 desired, Vector2 viewSize, Rect room)
    {
        var halfWidth = viewSize.x * 0.5f;
        var halfHeight = viewSize.y * 0.5f;

        var x = room.width <= viewSize.x
            ? room.center.x
            : Mathf.Clamp(desired.x, room.xMin + halfWidth, room.xMax - halfWidth);

        var y = room.height <= viewSize.y
            ? room.center.y
            : Mathf.Clamp(desired.y, room.yMin + halfHeight, room.yMax - halfHeight);

        return new Vector2(x, y);
    }

    private void Awake()
    {
        if (viewCamera == null)
            viewCamera = GetComponent<Camera>();

        CaptureRoomBounds();
    }

    private void LateUpdate()
    {
        Step(Time.deltaTime);
    }

    public void Step(float deltaTime)
    {
        if (target == null || viewCamera == null)
            return;

        // 本地冻结（联机弹窗）期间相机停住：世界照常推进，但本机视角不再跟随。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        if (LocalPauseScope.IsFrozen)
            return;

        if (!hasBounds)
            CaptureRoomBounds();

        var viewSize = new Vector2(
            viewCamera.orthographicSize * 2f * viewCamera.aspect,
            viewCamera.orthographicSize * 2f);

        var desired = (Vector2)target.position + targetOffset;
        if (hasBounds)
            desired = ClampToRoom(desired, viewSize, bounds);

        var current = (Vector2)transform.position;
        var next = smoothTime > 0f
            ? Vector2.SmoothDamp(current, desired, ref velocity, smoothTime, Mathf.Infinity, deltaTime)
            : desired;

        transform.position = new Vector3(next.x, next.y, transform.position.z);
    }
}
