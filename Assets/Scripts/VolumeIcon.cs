using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// The speaker icon beside the volume slider: it shows at a glance how loud the game currently is.
//
// The "audio buttons" sheet holds four speaker icons, sliced loudest-first:
//
//   _0  three waves — full volume
//   _1  two waves
//   _2  one wave
//   _3  a cross     — silent
//
// The sheet reads loud-to-quiet but a slider reads quiet-to-loud, so the frame is chosen from the
// slider's value inverted: dragged hard left the icon is the crossed-out speaker and the game is
// silent, dragged hard right it is three waves and the game is at full volume.
//
// It also follows the mute button. Mute works through AudioListener.pause, which is deliberately
// independent of the slider's AudioListener.volume so the two controls don't fight each other —
// but that means a muted game with the slider still at full would otherwise show three waves while
// making no sound at all.
//
// The state is polled in Update rather than driven off slider.onValueChanged, because the mute half
// of it has nothing to subscribe to: AudioListener.pause is a plain static property with no change
// event. The poll costs a float compare and only touches the Image when the frame actually changes.
[RequireComponent(typeof(Image))]
public class VolumeIcon : MonoBehaviour
{
    [Header("Sprite sheet")]
    [Tooltip("Sheet name inside Assets/Resources, sliced loudest-first into '<name>_0' (full) " +
             "through '<name>_3' (silent). Used only when the frames list below is left empty.")]
    [SerializeField] private string spriteSheet = "audio buttons";

    [Header("Frames")]
    [Tooltip("Loudest first, silent last. Left empty, they are loaded from the sheet.")]
    [SerializeField] private Sprite[] frames;

    [Header("Source")]
    [Tooltip("The slider to follow. Left empty, the icon reads AudioListener.volume directly.")]
    [SerializeField] private Slider slider;

    private Image image;
    private int shown = -1; // no frame drawn yet, so the first Refresh always assigns one

    private void Awake()
    {
        image = GetComponent<Image>();
        image.preserveAspect = true; // the frames share a rect, but never stretch pixel art anyway
        image.color = Color.white;   // drop any placeholder tint the Image was built with
        image.type = Image.Type.Simple;
        if (frames == null || frames.Length == 0) LoadFrames();
    }

    // For icons built in code: hand it the slider it belongs to and let it draw itself immediately.
    public void Track(Slider volumeSlider)
    {
        slider = volumeSlider;
        shown = -1;
        Refresh();
    }

    // The options panel is switched off rather than destroyed, so the icon has to re-read the
    // volume on the way back in — it may have been changed from another screen in the meantime.
    private void OnEnable()
    {
        shown = -1;
        Refresh();
    }

    private void Update() => Refresh();

    private void Refresh()
    {
        if (image == null || frames == null || frames.Length == 0) return;

        int frame = FrameFor(slider != null ? slider.value : AudioListener.volume);
        if (frame == shown) return;
        shown = frame;
        image.sprite = frames[frame];
    }

    // Picks a frame for a 0..1 volume. The last frame is the silent one; the rest split what is
    // left of the range evenly, so the icon gains a wave at each third and the arithmetic still
    // holds if the sheet ever grows another frame.
    private int FrameFor(float volume)
    {
        if (AudioListener.pause || volume <= 0.001f) return frames.Length - 1;

        int audible = frames.Length - 1;                                     // frames with sound
        int step = Mathf.CeilToInt(Mathf.Clamp01(volume) * audible);         // 1 .. audible
        return audible - step;                                               // full volume -> _0
    }

    // Pulls every sliced frame off the sheet, ordered by the number Unity appends to each one.
    // Resources.LoadAll gives no ordering guarantee, and the order is the whole meaning here —
    // sorted wrongly, the icon would show two waves for a silent game.
    private void LoadFrames()
    {
        if (string.IsNullOrEmpty(spriteSheet)) return;

        var loaded = new List<Sprite>(Resources.LoadAll<Sprite>(spriteSheet));
        loaded.Sort((a, b) => FrameNumber(a.name).CompareTo(FrameNumber(b.name)));
        frames = loaded.ToArray();

        if (frames.Length == 0)
            Debug.LogError($"[VolumeIcon] No frames found for sheet '{spriteSheet}'. It must live in " +
                           $"Assets/Resources and be sliced into '{spriteSheet}_0' ... '{spriteSheet}_3'.");
    }

    // The trailing "_<n>" of a sliced frame's name; unnumbered names sort to the front.
    private static int FrameNumber(string name)
    {
        int underscore = name.LastIndexOf('_');
        if (underscore < 0) return -1;
        return int.TryParse(name.Substring(underscore + 1), out int n) ? n : -1;
    }
}
