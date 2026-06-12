using System.Collections.Generic;
using UnityEngine;

// Reactive tile grid for the level.
//
// Each tile starts at coolColor (white) and is permanently painted hotColor (red) the
// first time the player steps on its cell — painted tiles never fade back, so the player
// leaves a lasting red trail. At startup the level is populated on random, non-overlapping
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
    [Tooltip("Base tile sprite. It is tinted between coolColor and hotColor.")]
    public Sprite tileSprite;

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

    void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.Find("Player");
            if (p != null) player = p.transform;
        }
        BuildGrid();
        PopulateLevel();
    }

    void BuildGrid()
    {
        tiles = new SpriteRenderer[width, height];

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
                sr.sprite = tileSprite;
                sr.color = coolColor;
                sr.sortingOrder = sortingOrder;
                ScaleToCell(sr.transform, tileSprite, true);
                tiles[x, y] = sr;
            }
    }

    void PopulateLevel()
    {
        // Start marker + player spawn.
        Vector2Int startCell = TakeRandomFreeCell();
        PlaceSprite("StartMarker", startMarkerSprite, startCell, sortingOrder + 1);
        if (player != null)
        {
            Vector3 s = CellToWorld(startCell.x, startCell.y);
            player.position = new Vector3(s.x, s.y, player.position.z);
        }

        // End marker on a different cell.
        Vector2Int endCell = TakeRandomFreeCell();
        PlaceSprite("EndMarker", endMarkerSprite, endCell, sortingOrder + 1);

        // Animated obstacles on further unique cells.
        if (obstacleFrames != null && obstacleFrames.Length > 0)
        {
            int free = width * height - usedCells.Count;
            int n = Mathf.Clamp(obstacleCount, 0, free);
            for (int i = 0; i < n; i++)
            {
                Vector2Int c = TakeRandomFreeCell();
                GameObject obs = PlaceSprite($"Obstacle_{i}", obstacleFrames[0], c, sortingOrder + 1);
                if (obs != null)
                {
                    SpriteFlipbook flip = obs.AddComponent<SpriteFlipbook>();
                    flip.frames = obstacleFrames;
                    flip.fps = obstacleFps;
                }
            }
        }
    }

    void Update()
    {
        if (tiles == null || player == null) return;

        // Permanently paint the tile under the player red. Nothing ever resets a tile's
        // color, so once stepped on it stays hotColor for the rest of the level.
        Vector2Int c = WorldToCell(player.position);
        if (InBounds(c)) tiles[c.x, c.y].color = hotColor;
    }

    // Runs after PlayerController.Update has moved the player: clamp it back inside the
    // grid so it bumps the edges and can't pass through them.
    void LateUpdate()
    {
        if (!clampPlayerToGrid || player == null || tiles == null) return;

        float half = cellSize * 0.5f;
        float minX = origin.x - half;
        float maxX = origin.x + (width - 1) * cellSize + half;
        float minY = origin.y - half;
        float maxY = origin.y + (height - 1) * cellSize + half;

        // Inset by the player's half-size so its edge (not its center) bumps the border.
        Vector3 ext = Vector3.zero;
        SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
        if (sr != null) ext = sr.bounds.extents;

        Vector3 p = player.position;
        p.x = Mathf.Clamp(p.x, minX + ext.x, maxX - ext.x);
        p.y = Mathf.Clamp(p.y, minY + ext.y, maxY - ext.y);
        player.position = p;
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
