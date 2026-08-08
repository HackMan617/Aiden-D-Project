using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Reactive tile grid for the level.
//
// Each tile starts on the grey frame of the tile sheet. The first time the player steps on a
// cell it plays a short flash animation (grey -> white -> red) through the sheet's frames and
// then stays red forever — the player leaves a lasting red trail. Painted tiles also become
// WALLS: the player may stay on the tile it is currently on, but can never move back onto a red
// one, so its own trail boxes it in. At startup the level is populated on random, non-overlapping
// cells with: a start marker (player spawns here), an end marker, and animated obstacles.
public class LevelGrid : MonoBehaviour
{
    [Header("Grid size")]
    public int width = 15;
    public int height = 9;
    public float cellSize = 1f;

    [Header("References")]
    [Tooltip("Transform that heats tiles and spawns on the start marker. Auto-finds 'Player' if empty.")]
    public Transform player;
    [Tooltip("Fallback base tile sprite, used only if no Tile Sheet is assigned.")]
    public Sprite tileSprite;

    [Header("Tiles")]
    [Tooltip("Tile state sheet: frame 0 = grey (start); stepping on a tile plays grey -> white -> red " +
             "through its 16x16 frames. The 224x16 sheet holds the 7-state cycle twice.")]
    public Texture2D tileSheet;
    [Tooltip("Seconds each frame of the step-on flash-to-red animation is shown.")]
    public float tileAnimFrameTime = 0.06f;

    [Header("Markers")]
    [Tooltip("Sprite for the start cell (player spawns here).")]
    public Sprite startMarkerSprite;
    [Tooltip("Sprite for the end-of-level cell.")]
    public Sprite endMarkerSprite;

    [Header("Obstacles")]
    [Tooltip("Animation frames cycled on each obstacle.")]
    public Sprite[] obstacleFrames;
    [Tooltip("How many obstacles to scatter on random cells.")]
    public int obstacleCount = 6;
    [Tooltip("Obstacle animation speed (frames per second).")]
    public float obstacleFps = 6f;

    [Header("Level 2 (tighter designed arena)")]
    [Tooltip("Grid width used for the designed level 2 (smaller than level 1 so it feels tighter).")]
    public int level2Width = 10;
    [Tooltip("Grid height used for the designed level 2.")]
    public int level2Height = 6;
    [Tooltip("Animated obstacles (the 'unnamed 2' sheet, shared with obstacleFrames) scattered " +
             "through the designed level 2 between the start and the win tile.")]
    public int level2ObstacleCount = 7;

    [Header("Sawblades (moving hazards)")]
    [Tooltip("The 'vertical sawblade' sheet (128x64 = 2 frames of 64x64). Leave empty to disable.")]
    public Texture2D sawbladeSheet;
    [Tooltip("How many equal-width frames the sawblade sheet holds.")]
    public int sawbladeFrameCount = 2;
    [Tooltip("Sawblade spin speed (frames per second).")]
    public float sawbladeFps = 12f;
    [Tooltip("Blade size in cells (uniform scale, so this also sets the width). >1 makes the thin " +
             "vertical blade easier to see as it approaches.")]
    public float sawbladeCellHeight = 1.5f;
    [Tooltip("Sorting order for sawblades — above the tiles and the player so they visibly sweep over.")]
    public int sawbladeSortingOrder = 10;

    [Header("Sawblade difficulty (scales with GameProgress.CurrentLevel; level 1 = easiest)")]
    [Tooltip("Blade travel speed at level 1 (world units/sec). Kept well below the player's speed so " +
             "the first level's blades are easy to dodge.")]
    public float sawbladeBaseSpeed = 1.6f;
    [Tooltip("Extra speed added per level beyond the first.")]
    public float sawbladeSpeedPerLevel = 0.55f;
    [Tooltip("Speed is never faster than this.")]
    public float sawbladeMaxSpeed = 8f;
    [Tooltip("Seconds between blade spawns at level 1 (large = rare).")]
    public float sawbladeBaseInterval = 5f;
    [Tooltip("Interval shrinks by this many seconds per level (blades get more frequent).")]
    public float sawbladeIntervalPerLevel = 0.5f;
    [Tooltip("Interval never drops below this.")]
    public float sawbladeMinInterval = 0.8f;
    [Tooltip("Grace period before the very first blade of a level appears.")]
    public float sawbladeFirstDelay = 2.5f;

