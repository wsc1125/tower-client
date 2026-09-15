using UnityEngine;

public sealed class AnchorGizmo : MonoBehaviour
{
    [SerializeField] private Color color = new Color(1f, 0.85f, 0.2f, 0.9f);
    [SerializeField] private float radius = 0.08f;

    private void OnDrawGizmos()
    {
        Gizmos.color = color;

        var position = transform.position;
        Gizmos.DrawWireSphere(position, radius);
        Gizmos.DrawLine(position - Vector3.right * radius, position + Vector3.right * radius);
        Gizmos.DrawLine(position - Vector3.up * radius, position + Vector3.up * radius);
    }
}
