using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Skins a UI Button with a looping sprite-sheet animation instead of a coloured box and a text
// label. Same job as SpriteButton, but for artwork that moves on its own — the options gear, which
// turns continuously rather than sitting on one frame.
//
// SpriteButton is the wrong fit for that: it hands the frames to Unity's built-in SpriteSwap
// transition, and SpriteSwap pins the Image to a single sprite whenever the button is hovered or
// selected. A gear that stops turning the moment the mouse touches it looks broken, so this drives
// the Image itself, reads the press through pointer events, and switches the Button's own
// transition off so nothing swaps the sprite out from under the animation.
//
// Press feedback is a burst of speed rather than a separate frame: every frame on the sheet is the
// same cog at a different angle, so the gear just spins up while it is held down.
//
// Frames come from a sheet in Assets/Resources, ordered by the numeric suffix Unity's slicer gives
// them (_0, _1, _2, ...), so buttons built in code need no inspector wiring. Leave the frames list
// empty to load them; fill it to override.
//
// The animation runs on unscaled time: two of the three screens that use it (the pause menu and the
// level-select nav bar) freeze the game with Time.timeScale = 0, and a scaled clock would leave the
// gear stopped exactly where it is meant to be spinning.
[RequireComponent(typeof(Button)), RequireComponent(typeof(Image))]
public class AnimatedSpriteButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [Header("Sprite sheet")]
    [Tooltip("Sheet name inside Assets/Resources, sliced into '<name>_0', '<name>_1', ... " +
             "Used only when the frames list below is left empty.")]
    [SerializeField] private string spriteSheet = "options icon";

    [Header("Frames")]
    [Tooltip("The loop, in order. Left empty, it is loaded from the sheet.")]
    [SerializeField] private Sprite[] frames;

    [Header("Speed")]
    [Tooltip("Frames per second while the button is idle.")]
    [SerializeField] private float fps = 8f;
    [Tooltip("How much faster the loop runs while the button is held down. This is the press " +
             "feedback, so keep it well above 1.")]
    [SerializeField] private float pressedSpeedMultiplier = 4f;

    [Header("Label")]
    [Tooltip("Hide any child Text when the skin is applied. The artwork carries the wording, so a " +
             "leftover label from an authored button would print over it.")]
    [SerializeField] private bool hideChildLabel = true;

    private Image image;
    private float timer;
    private int index;
    private bool held;

    private void Awake() => Apply();

    // For buttons built in code: point the skin at a different sheet and re-apply. AddComponent
    // already ran Awake against the default sheet, so this re-resolves the frames from scratch.
    public void Configure(string sheetName)
    {
        spriteSheet = sheetName;
        frames = null;
        Apply();
    }

    private void Apply()
    {
        image = GetComponent<Image>();

        if (frames == null || frames.Length == 0) LoadFrames();
        if (frames == null || frames.Length == 0)
        {
            Debug.LogError($"[AnimatedSpriteButton] No frames found for sheet '{spriteSheet}'. " +
                           $"It must live in Assets/Resources and be sliced into '{spriteSheet}_0', '{spriteSheet}_1', ...");
            return;
        }

        timer = 0f;
        index = 0;
        image.sprite = frames[0];
        image.preserveAspect = true;  // frames can differ slightly in size; never stretch them
        image.color = Color.white;    // drop any placeholder background tint the button was built with
        image.type = Image.Type.Simple;

        // Both of the built-in transitions fight the loop — ColorTint darkens the artwork on hover,
        // SpriteSwap replaces it outright — so the spin is the only feedback this button gives.
        GetComponent<Button>().transition = Selectable.Transition.None;

        if (hideChildLabel)
            foreach (Text label in GetComponentsInChildren<Text>(true))
                label.gameObject.SetActive(false);
    }

    // Pulls every sliced frame out of the sheet, ordered by the number Unity appends to each one.
    // Resources.LoadAll gives no ordering guarantee, and plain alphabetical ordering would put
    // "_10" between "_1" and "_2" the day a sheet grows past ten frames.
    private void LoadFrames()
    {
        if (string.IsNullOrEmpty(spriteSheet)) return;

        var loaded = new List<Sprite>(Resources.LoadAll<Sprite>(spriteSheet));
        loaded.Sort((a, b) => FrameNumber(a.name).CompareTo(FrameNumber(b.name)));
        frames = loaded.ToArray();
    }

    // The trailing "_<n>" of a sliced frame's name; unnumbered names sort to the front.
    private static int FrameNumber(string name)
    {
        int underscore = name.LastIndexOf('_');
        if (underscore < 0) return -1;
        return int.TryParse(name.Substring(underscore + 1), out int n) ? n : -1;
    }

    // Pointer down/up land here as well as on the Button itself — the event system delivers to every
    // component on the object that handles them, so the click still fires as normal. Release is
    // reported to whoever received the press, so dragging off the button before letting go still
    // drops the gear back to its idle speed.
    public void OnPointerDown(PointerEventData eventData) => held = true;
    public void OnPointerUp(PointerEventData eventData) => held = false;

    // A button that is switched off mid-press (the pause menu hides its main panel on a click) never
    // sees the release, and would come back spinning fast.
    private void OnDisable() => held = false;

    private void Update()
    {
        if (frames == null || frames.Length == 0) return;

        float rate = held ? fps * pressedSpeedMultiplier : fps;
        if (rate <= 0f) return;

        timer += Time.unscaledDeltaTime;
        float frameTime = 1f / rate;
        while (timer >= frameTime)
        {
            timer -= frameTime;
            index = (index + 1) % frames.Length;
            image.sprite = frames[index];
        }
    }
}
