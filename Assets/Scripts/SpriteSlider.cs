using UnityEngine;
using UnityEngine.UI;

// Skins a UI Slider with the volume-slider artwork. The companion to SpriteButton, but the sheet
// is laid out differently and so is the job:
//
//   <sheet>_0  the track — a thin horizontal bar (56x6)
//   <sheet>_1  the handle — a small round knob (13x13)
//
// These are two PARTS of one control, not the idle / clicked frames of a button, so a Slider gets
// them assigned to different child graphics rather than swapped on press.
//
// Sizes are fixed rather than derived from the slider's rect: the art is pixel art, and stretching
// a 6-pixel-tall bar to fill a 26-pixel-tall slider would blur it. The defaults are an exact 2x of
// the source pixels, so the art stays crisp. The track still stretches horizontally to whatever
// width the slider was laid out at — it is a uniform bar, so that reads correctly.
[RequireComponent(typeof(Slider))]
public class SpriteSlider : MonoBehaviour
{
    [Header("Sprite sheet")]
    [Tooltip("Sheet name inside Assets/Resources, sliced into '<name>_0' (track) and '<name>_1' (handle). " +
             "Used only when the two sprite fields below are left empty.")]
    [SerializeField] private string spriteSheet = "volume slider";

    [Header("Parts")]
    [Tooltip("The bar the handle slides along. Left blank, it is loaded from the sheet ('<sheet>_0').")]
    [SerializeField] private Sprite trackSprite;
    [Tooltip("The knob that is dragged. Left blank, it is loaded from the sheet ('<sheet>_1').")]
    [SerializeField] private Sprite handleSprite;

    [Header("Size")]
    [Tooltip("On-screen height of the track, in reference pixels. 18 = an exact 3x of the 6px source bar.")]
    [SerializeField] private float trackHeight = 18f;
    [Tooltip("On-screen size of the handle. 39 = an exact 3x of the 13px source knob.")]
    [SerializeField] private float handleSize = 39f;
    [Tooltip("Hide the default fill bar. The artwork is a plain track plus a knob with no fill graphic, " +
             "so a leftover fill would draw a coloured block over it.")]
    [SerializeField] private bool hideFill = true;

    private void Awake() => Apply();

    // For sliders built in code: point the skin at a different sheet and re-apply.
    public void Configure(string sheetName)
    {
        spriteSheet = sheetName;
        trackSprite = null;
        handleSprite = null;
        Apply();
    }

    private void Apply()
    {
        var slider = GetComponent<Slider>();

        if (trackSprite == null || handleSprite == null) LoadParts();
        if (trackSprite == null || handleSprite == null)
        {
            Debug.LogError($"[SpriteSlider] No parts found for sheet '{spriteSheet}'. It must live in " +
                           $"Assets/Resources and be sliced into '{spriteSheet}_0' (track) / '{spriteSheet}_1' (handle).");
            return;
        }

        // The track: a thin bar spanning the slider's full width, vertically centred.
        Image background = FindBackground(slider);
        if (background != null)
        {
            background.sprite = trackSprite;
            background.color = Color.white;
            background.type = Image.Type.Simple;
            var rt = background.rectTransform;
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, -trackHeight * 0.5f);
            rt.offsetMax = new Vector2(0f, trackHeight * 0.5f);
        }

        if (hideFill && slider.fillRect != null)
        {
            var fill = slider.fillRect.GetComponent<Image>();
            if (fill != null) fill.enabled = false;
        }

        // The handle: a square knob, kept round by preserveAspect.
        if (slider.handleRect != null)
        {
            var handle = slider.handleRect.GetComponent<Image>();
            if (handle != null)
            {
                handle.sprite = handleSprite;
                handle.color = Color.white;
                handle.preserveAspect = true;
                handle.type = Image.Type.Simple;
            }
            slider.handleRect.sizeDelta = new Vector2(handleSize, handleSize);
        }

        // The knob art carries the whole hover / drag feel; a colour tint on top would just dirty it.
        slider.transition = Selectable.Transition.None;
    }

    // The Slider's background is the one child Image that is neither the fill nor the handle.
    private static Image FindBackground(Slider slider)
    {
        foreach (Image image in slider.GetComponentsInChildren<Image>(true))
        {
            if (slider.fillRect != null && image.transform == slider.fillRect) continue;
            if (slider.handleRect != null && image.transform == slider.handleRect) continue;
            if (image.transform == slider.transform) continue; // the Slider's own graphic, if any
            return image;
        }
        return null;
    }

    private void LoadParts()
    {
        if (string.IsNullOrEmpty(spriteSheet)) return;
        foreach (Sprite part in Resources.LoadAll<Sprite>(spriteSheet))
        {
            if (part.name.EndsWith("_0")) { if (trackSprite == null) trackSprite = part; }
            else if (part.name.EndsWith("_1")) { if (handleSprite == null) handleSprite = part; }
        }
    }
}
