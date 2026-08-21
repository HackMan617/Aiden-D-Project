using System;
using System.Collections.Generic;
using UnityEngine;

// What a single cell of a designed level holds. Stored as plain ints inside LevelData.cells so
// JsonUtility can round-trip the whole board as one flat array.
// Values are the numbers written into the save files, so new kinds are only ever appended —
// renumbering one would silently reinterpret every level already on disk.
public enum LevelTile
{
    Floor = 0,   // ordinary reactive tile — walkable, and paints red behind the player
    Wall = 1,    // solid from the start; the player can never enter it
    Hazard = 2,  // animated obstacle — touching it is game over
    Brick = 3,   // solid too, but drawn with the "aiden d wall" block instead of the red trail tile
    Water = 4,   // animated pool — the player drowns on contact
}

// A player-designed level, as authored by the in-game editor (LevelEditor), written to disk by
// LevelStore, and rebuilt into a real maze by LevelGrid.
//
// Every field is a type JsonUtility understands (no dictionaries, no jagged arrays) so the whole
// level is one small JSON file. Anything read back from disk goes through Validate() first, so a
// truncated or hand-edited file degrades into a playable level instead of throwing.
[Serializable]
public class LevelData
{
    public const int MinWidth = 5, MinHeight = 4;
    // The camera framing and the built-in level 1 are both tuned for a 15x9 board, so that is also
    // the ceiling for designed levels — anything larger would spill outside the view.
    public const int MaxWidth = 15, MaxHeight = 9;

    public const float MinSawSpeed = 0.5f, MaxSawSpeed = 8f;
    public const float MinSawInterval = 0.5f, MaxSawInterval = 8f;

    public string levelName = "New Level";
    public int width = MaxWidth;
    public int height = MaxHeight;

    // Flat cell grid indexed [x * height + y] — the same cell keying LevelGrid already uses.
    public int[] cells;

    // Where the player spawns and where the win tile sits. Kept out of `cells` because they are
    // single cells rather than a paintable material, and both always sit on floor.
    public int startX, startY;
    public int endX, endY;

    // Vertical sawblades sweep along a whole row, so a blade "lane" is just a row index. Speed and
    // spawn interval are per-level; the editor exposes both as sliders. An empty list means the
    // level has no blades at all.
    public List<int> sawRows = new List<int>();
    public float sawSpeed = 2f;
    public float sawInterval = 3f;

    // A blank full-size board: all floor, start bottom-left, goal top-right.
    public static LevelData CreateDefault()
    {
        var d = new LevelData();
        d.cells = new int[d.width * d.height];
        d.startX = 0;
        d.startY = 0;
        d.endX = d.width - 1;
        d.endY = d.height - 1;
        return d;
    }

    public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    public LevelTile Get(int x, int y) =>
        (cells != null && InBounds(x, y)) ? (LevelTile)cells[x * height + y] : LevelTile.Floor;

    public void Set(int x, int y, LevelTile tile)
    {
        if (cells == null || !InBounds(x, y)) return;
        cells[x * height + y] = (int)tile;
    }

    public bool IsStart(int x, int y) => x == startX && y == startY;
    public bool IsGoal(int x, int y) => x == endX && y == endY;

    // Solid from the moment the level is built: the player bumps off these rather than dying.
    public static bool IsSolid(LevelTile t) => t == LevelTile.Wall || t == LevelTile.Brick;

    // Deadly on contact: stepping onto one of these ends the run.
    public static bool IsDeadly(LevelTile t) => t == LevelTile.Hazard || t == LevelTile.Water;

    // True where the player can never stand: solid walls and deadly obstacles alike.
    public bool IsBlocked(int x, int y)
    {
        LevelTile t = Get(x, y);
        return IsSolid(t) || IsDeadly(t);
    }

