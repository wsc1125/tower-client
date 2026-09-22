using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public sealed class Projectile2D : MonoBehaviour
{
    [SerializeField] private float lifetime = 2.5f;
    [SerializeField] private GameObject impactPrefab;
    [SerializeField] private float impactLifetime = 0.15f;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.right;
    private float speed = 10f;
    private float age;

    public Vector2 Direction => direction;

    public bool IsExpired => age >= lifetime;

    public void Launch(Vector2 dir, float launchSpeed)
    {
        direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.right;
        speed = launchSpeed;
        age = 0f;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
    }

    private void FixedUpdate()
    {
        Step(Time.fixedDeltaTime);
    }

    public void Step(float deltaTime)
    {
        if (IsExpired)
            return;

        age += deltaTime;

        if (body != null)
            body.MovePosition(body.position + direction * (speed * deltaTime));

        if (IsExpired)
            Destroy(gameObject);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        var enemy = collision.collider.GetComponentInParent<EnemyBrain2D>();
        if (enemy != null)
            enemy.TakeDamage(10f);

        if (impactPrefab != null)
        {
            var impact = Instantiate(impactPrefab, transform.position, Quaternion.identity);
            Destroy(impact, Mathf.Max(impactLifetime, 0f));
        }

        Destroy(gameObject);
    }
}
