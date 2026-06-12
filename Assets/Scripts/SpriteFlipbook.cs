using UnityEngine;

// Cycles a SpriteRenderer through a list of frames at a fixed rate.
// Used for the animated obstacles on the grid.
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteFlipbook : MonoBehaviour
{
    public Sprite[] frames;
    [Tooltip("Frames per second.")]
    public float fps = 6f;

    SpriteRenderer sr;
    float timer;
    int index;

    void Awake() => sr = GetComponent<SpriteRenderer>();

    void Update()
    {
        if (frames == null || frames.Length == 0 || fps <= 0f) return;

        timer += Time.deltaTime;
        float frameTime = 1f / fps;
        if (timer >= frameTime)
        {
            timer -= frameTime;
            index = (index + 1) % frames.Length;
            sr.sprite = frames[index];
        }
    }
}
