using UnityEngine;

// Animated game background. The sprite sheet ("aiden d updated bg", 768x128 = 6 frames of
// 128x128) is sliced at runtime and shown behind everything. The background follows the camera
// so it always fills the view, and its animation loops continuously — it keeps playing whether
// or not the player is moving.
[RequireComponent(typeof(SpriteRenderer))]
public class BackgroundController : MonoBehaviour
{
    [Tooltip("The 'aiden d updated bg' texture (768x128 = 6 frames of 128x128).")]
    public Texture2D bgSheet;
    [Tooltip("Animation speed, in frames per second.")]
    public float fps = 2f;
    [Tooltip("World-units the background spans; must cover the camera view at max zoom.")]
    public float coverSize = 30f;
    [Tooltip("Sorting order — keep well below the tiles (-10) so it draws behind everything.")]
    public int sortingOrder = -100;

    SpriteRenderer sr;
    Transform cam;
    Sprite[] frames;
    int frame;
    float timer;

    void Start()
    {
        sr = GetComponent<SpriteRenderer>();
        sr.sortingOrder = sortingOrder;

        if (Camera.main != null) cam = Camera.main.transform;

        BuildFrames();
        if (frames != null && frames.Length > 0)
        {
            sr.sprite = frames[0];
            ScaleToCover();
        }
    }

    // Slice the strip into square frames (one per 128px column) at runtime — no asset re-slice.
    void BuildFrames()
    {
        if (bgSheet == null)
        {
            Debug.LogError("[BackgroundController] bgSheet texture is not assigned.");
            return;
        }
        int fh = bgSheet.height;                 // 128 (square frames)
        if (fh <= 0) return;
        int n = Mathf.Max(1, bgSheet.width / fh); // 6
        frames = new Sprite[n];
        for (int i = 0; i < n; i++)
            frames[i] = Sprite.Create(bgSheet, new Rect(i * fh, 0f, fh, fh),
                                      new Vector2(0.5f, 0.5f), fh);
    }

    // Stretch the (square) frame to span coverSize world units in both axes.
    void ScaleToCover()
    {
        Vector2 b = sr.sprite.bounds.size;
        if (b.x > 0f && b.y > 0f)
            transform.localScale = new Vector3(coverSize / b.x, coverSize / b.y, 1f);
    }

    void LateUpdate()
    {
        // Follow the camera so the background always fills the view (keep our own z).
        if (cam != null)
        {
            Vector3 p = transform.position;
            transform.position = new Vector3(cam.position.x, cam.position.y, p.z);
        }

        // Advance the animation continuously, independent of player movement.
        if (frames != null && frames.Length > 0)
        {
            timer += Time.deltaTime;
            float frameTime = 1f / Mathf.Max(1f, fps);
            while (timer >= frameTime)
            {
                timer -= frameTime;
                frame = (frame + 1) % frames.Length;
                sr.sprite = frames[frame];
            }
        }
    }
}
