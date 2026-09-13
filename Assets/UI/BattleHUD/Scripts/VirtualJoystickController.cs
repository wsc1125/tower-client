using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

[System.Serializable]
public class JoystickValueChangedEvent : UnityEvent<Vector2, float>
{
}

public class VirtualJoystickController : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [SerializeField] private RectTransform rootRect;
    [SerializeField] private RectTransform baseRect;
    [SerializeField] private RectTransform handleRect;
    [SerializeField] private float maxRadius = 100f;
    [SerializeField] private JoystickValueChangedEvent onValueChanged = new();
    [SerializeField] private UnityEvent onPointerDownEvent = new();
    [SerializeField] private UnityEvent onPointerUpEvent = new();

    private Vector2 input;
    private Vector2 defaultBasePosition;
    private bool isDragging;
    private bool hasDefaultBasePosition;

    public Vector2 Input => input;
    public float Magnitude => input.magnitude;
    public bool IsDragging => isDragging;
    public JoystickValueChangedEvent OnValueChanged => onValueChanged;
    public UnityEvent OnPointerDownEvent => onPointerDownEvent;
    public UnityEvent OnPointerUpEvent => onPointerUpEvent;

    public void EditorBind(RectTransform root, RectTransform baseTarget, RectTransform handleTarget, float radius)
    {
        rootRect = root;
        baseRect = baseTarget;
        handleRect = handleTarget;
        maxRadius = radius;
        CacheDefaultBasePosition();
        ResetToDefaultPosition();
    }

    private void Awake()
    {
        if (rootRect == null)
        {
            rootRect = GetComponent<RectTransform>();
        }

        if (baseRect == null)
        {
            baseRect = transform.Find("Base") as RectTransform;
        }

        if (handleRect == null)
        {
            handleRect = transform.Find("Handle") as RectTransform;
        }

        if (maxRadius <= 0f && baseRect != null)
        {
            maxRadius = baseRect.sizeDelta.x * 0.5f;
        }

        CacheDefaultBasePosition();
        ResetToDefaultPosition();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!TryGetLocalPoint(eventData, out var localPoint))
        {
            return;
        }

        isDragging = true;
        MoveBaseTo(localPoint);
        onPointerDownEvent.Invoke();
        UpdateFromLocalPoint(localPoint);
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging)
        {
            return;
        }

        UpdateFromPointer(eventData);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        isDragging = false;
        ResetToDefaultPosition();
        onPointerUpEvent.Invoke();
    }

    private void UpdateFromPointer(PointerEventData eventData)
    {
        if (!TryGetLocalPoint(eventData, out var localPoint))
        {
            return;
        }

        UpdateFromLocalPoint(localPoint);
    }

    private void UpdateFromLocalPoint(Vector2 localPoint)
    {
        if (baseRect == null || handleRect == null)
        {
            return;
        }

        var center = baseRect.anchoredPosition;
        var clamped = Vector2.ClampMagnitude(localPoint - center, Mathf.Max(1f, maxRadius));
        input = clamped / Mathf.Max(1f, maxRadius);
        handleRect.anchoredPosition = center + clamped;

        if (input.sqrMagnitude > 0.0001f)
        {
            var angle = Mathf.Atan2(input.y, input.x) * Mathf.Rad2Deg - 90f;
            handleRect.localEulerAngles = new Vector3(0f, 0f, angle);
        }
        else
        {
            handleRect.localEulerAngles = Vector3.zero;
        }

        onValueChanged.Invoke(input, input.magnitude);
    }

    private bool TryGetLocalPoint(PointerEventData eventData, out Vector2 localPoint)
    {
        localPoint = Vector2.zero;

        if (rootRect == null)
        {
            return false;
        }

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rootRect,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint);
    }

    private void CacheDefaultBasePosition()
    {
        if (baseRect == null)
        {
            return;
        }

        defaultBasePosition = baseRect.anchoredPosition;
        hasDefaultBasePosition = true;
    }

    private void MoveBaseTo(Vector2 anchoredPosition)
    {
        if (baseRect == null)
        {
            return;
        }

        baseRect.anchoredPosition = anchoredPosition;
        ResetHandle();
    }

    private void ResetToDefaultPosition()
    {
        if (!hasDefaultBasePosition)
        {
            CacheDefaultBasePosition();
        }

        if (baseRect != null)
        {
            baseRect.anchoredPosition = defaultBasePosition;
        }

        ResetHandle();
    }

    private void ResetHandle()
    {
        input = Vector2.zero;

        if (handleRect != null)
        {
            handleRect.anchoredPosition = baseRect != null ? baseRect.anchoredPosition : Vector2.zero;
            handleRect.localEulerAngles = Vector3.zero;
        }

        onValueChanged.Invoke(Vector2.zero, 0f);
    }
}
