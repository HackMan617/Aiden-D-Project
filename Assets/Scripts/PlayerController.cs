using UnityEngine;
using UnityEngine.InputSystem;

// 2D four-directional movement for the Aiden D character.
// The project is configured for the Input System package only
// (Player Settings > Active Input Handling = Input System Package),
// so input is read through Keyboard.current rather than the legacy Input class.
//
// Drives an Animator "Speed" float: 0 = idle frame, > 0 = walk cycle.
[RequireComponent(typeof(SpriteRenderer))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Tooltip("Movement speed in world units per second.")]
    public float moveSpeed = 5f;

    [Tooltip("Target on-screen HEIGHT (world units) of the reference idle frame. A single UNIFORM " +
             "scale is derived from this and reused for every frame, so sprites are never distorted " +
             "and the side / up / down sheets keep their true proportions.")]
    public float playerSize = 0.9f;

    Animator animator;
    SpriteRenderer spriteRenderer;
    float referenceHeight; // world-space height of the reference (idle) frame, captured once

    void Awake()
    {
        animator = GetComponent<Animator>();
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Capture the reference frame's height once so the player is scaled by a single UNIFORM
        // factor every frame. Uniform scaling keeps pixels square (no squish/stretch), and a
        // CONSTANT factor avoids per-frame size jitter from the tightly-cropped sprite bounds.
        Sprite s = spriteRenderer.sprite;
        referenceHeight = (s != null && s.bounds.size.y > 0.0001f) ? s.bounds.size.y : 0.4f;
    }

    void Update()
    {
        // Uniform scale: identical factor on X and Y keeps every sprite's pixels square, so the
        // up/down sheets are no longer stretched/squished. Reference height is constant, so the
        // factor doesn't change frame to frame (playerSize stays live-tunable in the Inspector).
        if (referenceHeight > 0.0001f)
        {
            float k = playerSize / referenceHeight;
            transform.localScale = new Vector3(k, k, 1f);
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return; // no keyboard available this frame

        // Collect four-directional input from WASD and the arrow keys.
        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;

        // Normalize diagonals so they aren't faster than cardinal movement.
        if (input.sqrMagnitude > 1f) input = input.normalized;

        // Move on the XY plane (2D).
        transform.position += (Vector3)(input * (moveSpeed * Time.deltaTime));

        // Tell the animator whether we're moving (drives Idle <-> Walk states)...
        animator.SetFloat("Speed", input.sqrMagnitude);
        // ...and the dominant facing direction: 0 = side, 1 = up, 2 = down.
        // Selects WalkSide / WalkUp / WalkDown.
        int dir = 0; // side
        if (Mathf.Abs(input.y) > Mathf.Abs(input.x))
            dir = input.y > 0f ? 1 : 2; // up : down
        if (input.sqrMagnitude > 0.0001f) animator.SetInteger("Direction", dir);

        // Face the direction of horizontal travel (small threshold, never == on floats).
        if (input.x > 0.01f) spriteRenderer.flipX = false;
        else if (input.x < -0.01f) spriteRenderer.flipX = true;
    }
}
