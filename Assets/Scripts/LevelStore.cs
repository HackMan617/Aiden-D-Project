using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

// On-disk library of player-designed levels. Each level is one JSON file named after the level,
// living in <persistentDataPath>/CustomLevels — a writable location on every platform, so saved
// levels survive quitting the game and are not bundled into the build.
//
// The editor calls Save/Load/Delete/List; nothing else in the game touches the filesystem.
public static class LevelStore
{
    const string FolderName = "CustomLevels";
    const string Extension = ".json";
    public const int MaxNameLength = 28;

    public static string Folder => Path.Combine(Application.persistentDataPath, FolderName);

    // Every saved level's name, alphabetically. Returns an empty list (never null) if nothing has
    // been saved yet or the folder can't be read.
    public static List<string> ListLevels()
    {
        var names = new List<string>();
        try
        {
            if (!Directory.Exists(Folder)) return names;
            foreach (string file in Directory.GetFiles(Folder, "*" + Extension))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[LevelStore] Could not list levels: {e.Message}");
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static bool Exists(string levelName)
    {
        string safe = Sanitize(levelName);
        return safe.Length > 0 && File.Exists(PathFor(safe));
    }

    // Writes the level under its own (sanitized) name, overwriting any earlier save of that name.
    // Returns false with a player-facing reason in `error` rather than throwing, so the editor can
    // just show the message.
    public static bool Save(LevelData level, out string error)
    {
        error = null;
        if (level == null) { error = "Nothing to save."; return false; }

        string safe = Sanitize(level.levelName);
        if (safe.Length == 0) { error = "Give the level a name first."; return false; }

        level.levelName = safe;
        level.Validate();

        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(PathFor(safe), JsonUtility.ToJson(level, true));
            return true;
        }
        catch (Exception e)
        {
            error = "Could not save: " + e.Message;
            Debug.LogError($"[LevelStore] Save failed for '{safe}': {e}");
            return false;
        }
    }

    // Reads a level back. Returns null when the file is missing or unreadable; anything that does
    // parse is validated, so callers always get a level they can build.
    public static LevelData Load(string levelName)
    {
        string safe = Sanitize(levelName);
        if (safe.Length == 0) return null;

        try
        {
            string path = PathFor(safe);
            if (!File.Exists(path)) return null;

            var level = JsonUtility.FromJson<LevelData>(File.ReadAllText(path));
            if (level == null) return null;
            level.levelName = safe; // the filename is the source of truth for the name
            level.Validate();
            return level;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LevelStore] Load failed for '{safe}': {e}");
            return null;
        }
    }

    public static bool Delete(string levelName)
    {
        string safe = Sanitize(levelName);
        if (safe.Length == 0) return false;
        try
        {
            string path = PathFor(safe);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[LevelStore] Delete failed for '{safe}': {e}");
            return false;
        }
    }

    // The level name doubles as its filename, so keep it to characters that are legal everywhere:
    // letters, digits, space, dash and underscore. Anything else is dropped, runs of spaces are
    // collapsed, and the result is trimmed and length-capped.
    public static string Sanitize(string levelName)
    {
        if (string.IsNullOrEmpty(levelName)) return string.Empty;

        var sb = new StringBuilder(levelName.Length);
        foreach (char c in levelName)
        {
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
            {
                // Collapse runs of whitespace so " a   b " can't become two different filenames.
                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
                sb.Append(c);
            }
        }
        string result = sb.ToString().Trim();
        if (result.Length > MaxNameLength) result = result.Substring(0, MaxNameLength).Trim();
        return result;
    }

    static string PathFor(string safeName) => Path.Combine(Folder, safeName + Extension);
}
