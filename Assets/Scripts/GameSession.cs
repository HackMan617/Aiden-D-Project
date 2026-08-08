using UnityEngine;
using UnityEngine.SceneManagement;

// What SampleScene should do when it loads. The scene hosts both the game and the level editor —
// the editor needs the very same tile / marker / obstacle / sawblade art that LevelGrid already
// carries, so giving it its own scene would mean maintaining a second copy of all that wiring.
// Instead this static decides which of the two builds itself on load:
//
//   Mode.Play + CustomLevel == null -> the normal numbered maze (GameProgress.CurrentLevel)
//   Mode.Play + CustomLevel != null -> a player-designed level, built by LevelGrid from the data
//   Mode.Edit                       -> LevelEditor takes over; nothing gameplay-side is built
//
// Like GameProgress this is plain static state: it survives scene loads within a play session and
// resets on a fresh launch, which is exactly the lifetime these flags need.
public static class GameSession
{
    public enum Mode { Play, Edit }

    const string GameSceneName = "SampleScene";

    public static Mode Current = Mode.Play;

    // The designed level currently being played, or null for the normal numbered progression.
    public static LevelData CustomLevel;

    // The level the editor should reopen with (its unsaved working copy), so a test play can hand
    // the player straight back to what they were building.
    public static LevelData EditorDraft;

    // True while test-playing from the editor: the win / retry screens then offer a way back to
    // the editor instead of advancing the numbered progression.
    public static bool ReturnToEditorOnExit;

    public static bool IsEditing => Current == Mode.Edit;

    // Open the editor, optionally on a specific level. Reloads the game scene so LevelGrid and the
    // level-select picker both see the new mode in their own Start().
    public static void EnterEditor(LevelData draft = null)
    {
        Current = Mode.Edit;
        CustomLevel = null;
        ReturnToEditorOnExit = false;
        if (draft != null) EditorDraft = draft;
        GoToGameScene();
    }

    // Play a designed level. `draft` is what the editor should come back to afterwards; pass null
    // when the level is being played for its own sake rather than test-played.
    public static void PlayCustom(LevelData level, LevelData draft)
    {
        if (level == null) return;
        Current = Mode.Play;
        CustomLevel = level;
        EditorDraft = draft;
        ReturnToEditorOnExit = draft != null;
        LevelSelectManager.AutoStart = true; // skip the picker — drop straight into the level
        GoToGameScene();
    }

    // Leave the editor (or a custom level) and return to the normal level-select screen.
    public static void ExitToLevelSelect()
    {
        Current = Mode.Play;
        CustomLevel = null;
        EditorDraft = null;
        ReturnToEditorOnExit = false;
        LevelSelectManager.AutoStart = false; // show the picker again
        GameProgress.Reset();
        GoToGameScene();
    }

    static void GoToGameScene()
    {
        Time.timeScale = 1f; // timeScale survives scene loads, so always restore it before loading
        SceneManager.LoadScene(GameSceneName);
    }
}
