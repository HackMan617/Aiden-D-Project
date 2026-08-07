using UnityEngine;
using UnityEngine.UI;

// A settings-screen mute toggle. Clicking it mutes/unmutes ALL game audio and swaps the button
// icon between two frames of the "mute button (1)" sprite sheet:
//   * unmutedSprite ("mute button (1)_0") — sound is playing; a click will mute.
//   * mutedSprite   ("mute button (1)_1") — sound is muted;   a click will resume.
//
// Audio is toggled through AudioListener.pause, which silences (and later resumes) every audio
// source at once without touching the volume level. That keeps it independent of the Options
// volume slider (which drives AudioListener.volume) — the two controls don't fight each other.
//
// The muted state is saved to PlayerPrefs so it survives scene changes and app restarts, and is
// re-applied whenever the settings panel is opened so the icon always matches reality.
[RequireComponent(typeof(Button)), RequireComponent(typeof(Image))]
public class MuteButton : MonoBehaviour
{
    [Header("Icon frames")]
    [Tooltip("Shown while sound is ON. Left blank, it is loaded from Resources ('mute button (1)_0').")]
    [SerializeField] private Sprite unmutedSprite;
    [Tooltip("Shown while sound is muted. Left blank, it is loaded from Resources ('mute button (1)_1').")]
    [SerializeField] private Sprite mutedSprite;

    // Sprite sheet in Assets/Resources so the two frames can be loaded at runtime without an
    // inspector reference (this button is built in code by MainMenu).
    private const string SpriteSheet = "mute button (1)";
    private const string MuteKey = "audio_muted";

    private Button button;
    private Image image;

    private static bool IsMuted
    {
        get => PlayerPrefs.GetInt(MuteKey, 0) == 1;
        set { PlayerPrefs.SetInt(MuteKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // Apply the saved mute state the moment the game starts, before any scene loads. Without this,
    // audio would only honour a previously-saved "muted" state once a settings panel is opened
    // (that is where the button lives). This keeps mute correct in every scene from launch.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplySavedStateOnStartup() => AudioListener.pause = IsMuted;

    private void Awake()
    {
        button = GetComponent<Button>();
        image = GetComponent<Image>();
        if (unmutedSprite == null || mutedSprite == null) LoadFrames();
        button.onClick.AddListener(Toggle);
    }

    // Pulls the two sliced frames out of the sprite sheet by name suffix (_0 = on, _1 = muted).
    private void LoadFrames()
    {
        foreach (Sprite frame in Resources.LoadAll<Sprite>(SpriteSheet))
        {
            if (frame.name.EndsWith("_0")) unmutedSprite = frame;
            else if (frame.name.EndsWith("_1")) mutedSprite = frame;
        }
    }

    // Runs every time the settings panel (and thus this button) is shown, so the icon and the
    // actual audio state stay in sync with whatever was saved previously.
    private void OnEnable() => Apply(IsMuted);

    private void Toggle()
    {
        IsMuted = !IsMuted;
        Apply(IsMuted);
    }

    private void Apply(bool muted)
    {
        AudioListener.pause = muted;              // mute (true) / resume (false) all audio
        image.sprite = muted ? mutedSprite : unmutedSprite;
    }
}