    // Grow or shrink the board, keeping everything that still fits inside the new bounds. Markers
    // and blade lanes that fall outside are pulled back in so the level stays playable.
    public void Resize(int newWidth, int newHeight)
    {
        newWidth = Mathf.Clamp(newWidth, MinWidth, MaxWidth);
        newHeight = Mathf.Clamp(newHeight, MinHeight, MaxHeight);
        if (newWidth == width && newHeight == height && cells != null) return;

        var next = new int[newWidth * newHeight];
        if (cells != null)
        {
            int cw = Mathf.Min(width, newWidth), ch = Mathf.Min(height, newHeight);
            for (int x = 0; x < cw; x++)
                for (int y = 0; y < ch; y++)
                    next[x * newHeight + y] = cells[x * height + y];
        }

        width = newWidth;
        height = newHeight;
        cells = next;
        Validate();
    }

    // Repairs anything out of range — used after a resize and after every load from disk, so a
    // corrupt or hand-edited file can never produce an unbuildable level.
    public void Validate()
    {
        width = Mathf.Clamp(width, MinWidth, MaxWidth);
        height = Mathf.Clamp(height, MinHeight, MaxHeight);

        if (cells == null || cells.Length != width * height)
        {
            var repaired = new int[width * height];
            if (cells != null) Array.Copy(cells, repaired, Mathf.Min(cells.Length, repaired.Length));
            cells = repaired;
        }
        for (int i = 0; i < cells.Length; i++)
            if (cells[i] < 0 || cells[i] > (int)LevelTile.Water) cells[i] = (int)LevelTile.Floor;

        startX = Mathf.Clamp(startX, 0, width - 1);
        startY = Mathf.Clamp(startY, 0, height - 1);
        endX = Mathf.Clamp(endX, 0, width - 1);
        endY = Mathf.Clamp(endY, 0, height - 1);
        // A level whose goal sits on the spawn would be won on frame one — push the goal away.
        if (startX == endX && startY == endY)
        {
            if (endX < width - 1) endX++;
            else if (endX > 0) endX--;
            else if (endY < height - 1) endY++;
            else endY--;
        }
        // Both markers always sit on plain floor: a wall there is unenterable and a hazard there
        // would kill the player on spawn.
        Set(startX, startY, LevelTile.Floor);
        Set(endX, endY, LevelTile.Floor);

        if (sawRows == null) sawRows = new List<int>();
        sawRows.RemoveAll(r => r < 0 || r >= height);
        for (int i = sawRows.Count - 1; i > 0; i--)
            if (sawRows.IndexOf(sawRows[i]) != i) sawRows.RemoveAt(i); // drop duplicate lanes
        sawRows.Sort();

        sawSpeed = Mathf.Clamp(sawSpeed, MinSawSpeed, MaxSawSpeed);
        sawInterval = Mathf.Clamp(sawInterval, MinSawInterval, MaxSawInterval);

        if (string.IsNullOrWhiteSpace(levelName)) levelName = "New Level";
    }

    // Can the player actually walk from the spawn to the win tile? Flood-fills the four-way
    // neighbourhood, treating walls and hazards alike as impassable (stepping on a hazard ends the
    // run, so a "path" through one is no path at all). The red trail the player leaves can still
    // strand them, but that's their move to plan — this only rules out levels nobody could finish.
    public bool GoalIsReachable()
    {
        var seen = new bool[width, height];
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        seen[startX, startY] = true;

        var steps = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            if (c.x == endX && c.y == endY) return true;
            foreach (Vector2Int step in steps)
            {
                Vector2Int n = c + step;
                if (!InBounds(n.x, n.y) || seen[n.x, n.y] || IsBlocked(n.x, n.y)) continue;
                seen[n.x, n.y] = true;
                queue.Enqueue(n);
            }
        }
        return false;
    }

    // A detached copy. Used to hand the editor's working level to the game for a test play without
    // the two sharing a reference (edits made after starting the test must not leak into it).
    public LevelData Clone()
    {
        var copy = JsonUtility.FromJson<LevelData>(JsonUtility.ToJson(this));
        copy.Validate();
        return copy;
    }
}
