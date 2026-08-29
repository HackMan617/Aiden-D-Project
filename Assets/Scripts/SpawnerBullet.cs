using UnityEngine;

// A single shot fired by a BulletSpawner. It flies in a straight line, ends the run on contact with
// the player, and gives up when it leaves the board or buries itself in something solid.
//
// Like Sawblade and ColorProjectile it moves itself and tests its own overlap instead of going
// through a Rigidbody2D, because this project runs no 2D physics at all. Movement is on scaled time,
// so shots hang in the air with everything else while the pause or game-over screen is up.
public class SpawnerBullet : MonoBehaviour
{
    [Tooltip("The grid this bullet flies over. Used to find out where it should stop; without one " +
             "the bullet is bounded only by its lifetime.")]
    public LevelGrid grid;
    [Tooltip("Unit direction of travel in the XY plane.")]
    public Vector2 direction = Vector2.right;
    [Tooltip("World units per second.")]
    public float speed = 3.5f;
    [Tooltip("The player transform this bullet can hit.")]
    public Transform target;
    [Tooltip("Collision half-extent around the bullet's centre, in world units.")]
    public float hitRadius = 0.28f;
    [Tooltip("Backstop despawn, in seconds, for a bullet that never meets an edge it recognises.")]
    public float lifetime = 15f;

    float age;

    void Update()
    {
        // Nothing is in flight any more once the level has been won or lost.
        var over = GameOverManager.Instance;
        if (over != null && over.HasEnded) { Destroy(gameObject); return; }

        transform.position += (Vector3)(direction * (speed * Time.deltaTime));

        age += Time.deltaTime;
        if (age >= lifetime) { Destroy(gameObject); return; }

        // Off the board, or into a wall the designer painted — either way the shot is spent.
        if (grid != null && grid.StopsBullet(transform.position)) { Destroy(gameObject); return; }

        if (target == null) return;
        Vector3 d = target.position - transform.position;
        if (Mathf.Abs(d.x) <= hitRadius && Mathf.Abs(d.y) <= hitRadius && over != null)
            over.TriggerGameOver();
    }
}
