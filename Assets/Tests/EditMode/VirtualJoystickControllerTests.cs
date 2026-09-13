using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

public class VirtualJoystickControllerTests
{
    [Test]
    public void DraggingUpdatesInputMagnitudeAndHandlePosition()
    {
        var root = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystickController));
        var baseGo = new GameObject("Base", typeof(RectTransform));
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        baseGo.transform.SetParent(root.transform, false);
        handleGo.transform.SetParent(root.transform, false);

        var controller = root.GetComponent<VirtualJoystickController>();
        controller.EditorBind(
            root.GetComponent<RectTransform>(),
            baseGo.GetComponent<RectTransform>(),
            handleGo.GetComponent<RectTransform>(),
            100f
        );

        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                .GetComponent<EventSystem>();
        }

        var eventData = new PointerEventData(eventSystem)
        {
            position = Vector2.zero
        };

        controller.OnPointerDown(eventData);
        eventData.position = new Vector2(100f, 0f);
        controller.OnDrag(eventData);

        Assert.That(controller.IsDragging, Is.True);
        Assert.That(controller.Input.x, Is.EqualTo(1f).Within(0.01f));
        Assert.That(controller.Magnitude, Is.EqualTo(1f).Within(0.01f));
        Assert.That(handleGo.GetComponent<RectTransform>().anchoredPosition.x, Is.EqualTo(100f).Within(0.01f));
    }

    [Test]
    public void PointerUpResetsInputAndHandle()
    {
        var root = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystickController));
        var baseGo = new GameObject("Base", typeof(RectTransform));
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        baseGo.transform.SetParent(root.transform, false);
        handleGo.transform.SetParent(root.transform, false);

        var controller = root.GetComponent<VirtualJoystickController>();
        controller.EditorBind(
            root.GetComponent<RectTransform>(),
            baseGo.GetComponent<RectTransform>(),
            handleGo.GetComponent<RectTransform>(),
            80f
        );

        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                .GetComponent<EventSystem>();
        }

        var eventData = new PointerEventData(eventSystem)
        {
            position = new Vector2(120f, 0f)
        };

        controller.OnPointerDown(eventData);
        controller.OnDrag(eventData);
        controller.OnPointerUp(eventData);

        Assert.That(controller.IsDragging, Is.False);
        Assert.That(controller.Input, Is.EqualTo(Vector2.zero));
        Assert.That(controller.Magnitude, Is.EqualTo(0f).Within(0.01f));
        Assert.That(handleGo.GetComponent<RectTransform>().anchoredPosition, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void PointerDownMovesJoystickBaseToTouchPosition()
    {
        var root = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystickController));
        var baseGo = new GameObject("Base", typeof(RectTransform));
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        baseGo.transform.SetParent(root.transform, false);
        handleGo.transform.SetParent(root.transform, false);

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(960f, 540f);

        var baseRect = baseGo.GetComponent<RectTransform>();
        var handleRect = handleGo.GetComponent<RectTransform>();
        baseRect.anchoredPosition = new Vector2(112f, 84f);
        handleRect.anchoredPosition = new Vector2(112f, 84f);

        var controller = root.GetComponent<VirtualJoystickController>();
        controller.EditorBind(rootRect, baseRect, handleRect, 134f);

        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                .GetComponent<EventSystem>();
        }

        var eventData = new PointerEventData(eventSystem)
        {
            position = new Vector2(160f, 120f)
        };

        controller.OnPointerDown(eventData);

        Assert.That(baseRect.anchoredPosition.x, Is.EqualTo(160f).Within(0.01f));
        Assert.That(baseRect.anchoredPosition.y, Is.EqualTo(120f).Within(0.01f));
        Assert.That(handleRect.anchoredPosition.x, Is.EqualTo(160f).Within(0.01f));
        Assert.That(handleRect.anchoredPosition.y, Is.EqualTo(120f).Within(0.01f));
        Assert.That(controller.Input.x, Is.EqualTo(0f).Within(0.01f));
        Assert.That(controller.Input.y, Is.EqualTo(0f).Within(0.01f));
    }

    [Test]
    public void PointerUpReturnsJoystickToDefaultPosition()
    {
        var root = new GameObject("Joystick", typeof(RectTransform), typeof(VirtualJoystickController));
        var baseGo = new GameObject("Base", typeof(RectTransform));
        var handleGo = new GameObject("Handle", typeof(RectTransform));
        baseGo.transform.SetParent(root.transform, false);
        handleGo.transform.SetParent(root.transform, false);

        var rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(960f, 540f);

        var baseRect = baseGo.GetComponent<RectTransform>();
        var handleRect = handleGo.GetComponent<RectTransform>();
        var defaultPosition = new Vector2(112f, 84f);
        baseRect.anchoredPosition = defaultPosition;
        handleRect.anchoredPosition = defaultPosition;

        var controller = root.GetComponent<VirtualJoystickController>();
        controller.EditorBind(rootRect, baseRect, handleRect, 134f);

        baseRect.anchoredPosition = new Vector2(240f, 160f);
        handleRect.anchoredPosition = new Vector2(260f, 172f);

        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule))
                .GetComponent<EventSystem>();
        }

        var eventData = new PointerEventData(eventSystem);

        controller.OnPointerUp(eventData);

        Assert.That(baseRect.anchoredPosition.x, Is.EqualTo(defaultPosition.x).Within(0.01f));
        Assert.That(baseRect.anchoredPosition.y, Is.EqualTo(defaultPosition.y).Within(0.01f));
        Assert.That(handleRect.anchoredPosition.x, Is.EqualTo(defaultPosition.x).Within(0.01f));
        Assert.That(handleRect.anchoredPosition.y, Is.EqualTo(defaultPosition.y).Within(0.01f));
    }

    [Test]
    public void JoystickPrefab_HasControllerAndNamedChildren()
    {
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/UI/BattleHUD/Prefabs/Modules/Joystick.prefab"
        );

        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<VirtualJoystickController>(), Is.Not.Null);
        Assert.That(prefab.transform.Find("Base"), Is.Not.Null);
        Assert.That(prefab.transform.Find("Handle"), Is.Not.Null);
    }
}
