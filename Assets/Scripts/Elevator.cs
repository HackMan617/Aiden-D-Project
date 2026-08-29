using UnityEngine;

// A moving platform, dropped into a level with the editor's Elevator tool. It rides up and down the
// full height of the column it was placed in, turning around at each end, and while the player is
// standing on it it carries them along.
//
// Being carried is the whole point of placing one. The player's own red trail is a wall they can
// never walk back over, so a level normally only runs one way; a lift hands them a way back, because
// LevelGrid folds a carried move into the player's position BEFORE the trail test rather than after
// (see LevelGrid.CarryPlayer). Riding also doesn't paint, so the lift's column stays open.
//
// Like every other moving thing here it drives itself on scaled time — so it freezes with the pause
// and game-over screens — and tests its own overlap rather than using 2D physics, which this project
// never runs.
public class Elevator : MonoBehaviour
{
    [Tooltip("The grid this platform rides over. It is told about every carried move so the trail " +
             "test can allow it.")]
    public LevelGrid grid;
    [Tooltip("The player transform this platform can carry.")]
    public Transform target;
    [Tooltip("World units per second along the column.")]
    public float speed = 1.6f;
    [Tooltip("World y values the platform turns around at — the bottom and top rows of the board.")]
    public float minY, maxY;
    [Tooltip("How close to the platform's centre the player has to be to be riding it.")]
    public float rideHalfX = 0.5f, rideHalfY = 0.5f;

    int direction = 1; // +1 rising, -1 falling; flipped at each end of the column

    void Update()
    {
        var over = GameOverManager.Instance;
        if (over != null && over.HasEnded) return;

        Vector3 pos = transform.position;
        float y = pos.y + direction * speed * Time.deltaTime;

        // Turn around at the ends. Clamping to the limit rather than reflecting past it means a
        // platform that somehow started outside its own range settles into it instead of running off.
        if (y >= maxY) { y = maxY; direction = -1; }
        else if (y <= minY) { y = minY; direction = 1; }

        float moved = y - pos.y;
        transform.position = new Vector3(pos.x, y, pos.z);
        if (moved == 0f || target == null) return;

        // Standing on it? Take them along. The test is against where the platform WAS, not where it
        // has just moved to: a fast lift on a long frame travels further than the ride box is wide,
        // and testing after the move would have it step out from under a rider who was squarely on
        // it and drop them. The grid is told separately — it has to know this move was the floor
        // moving rather than the player walking, or it would reject it as a step onto a tile they
        // had already painted.
        Vector3 d = target.position - pos;
        if (Mathf.Abs(d.x) > rideHalfX || Mathf.Abs(d.y) > rideHalfY) return;

        var lift = new Vector3(0f, moved, 0f);
        target.position += lift;
        if (grid != null) grid.CarryPlayer(lift);
    }
}
