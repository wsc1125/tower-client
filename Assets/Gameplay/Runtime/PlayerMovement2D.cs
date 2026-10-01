using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement2D : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private VirtualJoystickController joystick;
    [SerializeField] private Rigidbody2D body;
    [SerializeField] private CharacterVisual2D visual;

    private Vector2 resolvedInput;

    public Vector2 ResolvedInput => resolvedInput;

    public CharacterVisual2D Visual => visual;

    /// <summary>当前绑定的虚拟摇杆。联机动态生成的玩家需要在运行时绑定场景摇杆。</summary>
    public VirtualJoystickController Joystick => joystick;

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

    private void OnEnable()
    {
        WorldRegistry.Register(this);
    }

    private void OnDisable()
    {
        WorldRegistry.Unregister(this);
    }

    private void Update()
    {
        // 本地冻结（联机弹窗）期间输入归零：世界照常推进，但本机玩家不动。
        // 单机下 LocalPauseScope.IsFrozen 恒为 false，行为与改造前一致。
        resolvedInput = LocalPauseScope.IsFrozen
            ? Vector2.zero
            : ResolveMovementInput(ReadKeyboardInput(), ReadJoystickInput());

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
