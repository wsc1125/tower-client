using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class EnemyLaserProjectile2D : MonoBehaviour
{
    [SerializeField] private float damage = 12f;
    [SerializeField] private float lifetime = 3f;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;
    private float speed = 7f;
    private float age;

    public static Quaternion GetRotation(Vector2 travelDirection)
    {
        var direction = travelDirection.sqrMagnitude > 0.0001f ? travelDirection.normalized : Vector2.right;
        return Quaternion.FromToRotation(Vector3.up, direction);
    }

    public void Launch(Vector2 launchDirection, float launchSpeed, Collider2D ownerCollider = null)
    {
        direction = launchDirection.sqrMagnitude > 0.0001f ? launchDirection.normalized : Vector2.right;
        speed = launchSpeed;
        transform.rotation = GetRotation(direction);

        if (ownerCollider == null)
            return;

        var projectileColliders = GetComponentsInChildren<Collider2D>();
        foreach (var projectileCollider in projectileColliders)
            Physics2D.IgnoreCollision(projectileCollider, ownerCollider, true);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        age += Time.fixedDeltaTime;
        body.MovePosition(body.position + direction * (speed * Time.fixedDeltaTime));

        if (age >= lifetime)
            Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var playerHealth = other.GetComponentInParent<PlayerHealth2D>();
        if (playerHealth != null)
            playerHealth.TakeDamage(damage);

        Destroy(gameObject);
    }
}
