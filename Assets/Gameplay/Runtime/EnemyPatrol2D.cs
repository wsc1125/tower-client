using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class EnemyPatrol2D : MonoBehaviour
{
    [SerializeField] private Vector2 pointA = new(-2f, 0f);
    [SerializeField] private Vector2 pointB = new(2f, 0f);
    [SerializeField] private float moveSpeed = 1.75f;
    [SerializeField] private float arriveThreshold = 0.05f;
    [SerializeField] private Rigidbody2D body;

    private bool movingToB = true;

    public static Vector2 GetNextPosition(Vector2 currentPosition, Vector2 targetPosition, float speed, float deltaTime)
    {
        return Vector2.MoveTowards(currentPosition, targetPosition, speed * deltaTime);
    }

    public static bool ShouldSwapTarget(Vector2 currentPosition, Vector2 targetPosition, float threshold)
    {
        return Vector2.Distance(currentPosition, targetPosition) <= Mathf.Max(0.001f, threshold);
    }

    public void Initialize(Vector2 startPoint, Vector2 endPoint)
    {
        pointA = startPoint;
        pointB = endPoint;
    }

    private void Awake()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }
    }

    private void FixedUpdate()
    {
        if (body == null || moveSpeed <= 0f)
        {
            return;
        }

        var target = movingToB ? pointB : pointA;
        var nextPosition = GetNextPosition(body.position, target, moveSpeed, Time.fixedDeltaTime);
        body.MovePosition(nextPosition);

        if (ShouldSwapTarget(nextPosition, target, arriveThreshold))
        {
            movingToB = !movingToB;
        }
    }
}
