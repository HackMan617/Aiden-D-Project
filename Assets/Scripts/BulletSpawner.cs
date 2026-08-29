using UnityEngine;

// The turret behind the level editor's Spawner tool. It sits on a solid cell — the player bumps off
// it like any wall — and fires a bullet along its facing every `interval` seconds for as long as the
// level is running.
//
// Which cells hold a turret and which way each one points is painted in the editor and stored in the
// level's own tile grid (LevelTile.SpawnerRight and its three siblings); how fast and how often they
// all fire is one setting for the whole level, the same way the sawblades' speed is. LevelGrid reads
// both back and configures each turret it builds.
public class BulletSpawner : MonoBehaviour
{
    [Tooltip("The grid this turret stands on. Handed to every bullet so it knows where to stop.")]
    public LevelGrid grid;
    [Tooltip("The player transform the bullets can hit.")]
    public Transform target;
    [Tooltip("Unit direction the turret fires in.")]
    public Vector2 direction = Vector2.right;

    [Tooltip("Animation frames for the bullets this turret fires.")]
    public Sprite[] frames;
    [Tooltip("Bullet animation speed (frames per second).")]
    public float fps = 10f;

    [Tooltip("Bullet travel speed in world units per second.")]
    public float speed = 3.5f;
    [Tooltip("Seconds between shots. The first shot of a level comes one full interval in, which " +
             "doubles as the player's grace period.")]
    public float interval = 2f;

    [Tooltip("One grid cell in world units — sets how big a bullet is and how far clear of the " +
             "turret it appears.")]
    public float cellSize = 1f;
    [Tooltip("Bullet height as a fraction of a cell.")]
    public float bulletCellHeight = 0.5f;
    [Tooltip("Sorting order for the bullets — above the tiles and the player, like the sawblades.")]
    public int sortingOrder = 10;

    float timer;

    void Update()
    {
        var over = GameOverManager.Instance;
        if (over != null && over.HasEnded) return;
        if (frames == null || frames.Length == 0 || interval <= 0f) return;

        // Scaled time, so a turret stops counting down with everything else while the game is
        // paused or the level-select freeze is holding timeScale at zero.
        timer += Time.deltaTime;
        if (timer < interval) return;
        timer -= interval;
        Fire();
    }

    void Fire()
    {
        // Clear of the turret's own cell, so a bullet is never born underneath the thing firing it.
        Vector3 origin = transform.position + (Vector3)(direction * (cellSize * 0.6f));

        var go = new GameObject("SpawnerBullet");
        // Parented to the grid rather than to the turret: the turret carries the scale that fits it
        // to a cell, and a bullet inheriting that would come out the wrong size.
        go.transform.SetParent(transform.parent, false);
        go.transform.position = origin;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = frames[0];
        sr.color = Color.white;
        sr.sortingOrder = sortingOrder;

        var flip = go.AddComponent<SpriteFlipbook>();
        flip.frames = frames;
        flip.fps = fps;

        // Scale to bulletCellHeight cells tall (uniform, so the art keeps its proportions)...
        Vector2 size = frames[0].bounds.size;
        if (size.y > 0f)
        {
            float k = (cellSize * bulletCellHeight) / size.y;
            go.transform.localScale = new Vector3(k, k, 1f);
        }
        // ...then turn it to face the way it flies. The art is drawn nose-right, so the angle is
        // measured from +x.
        go.transform.rotation = Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.right, direction));

        var bullet = go.AddComponent<SpawnerBullet>();
        bullet.grid = grid;
        bullet.target = target;
        bullet.direction = direction;
        bullet.speed = speed;
        bullet.hitRadius = 0.3f * cellSize;
    }
}
