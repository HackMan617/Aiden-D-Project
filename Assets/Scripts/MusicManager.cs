using UnityEngine;

// Persistent, app-wide background-music player. It lives on a DontDestroyOnLoad singleton so music
// survives scene loads and reloads, and each part of the game just tells it what should be playing:
//
//   * Main menu     -> "Glossy Marbles"                 (MainMenu.Start, LevelSelectManager's picker)
//   * Gameplay      -> the level track ("i dont KNOW")  (LevelSelectManager, seamless across levels)
//   * Level editor  -> "Construction"                   (LevelEditor.Start)
//   * Pause menu    -> "Marbles Yet To Be Glossed"      (PauseMenu.Pause/Resume)
//
// Two AudioSources are used: `primary` for menu/gameplay music, and `overlay` for the pause track.
// Pausing PAUSES the primary and plays the overlay on top, so resuming continues the gameplay track
// exactly where it left off instead of restarting. Both are 2D sources, so AudioListener.volume (the
// volume slider) and AudioListener.pause (the mute button) still control everything.
public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    // Menu / editor / pause tracks live in Assets/Resources so they load by name without inspector
    // wiring. (The gameplay track is passed in from LevelSelectManager's serialized clip.)
    const string MenuTrack = "Glossy Marbles";
    const string EditorTrack = "Construction";
    const string PauseTrack = "Marbles Yet To Be Glossed";

    AudioSource primary; // menu / gameplay music
    AudioSource overlay; // pause-menu music, layered over a paused primary

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        primary = CreateSource();
        overlay = CreateSource();
    }

    AudioSource CreateSource()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        s.spatialBlend = 0f; // 2D; scaled by AudioListener.volume
        return s;
    }

    static MusicManager Get()
    {
        if (Instance == null) new GameObject("MusicManager").AddComponent<MusicManager>();
        return Instance;
    }

    // --- public API ------------------------------------------------------

    // Gameplay music. Idempotent: if this track is already the primary and playing, it is left alone
    // so it never restarts across the level 1 -> 2 transition or a retry.
    public static void Ensure(AudioClip clip) => Get().PlayPrimary(clip);

    // Main-menu music (loaded from Resources).
    public static void PlayMenuMusic() => Get().PlayPrimary(Resources.Load<AudioClip>(MenuTrack));

    // Level-editor music, played while the player is building rather than playing.
    public static void PlayEditorMusic() => Get().PlayPrimary(Resources.Load<AudioClip>(EditorTrack));

    // Switch to the pause-menu track, freezing the gameplay track underneath it.
    public static void PlayPauseMusic() => Get().PushPause(Resources.Load<AudioClip>(PauseTrack));

    // Leave the pause menu: stop the pause track and resume the gameplay track where it paused.
    public static void StopPauseMusic() { if (Instance != null) Instance.PopPause(); }

    // --- internals -------------------------------------------------------

    void PlayPrimary(AudioClip clip)
    {
        StopOverlay(); // setting the main track ends any lingering pause overlay
        if (clip == null) return;

        if (primary.clip == clip)
        {
            if (!primary.isPlaying) primary.UnPause(); // was paused (e.g. returning from a pause)
            return;                                    // already the current track — don't restart it
        }
        primary.clip = clip;
        primary.Play();
    }

    void PushPause(AudioClip clip)
    {
        if (clip == null) return;
        primary.Pause();       // freeze the gameplay track at its current position
        overlay.clip = clip;
        overlay.Play();
    }

    void PopPause()
    {
        StopOverlay();
        primary.UnPause();     // continue the gameplay track from where it paused
    }

    void StopOverlay()
    {
        if (overlay.isPlaying) overlay.Stop();
        overlay.clip = null;
    }
}
