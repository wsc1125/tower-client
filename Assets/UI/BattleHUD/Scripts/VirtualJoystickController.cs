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
    private bool isDragging;

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
        ResetHandle();
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

        ResetHandle();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        isDragging = true;
        onPointerDownEvent.Invoke();
        UpdateFromPointer(eventData);
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
        ResetHandle();
        onPointerUpEvent.Invoke();
    }

    private void UpdateFromPointer(PointerEventData eventData)
    {
        if (rootRect == null || handleRect == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rootRect,
                eventData.position,
                eventData.pressEventCamera,
                out var localPoint))
        {
            return;
        }

        var clamped = Vector2.ClampMagnitude(localPoint, Mathf.Max(1f, maxRadius));
        input = clamped / Mathf.Max(1f, maxRadius);
        handleRect.anchoredPosition = clamped;

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

    private void ResetHandle()
    {
        input = Vector2.zero;

        if (handleRect != null)
        {
            handleRect.anchoredPosition = Vector2.zero;
            handleRect.localEulerAngles = Vector3.zero;
        }

        onValueChanged.Invoke(Vector2.zero, 0f);
    }
}
