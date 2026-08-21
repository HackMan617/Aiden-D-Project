using UnityEngine;

// A single shot from the ColorWheelWeapon: a small dot tinted whatever color the wheel landed on,
// travelling in the direction the player was facing when it was fired.
//
// It moves itself and tests its own overlaps rather than going through Rigidbody2D, because this
// project runs no 2D physics at all — Sawblade checks the player the same way. Sawblades are the
// only hazard that can be shot away; the painted-tile walls and the static obstacles are level
// geometry and are left alone. Movement uses scaled time, so shots hang in the air with everything
// else while the game is paused.
public class ColorProjectile : MonoBehaviour
{
    [Tooltip("Unit direction of travel in the XY plane.")]
    public Vector2 direction = Vector2.right;
    [Tooltip("World units per second.")]
    public float speed = 9f;
    [Tooltip("Seconds before the shot gives up and despawns.")]
    public float lifetime = 2.5f;
    [Tooltip("Collision half-extent of the shot itself, added to whatever it is tested against.")]
    public float hitRadius = 0.16f;
    [Tooltip("Diameter of the dot in world units.")]
    public float size = 0.28f;
    [Tooltip("Drawn above the tiles and the sawblades so the shot is never hidden under one.")]
    public int sortingOrder = 20;

    float age;

    // Spawn a shot at `origin`, heading `direction`, tinted `color`.
    public static ColorProjectile Spawn(Vector3 origin, Vector2 direction, Color color, float speed)
    {
        var go = new GameObject("ColorProjectile");
        go.transform.position = origin;

        var shot = go.AddComponent<ColorProjectile>();
        shot.direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        shot.speed = speed;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = DotSprite();
        sr.color = color;
        sr.sortingOrder = shot.sortingOrder;

        // The dot sprite is authored one world unit across, so scale is just the wanted diameter.
        go.transform.localScale = new Vector3(shot.size, shot.size, 1f);
        return shot;
    }

    void Update()
    {
        // Once the level has been won or lost nothing is in flight any more.
        var over = GameOverManager.Instance;
        if (over != null && over.HasEnded) { Destroy(gameObject); return; }

        transform.position += (Vector3)(direction * (speed * Time.deltaTime));

        age += Time.deltaTime;
        if (age >= lifetime) { Destroy(gameObject); return; }

        // Box overlap against every blade currently sweeping the level. There are only ever a
        // handful of each, so a straight scan is cheaper than any bookkeeping around it.
        foreach (Sawblade saw in FindObjectsByType<Sawblade>())
        {
            Vector3 d = saw.transform.position - transform.position;
            if (Mathf.Abs(d.x) <= saw.hitHalfX + hitRadius && Mathf.Abs(d.y) <= saw.hitHalfY + hitRadius)
            {
                Destroy(saw.gameObject);
                Destroy(gameObject);
                return;
            }
        }
    }

    // A round white dot built in code — tinting a white sprite is what gives each shot its color,
    // and it saves the project another piece of art. One world unit across, cached and shared.
    static Sprite dotSprite;
    static Sprite DotSprite()
    {
        if (dotSprite != null) return dotSprite;

        const int S = 16;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        float c = (S - 1) * 0.5f, r = c + 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                float dist = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                // A dark rim keeps the shot readable over bright tiles; black ignores the tint.
                Color px = Color.clear;
                if (dist <= r) px = dist > r - 2f ? new Color(0f, 0f, 0f, 0.85f) : Color.white;
                tex.SetPixel(x, y, px);
            }
        }
        tex.Apply();
        dotSprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), S);
        return dotSprite;
    }
}
