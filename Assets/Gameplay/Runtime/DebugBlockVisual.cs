using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(SpriteRenderer))]
public class DebugBlockVisual : MonoBehaviour
{
    [SerializeField] private Color blockColor = Color.white;
    [SerializeField] private Vector2 spriteSize = Vector2.one;

    private static Sprite cachedSprite;
    private SpriteRenderer spriteRenderer;

    private void OnEnable()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    private void Apply()
    {
        if (spriteRenderer == null)
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        if (spriteRenderer == null)
        {
            return;
        }

        spriteRenderer.sprite = GetOrCreateSprite();
        spriteRenderer.drawMode = SpriteDrawMode.Sliced;
        spriteRenderer.size = new Vector2(Mathf.Max(0.1f, spriteSize.x), Mathf.Max(0.1f, spriteSize.y));
        spriteRenderer.color = blockColor;
    }

    private static Sprite GetOrCreateSprite()
    {
        if (cachedSprite != null)
        {
            return cachedSprite;
        }

        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            hideFlags = HideFlags.HideAndDontSave
        };
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();

        cachedSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f), 1f, 0u, SpriteMeshType.FullRect);
        cachedSprite.name = "DebugBlockSprite";
        return cachedSprite;
    }
}