    [Header("Look")]
    public Color coolColor = Color.white;
    [Tooltip("Color a tile is permanently painted once the player steps on it.")]
    public Color hotColor = new Color(0.95f, 0.15f, 0.15f, 1f);
    [Tooltip("Sorting order for the tiles (keep below the player's so the player draws on top).")]
    public int sortingOrder = -10;
    [Tooltip("Clamp the player inside the grid so it can't walk off the edge (bumps the border).")]
    public bool clampPlayerToGrid = true;

    SpriteRenderer[,] tiles;
    Vector3 origin;
    readonly HashSet<int> usedCells = new HashSet<int>();
    readonly HashSet<Vector2Int> obstacleCells = new HashSet<Vector2Int>(); // hazard cells = game over on contact

    bool[,] painted;        // true where a tile is red — a wall the player may not re-enter
    Vector2Int currentCell; // the cell the player currently occupies (it may stay on its own red tile)
    Vector3 lastValidPos;   // last allowed player position; blocked moves revert to it
    bool playerTracked;     // whether currentCell / lastValidPos have been initialised yet
    Sprite[] tileFrames;    // grey -> white -> red frames sliced from tileSheet at runtime
    Vector2Int goalCell;    // the end-marker cell — reaching it wins the maze
    bool hasGoal;           // whether the goal cell has been placed
    Sprite[] sawbladeFrames; // spin frames sliced from sawbladeSheet at runtime

    LevelData customLevel;  // the player-designed level being built, or null for a numbered maze
    int customLaneIndex;    // round-robin cursor over the designed level's blade lanes

    // The runtime-sliced sheets, built on first use. The level editor borrows these for its palette
    // and board so it paints with exactly the art the game plays with, and it needs them even
    // though Start() never builds a grid in edit mode.
    public Sprite[] TileFrames { get { if (tileFrames == null) BuildTileFrames(); return tileFrames; } }
    public Sprite[] SawbladeFrames { get { if (sawbladeFrames == null) BuildSawbladeFrames(); return sawbladeFrames; } }

    void Start()
    {
        // This scene doubles as the host for the level editor so the editor can reuse the art
        // assigned here. In edit mode no maze is built at all: `tiles` stays null, which makes
        // LateUpdate a no-op, and LevelEditor takes the screen over.
        if (GameSession.IsEditing)
        {
            gameObject.AddComponent<LevelEditor>();
            return;
        }

        if (player == null)
        {
            GameObject p = GameObject.Find("Player");
            if (p != null) player = p.transform;
        }

        // A player-designed level carries its own board size; otherwise level 2 is a tighter,
        // hand-designed arena — shrink the grid before it is built so there's less room to dodge.
        // Every other level keeps the inspector-configured size.
        customLevel = GameSession.CustomLevel;
        if (customLevel != null) { width = customLevel.width; height = customLevel.height; }
        else if (GameProgress.CurrentLevel == 2) { width = level2Width; height = level2Height; }

        BuildTileFrames();
        BuildSawbladeFrames();
        BuildGrid();
        PopulateLevel();

        // Moving sawblade hazards — start the spawner if a sheet was assigned. The coroutine uses
        // scaled WaitForSeconds, so it stays paused while the level-select freeze holds timeScale
        // at 0 and only begins once the player actually starts the level. A designed level with no
        // blade lanes painted gets no blades at all.
        bool wantsBlades = customLevel == null || customLevel.sawRows.Count > 0;
        if (sawbladeFrames != null && sawbladeFrames.Length > 0 && wantsBlades)
            StartCoroutine(SpawnSawblades());
    }

    // Slice the tile sheet into the frames of one grey -> white -> red cycle. The 224x16 sheet
    // repeats this cycle twice, so we take the first half. Built at runtime via Sprite.Create —
    // no asset re-slicing needed.
    void BuildTileFrames()
    {
        if (tileSheet == null) return;
        int frame = tileSheet.height;        // square frames (16x16)
        if (frame <= 0) return;
        int total = tileSheet.width / frame; // 14
        int n = Mathf.Max(1, total / 2);     // 7 — the sheet holds the cycle twice
        tileFrames = new Sprite[n];
        for (int i = 0; i < n; i++)
            tileFrames[i] = Sprite.Create(tileSheet, new Rect(i * frame, 0f, frame, frame),
                                          new Vector2(0.5f, 0.5f), frame);
    }

