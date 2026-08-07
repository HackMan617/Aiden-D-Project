using UnityEngine;

// A vertical sawblade hazard that sweeps horizontally across the level (left -> right). It is
// spawned by LevelGrid; its speed and how often it appears scale with the current level, so the
// blades get faster and more frequent the further the player gets. Touching one ends the game.
//
// Movement uses scaled time (Time.deltaTime), so the blade freezes together with the rest of the
// game whenever Time.timeScale is 0 (level-select, pause, game-over / win screens). The animated
// spin is handled separately by a SpriteFlipbook on the same object.
public class Sawblade : MonoBehaviour
{
    [Tooltip("World units per second, moving in +x (left -> right).")]
    public float speed = 2f;
    [Tooltip("Despawn once the blade's x passes this world x (just past the right edge).")]
    public float destroyX = 999f;
    [Tooltip("The player transform this blade can hit.")]
    public Transform target;
    [Tooltip("Collision half-extents around the blade centre (world units). The blade is a thin " +
             "vertical sliver, so x is kept narrow and y about half a cell.")]
    public float hitHalfX = 0.22f;
    public float hitHalfY = 0.45f;

    void Update()
    {
        transform.position += Vector3.right * (speed * Time.deltaTime);

        // Left the play area — clean up.
        if (transform.position.x > destroyX)
        {
            Destroy(gameObject);
            return;
        }

        // Don't score hits once the game has already ended (or before the player exists).
        var gm = GameOverManager.Instance;
        if (gm != null && gm.HasEnded) return;
        if (target == null) return;

        Vector3 d = target.position - transform.position;
        if (Mathf.Abs(d.x) <= hitHalfX && Mathf.Abs(d.y) <= hitHalfY && gm != null)
            gm.TriggerGameOver();
    }
}
