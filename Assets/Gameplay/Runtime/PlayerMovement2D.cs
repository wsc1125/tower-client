using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement2D : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private VirtualJoystickController joystick;
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private CharacterVisual2D visual;

    private Vector2 resolvedInput;

    public static Vector2 ResolveMovementInput(Vector2 keyboardInput, Vector2 joystickInput)
    {
        return Vector2.ClampMagnitude(Vector2.ClampMagnitude(keyboardInput, 1f) + Vector2.ClampMagnitude(joystickInput, 1f), 1f);
    }

    public void SetJoystick(VirtualJoystickController targetJoystick)
    {
        joystick = targetJoystick;
    }

    private void Awake()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }
    }

    private void Update()
    {
        resolvedInput = ResolveMovementInput(ReadKeyboardInput(), ReadJoystickInput());

        if (visual != null)
        {
            visual.SetMotion(resolvedInput * moveSpeed);
        }
    }

    private void FixedUpdate()
    {
        if (body == null)
        {
            return;
        }

        body.MovePosition(body.position + resolvedInput * (moveSpeed * Time.fixedDeltaTime));
    }

    private static Vector2 ReadKeyboardInput()
    {
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
    }

    private Vector2 ReadJoystickInput()
    {
        return joystick != null ? joystick.Input : Vector2.zero;
    }
}