    // Slice the sawblade sheet into its equal-width spin frames (128x64 -> 2 x 64x64).
    void BuildSawbladeFrames()
    {
        if (sawbladeSheet == null) return;
        int n = Mathf.Max(1, sawbladeFrameCount);
        float fw = sawbladeSheet.width / (float)n;
        float fh = sawbladeSheet.height;
        if (fw <= 0f || fh <= 0f) return;
        sawbladeFrames = new Sprite[n];
        for (int i = 0; i < n; i++)
            sawbladeFrames[i] = Sprite.Create(sawbladeSheet, new Rect(i * fw, 0f, fw, fh),
                                              new Vector2(0.5f, 0.5f), fh);
    }

    // Continuously spawns sawblades that sweep left -> right across random rows. Speed and spawn
    // frequency both scale with the current level, so level 1 gets a rare, slow blade and later
    // levels get fast, frequent ones. WaitForSeconds is scaled time, so this naturally pauses with
    // the game (level-select freeze, pause menu, game-over / win).
    IEnumerator SpawnSawblades()
    {
        float speed, interval;
        if (customLevel != null)
        {
            // A designed level sets its own blade speed and spacing in the editor, so the per-level
            // difficulty curve doesn't apply — the designer's numbers are the difficulty.
            speed = customLevel.sawSpeed;
            interval = customLevel.sawInterval;
        }
        else
        {
            int level = Mathf.Max(1, GameProgress.CurrentLevel);
            speed = Mathf.Min(sawbladeMaxSpeed, sawbladeBaseSpeed + (level - 1) * sawbladeSpeedPerLevel);
            interval = Mathf.Max(sawbladeMinInterval, sawbladeBaseInterval - (level - 1) * sawbladeIntervalPerLevel);
        }

        yield return new WaitForSeconds(sawbladeFirstDelay);
        while (true)
        {
            if (GameOverManager.Instance == null || !GameOverManager.Instance.HasEnded)
                SpawnOneSawblade(speed);
            yield return new WaitForSeconds(interval);
        }
    }

    // Spawn a single blade just off the left edge on a random row, moving right past the far edge.
    void SpawnOneSawblade(float speed)
    {
        if (sawbladeFrames == null || sawbladeFrames.Length == 0) return;

        // Procedural levels drop blades on any row; a designed level cycles through exactly the
        // lanes the player painted, so every blade they placed reliably shows up in rotation.
        int row;
        if (customLevel != null)
        {
            if (customLevel.sawRows.Count == 0) return;
            row = customLevel.sawRows[customLaneIndex % customLevel.sawRows.Count];
            customLaneIndex++;
        }
        else row = Random.Range(0, height);

        float y = origin.y + row * cellSize;
        float startX = origin.x - cellSize;         // just off the left edge
        float endX = origin.x + width * cellSize;   // just past the right edge

        GameObject go = new GameObject("Sawblade");
        go.transform.SetParent(transform, false);
        go.transform.position = new Vector3(startX, y, 0f);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sawbladeFrames[0];
        sr.color = Color.white;
        sr.sortingOrder = sawbladeSortingOrder;

        // Scale the blade to sawbladeCellHeight cells tall (uniform, keeps its aspect).
        Vector2 size = sawbladeFrames[0].bounds.size;
        if (size.y > 0f)
        {
            float k = (cellSize * sawbladeCellHeight) / size.y;
            go.transform.localScale = new Vector3(k, k, 1f);
        }

        SpriteFlipbook flip = go.AddComponent<SpriteFlipbook>();
        flip.frames = sawbladeFrames;
        flip.fps = sawbladeFps;

        Sawblade saw = go.AddComponent<Sawblade>();
        saw.speed = speed;
        saw.destroyX = endX;
        saw.target = player;
        saw.hitHalfX = 0.22f * cellSize;
        saw.hitHalfY = 0.45f * cellSize * sawbladeCellHeight;
    }

