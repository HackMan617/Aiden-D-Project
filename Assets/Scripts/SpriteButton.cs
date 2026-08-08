using UnityEngine;
using UnityEngine.UI;

// Skins a UI Button with a two-frame sprite sheet instead of a coloured box and a text label:
//
//   <sheet>_0  the idle frame, shown normally
//   <sheet>_1  the clicked frame, shown while the button is held down
//
// Same idea as MuteButton, but for momentary presses rather than a saved on/off state, and it does
// not touch what the button DOES — the Button's own onClick stays wherever it was wired (the scene,
// or the code that built it). This only changes how the button looks.
//
// The press swap is handed to Unity's built-in SpriteSwap transition rather than reimplemented with
// pointer events, so it already behaves correctly for the awkward cases: dragging off the button
// while held, releasing outside it, and keyboard / gamepad submit.
//
// Frames are pulled from a sheet in Assets/Resources by name suffix, so buttons that are built in
// code (the pause menu) need no inspector wiring at all. Leave the sprite fields empty to load
// them; assign them to override.
[RequireComponent(typeof(Button)), RequireComponent(typeof(Image))]
public class SpriteButton : MonoBehaviour
{
    [Header("Sprite sheet")]
    [Tooltip("Sheet name inside Assets/Resources, sliced into '<name>_0' (idle) and '<name>_1' (clicked). " +
             "Used only when the two sprite fields below are left empty.")]
    [SerializeField] private string spriteSheet = "quit button";

    [Header("Frames")]
    [Tooltip("Shown normally. Left blank, it is loaded from the sheet ('<sheet>_0').")]
    [SerializeField] private Sprite idleSprite;
    [Tooltip("Shown while the button is held down. Left blank, it is loaded from the sheet ('<sheet>_1').")]
    [SerializeField] private Sprite clickedSprite;

    [Header("Label")]
    [Tooltip("Hide any child Text when the skin is applied. The artwork carries the wording, so a " +
             "leftover label from an authored button would print over it.")]
    [SerializeField] private bool hideChildLabel = true;

    private void Awake() => Apply();

    // For buttons built in code: point the skin at a different sheet and re-apply. AddComponent
    // already ran Awake against the default sheet, so this re-resolves the frames from scratch.
    public void Configure(string sheetName)
    {
        spriteSheet = sheetName;
        idleSprite = null;
        clickedSprite = null;
        Apply();
    }

    private void Apply()
    {
        var button = GetComponent<Button>();
        var image = GetComponent<Image>();

        if (idleSprite == null || clickedSprite == null) LoadFrames();
        if (idleSprite == null)
        {
            Debug.LogError($"[SpriteButton] No frames found for sheet '{spriteSheet}'. " +
                           $"It must live in Assets/Resources and be sliced into '{spriteSheet}_0' / '{spriteSheet}_1'.");
            return;
        }

        image.sprite = idleSprite;
        image.preserveAspect = true;  // the two frames differ slightly in height; never stretch them
        image.color = Color.white;    // drop any placeholder background tint the button was built with
        image.type = Image.Type.Simple;

        // SpriteSwap replaces the default colour tint, so the frames are the only feedback — a tint
        // on top would darken the artwork on hover and fight the press frame.
        button.transition = Selectable.Transition.SpriteSwap;
        SpriteState state = button.spriteState;
        state.pressedSprite = clickedSprite;
        state.selectedSprite = idleSprite;
        state.highlightedSprite = idleSprite;
        state.disabledSprite = idleSprite;
        button.spriteState = state;

        if (hideChildLabel)
            foreach (Text label in GetComponentsInChildren<Text>(true))
                label.gameObject.SetActive(false);
    }

    // Pulls the two sliced frames out of the sheet by name suffix (_0 = idle, _1 = clicked).
    private void LoadFrames()
    {
        if (string.IsNullOrEmpty(spriteSheet)) return;
        foreach (Sprite frame in Resources.LoadAll<Sprite>(spriteSheet))
        {
            if (frame.name.EndsWith("_0")) { if (idleSprite == null) idleSprite = frame; }
            else if (frame.name.EndsWith("_1")) { if (clickedSprite == null) clickedSprite = frame; }
        }
        // A sheet that was never sliced shows up as a single sprite; use it for both states rather
        // than leaving the button blank.
        if (idleSprite == null)
        {
            Sprite whole = Resources.Load<Sprite>(spriteSheet);
            if (whole != null) idleSprite = whole;
        }
        if (clickedSprite == null) clickedSprite = idleSprite;
    }
}