    void BuildGrid()
    {
        tiles = new SpriteRenderer[width, height];
        painted = new bool[width, height];

        bool useFrames = tileFrames != null && tileFrames.Length > 0;
        Sprite baseSprite = useFrames ? tileFrames[0] : tileSprite; // grey start frame

        // Bottom-left cell, positioned so the grid is centred on this object's position.
        origin = transform.position
                 - new Vector3((width - 1) * cellSize, (height - 1) * cellSize, 0f) * 0.5f;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                GameObject go = new GameObject($"Tile_{x}_{y}");
                go.transform.SetParent(transform, false);
                go.transform.position = CellToWorld(x, y);

                SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = baseSprite;
                sr.color = useFrames ? Color.white : coolColor; // frames carry their own color
                sr.sortingOrder = sortingOrder;
                ScaleToCell(sr.transform, baseSprite, true);
                tiles[x, y] = sr;
            }
    }

    void PopulateLevel()
    {
        // A level built in the in-game editor wins outright: it says exactly where everything goes.
        if (customLevel != null)
        {
            PopulateCustomLevel(customLevel);
            return;
        }

        // Level 2 is a hand-designed layout (see the "level 2 concept" art) rather than a random
        // one: the player starts in the bottom-right corner and must reach the win tile in the
        // top-left, dodging the sweeping sawblades (which are faster/more frequent than level 1 via
        // the per-level difficulty scaling in SpawnSawblades). All other levels stay procedural.
        if (GameProgress.CurrentLevel == 2)
        {
            PopulateDesignedLevel(startCell: new Vector2Int(width - 1, 0),
                                  endCell:   new Vector2Int(0, height - 1));
            return;
        }

        // Start marker + player spawn.
        Vector2Int startCell = TakeRandomFreeCell();
        PlaceSprite("StartMarker", startMarkerSprite, startCell, sortingOrder + 1);
        if (player != null)
        {
            Vector3 s = CellToWorld(startCell.x, startCell.y);
            player.position = new Vector3(s.x, s.y, player.position.z);
        }

        // End marker on a different cell — reaching it wins the maze.
        Vector2Int endCell = TakeRandomFreeCell();
        goalCell = endCell;
        hasGoal = true;
        PlaceSprite("EndMarker", endMarkerSprite, endCell, sortingOrder + 1);

        // Animated obstacles on further unique cells.
        SpawnObstacles(obstacleCount);
    }

    // Scatters up to `count` animated obstacles on random free cells. Each is a hazard cell
    // (stepping on it ends the game) that cycles the obstacleFrames sheet via a SpriteFlipbook.
    // Shared by the procedural levels and the designed level 2.
    void SpawnObstacles(int count)
    {
        if (obstacleFrames == null || obstacleFrames.Length == 0) return;

        int free = width * height - usedCells.Count;
        int n = Mathf.Clamp(count, 0, free);
        for (int i = 0; i < n; i++)
        {
            Vector2Int c = TakeRandomFreeCell();
            obstacleCells.Add(c); // stepping on this cell ends the game
            GameObject obs = PlaceSprite($"Obstacle_{i}", obstacleFrames[0], c, sortingOrder + 1);
            if (obs != null)
            {
                SpriteFlipbook flip = obs.AddComponent<SpriteFlipbook>();
                flip.frames = obstacleFrames;
                flip.fps = obstacleFps;
            }
        }
    }

    // Places the start marker (and spawns the player on it) at the bottom-right and the win-tile end
    // marker at the top-left for the hand-designed level 2, then scatters animated obstacles between
    // them. Obstacles are hazard cells (deadly to touch) rather than walls, so the corner-to-corner
    // route always stays open. The tile-painting, flash-to-red trail, and sprite animation all
    // behave exactly as in the procedural levels — this only fixes WHERE start/goal sit and adds the
    // obstacles; it doesn't change how the grid works.
    void PopulateDesignedLevel(Vector2Int startCell, Vector2Int endCell)
    {
        // Reserve both cells so nothing else lands on them, plus the cells directly next to them so
        // a random obstacle can never seal the player into the start corner or block the win tile.
        usedCells.Add(startCell.x * height + startCell.y);
        usedCells.Add(endCell.x * height + endCell.y);
        ReserveNeighbors(startCell);
        ReserveNeighbors(endCell);

        PlaceSprite("StartMarker", startMarkerSprite, startCell, sortingOrder + 1);
        if (player != null)
        {
            Vector3 s = CellToWorld(startCell.x, startCell.y);
            player.position = new Vector3(s.x, s.y, player.position.z);
        }

        goalCell = endCell;
        hasGoal = true;
        // endMarkerSprite is the "win tile" art (assigned in the scene), so the goal shows the win tile.
        PlaceSprite("EndMarker", endMarkerSprite, endCell, sortingOrder + 1);

        // Additional animated obstacles (the "unnamed 2" dome sheet) scattered between the corners.
        // They're hazard cells, not walls, so the bottom-right -> top-left route always stays open;
        // combined with the tighter grid and the faster sweeping blades they make level 2 harder.
        SpawnObstacles(level2ObstacleCount);
    }

    // Builds a level authored in the in-game editor. Nothing here is random: every wall, hazard,
    // marker and blade lane is placed exactly where the designer painted it.
    //
    // Walls reuse the existing red-tile machinery — a wall is simply a cell that starts out already
    // painted, so the same movement rule that stops the player re-entering their own trail also
    // stops them walking into a wall, with no new collision code. Hazards are the same deadly cells
    // the procedural levels scatter, and blade lanes are read back in SpawnOneSawblade.
    void PopulateCustomLevel(LevelData data)
    {
        bool hasWallSprite = tileFrames != null && tileFrames.Length > 0;

        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                switch (data.Get(x, y))
                {
                    case LevelTile.Wall:
                        painted[x, y] = true; // already "trail", so the player can never enter it
                        if (hasWallSprite) tiles[x, y].sprite = tileFrames[tileFrames.Length - 1];
                        else tiles[x, y].color = hotColor;
                        break;

                    case LevelTile.Hazard:
                        obstacleCells.Add(new Vector2Int(x, y));
                        if (obstacleFrames != null && obstacleFrames.Length > 0)
                        {
                            GameObject obs = PlaceSprite($"Obstacle_{x}_{y}", obstacleFrames[0],
                                                         new Vector2Int(x, y), sortingOrder + 1);
                            if (obs != null)
                            {
                                SpriteFlipbook flip = obs.AddComponent<SpriteFlipbook>();
                                flip.frames = obstacleFrames;
                                flip.fps = obstacleFps;
                            }
                        }
                        break;
                }
            }

        var startCell = new Vector2Int(data.startX, data.startY);
        PlaceSprite("StartMarker", startMarkerSprite, startCell, sortingOrder + 1);
        if (player != null)
        {
            Vector3 s = CellToWorld(startCell.x, startCell.y);
            player.position = new Vector3(s.x, s.y, player.position.z);
        }

        goalCell = new Vector2Int(data.endX, data.endY);
        hasGoal = true;
        PlaceSprite("EndMarker", endMarkerSprite, goalCell, sortingOrder + 1);
    }

    // Marks the four orthogonal neighbours of a cell as used (in-bounds only) so obstacle placement
    // skips them — used to keep the immediate area around the start and goal clear.
    void ReserveNeighbors(Vector2Int c)
    {
        ReserveCell(new Vector2Int(c.x + 1, c.y));
        ReserveCell(new Vector2Int(c.x - 1, c.y));
        ReserveCell(new Vector2Int(c.x, c.y + 1));
        ReserveCell(new Vector2Int(c.x, c.y - 1));
    }

    void ReserveCell(Vector2Int c)
    {
        if (InBounds(c)) usedCells.Add(c.x * height + c.y);
    }

    // Runs after PlayerController.Update has moved the player. Order matters: clamp inside the
    // grid, block movement onto red tiles, then paint the tile the player ended up on.
    void LateUpdate()
    {
        if (tiles == null || player == null) return;
        if (GameOverManager.Instance != null && GameOverManager.Instance.HasEnded) return;

        // First frame (and after a scene reload): latch where the player started.
        if (!playerTracked)
        {
            lastValidPos = player.position;
            currentCell = WorldToCell(player.position);
            playerTracked = true;
        }

        Vector3 now = player.position; // where PlayerController just moved the player to

        // 1. Keep the player inside the grid (its edge bumps the border).
        if (clampPlayerToGrid)
        {
            float half = cellSize * 0.5f;
            float minX = origin.x - half, maxX = origin.x + (width - 1) * cellSize + half;
            float minY = origin.y - half, maxY = origin.y + (height - 1) * cellSize + half;
            Vector3 ext = Vector3.zero;
            SpriteRenderer psr = player.GetComponent<SpriteRenderer>();
            if (psr != null) ext = psr.bounds.extents;
            now.x = Mathf.Clamp(now.x, minX + ext.x, maxX - ext.x);
            now.y = Mathf.Clamp(now.y, minY + ext.y, maxY - ext.y);
        }

        // 2. Block movement onto already-red tiles (per-axis so the player slides along them).
        now = ResolveRedBlocking(now);

        player.position = now;
        lastValidPos = now;

        // 3. Resolve the cell the player ended up in.
        Vector2Int c = WorldToCell(now);
        if (!InBounds(c)) return;
        currentCell = c;

        // 4. Hazard? End the game.
        if (obstacleCells.Contains(c) && GameOverManager.Instance != null)
        {
            GameOverManager.Instance.TriggerGameOver();
            return;
        }

        // 5. Reached the end marker? Win the maze.
        if (hasGoal && c == goalCell && GameOverManager.Instance != null)
        {
            GameOverManager.Instance.TriggerWin();
            return;
        }

        // 6. Paint the tile red — from now on it is a wall the player can't re-enter.
        Paint(c);
    }

    // Per-axis movement validation: a move is allowed only if its destination cell is the one
    // the player already occupies (so it can move freely on its own tile) or a tile that hasn't
    // been painted red yet. Blocked axes revert to the last valid position, which lets the
    // player slide along its trail instead of sticking to it.
    Vector3 ResolveRedBlocking(Vector3 now)
    {
        Vector3 prev = lastValidPos;
        Vector3 result = prev;

        Vector2Int cx = WorldToCell(new Vector3(now.x, prev.y, prev.z));
        if (InBounds(cx) && (cx == currentCell || !painted[cx.x, cx.y]))
            result.x = now.x;

        Vector2Int cy = WorldToCell(new Vector3(result.x, now.y, prev.z));
        if (InBounds(cy) && (cy == currentCell || !painted[cy.x, cy.y]))
            result.y = now.y;

        result.z = now.z;
        return result;
    }

    // Permanently paint a cell red. Idempotent — once painted it stays a wall.
    void Paint(Vector2Int c)
    {
        if (!InBounds(c) || painted[c.x, c.y]) return;
        painted[c.x, c.y] = true; // becomes a wall immediately; the flash is purely visual

        if (tileFrames != null && tileFrames.Length > 1)
            StartCoroutine(FlashToRed(tiles[c.x, c.y]));
        else
            tiles[c.x, c.y].color = hotColor; // fallback when no tile sheet is assigned
    }

    // Play the tile sheet's flash frames once (white -> ... -> red), then rest on the red frame.
    IEnumerator FlashToRed(SpriteRenderer sr)
    {
        for (int i = 1; i < tileFrames.Length; i++)
        {
            sr.sprite = tileFrames[i];
            yield return new WaitForSeconds(tileAnimFrameTime);
        }
    }

    // Picks a random cell not already used, marks it used, and returns it.
    Vector2Int TakeRandomFreeCell()
    {
        // Safe because callers stay well under width*height.
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            int x = Random.Range(0, width);
            int y = Random.Range(0, height);
            int key = x * height + y;
            if (usedCells.Add(key)) return new Vector2Int(x, y);
        }
        return Vector2Int.zero;
    }

    GameObject PlaceSprite(string name, Sprite sprite, Vector2Int cell, int order)
    {
        if (sprite == null) return null;
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.position = CellToWorld(cell.x, cell.y);

        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = Color.white;
        sr.sortingOrder = order;
        ScaleToCell(go.transform, sprite, false); // uniform, keep aspect
        return go;
    }

    Vector3 CellToWorld(int x, int y) => origin + new Vector3(x * cellSize, y * cellSize, 0f);

    Vector2Int WorldToCell(Vector3 world)
    {
        Vector3 local = world - origin;
        return new Vector2Int(
            Mathf.RoundToInt(local.x / cellSize),
            Mathf.RoundToInt(local.y / cellSize));
    }

    bool InBounds(Vector2Int c) => c.x >= 0 && c.x < width && c.y >= 0 && c.y < height;

    // Scale a sprite transform to one cell. fill=true stretches to fill the cell exactly;
    // fill=false scales uniformly so the sprite fits within the cell (keeps aspect ratio).
    void ScaleToCell(Transform t, Sprite sprite, bool fill)
    {
        if (sprite == null) return;
        Vector2 size = sprite.bounds.size;
        if (size.x <= 0f || size.y <= 0f) return;

        if (fill)
            t.localScale = new Vector3(cellSize / size.x, cellSize / size.y, 1f);
        else
        {
            float s = cellSize / Mathf.Max(size.x, size.y);
            t.localScale = new Vector3(s, s, 1f);
        }
    }
}
