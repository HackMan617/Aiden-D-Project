using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The player's color-wheel weapon: a spinning roulette dial pinned to the BOTTOM-LEFT corner of
/// the screen for the whole of a level. Press Space (or click the dial) to fire — the wheel spins
/// up, eases to a stop on a random frame, and the color the FIXED arrow lands on is the color the
/// shot is tinted. So every shot comes out a random color, drawn from the artwork itself.
///
/// Like the rest of this project's UI it builds itself in code and slices its own sheet at runtime:
/// 'color-wheel-spritesheet' lives in Assets/Resources (384x256 = 6 columns x 4 rows of 64x64
/// wheels) and is cut into 24 frames on first use, so nothing has to be wired in the Inspector or
/// re-sliced in the Sprite Editor.
///
/// The 24 cells are one 12-segment wheel turned 15 degrees at a time, so playing them in order is
/// a real rotation and each of the 12 segment colors is what the arrow sees on exactly 2 of the
/// frames — an even 1-in-12 chance per shot. The color itself is READ OUT OF THE TEXTURE rather
/// than computed, so the shot is tinted with the artwork's own palette.
///
/// Spawned by LevelGrid on the gameplay path only, so the level editor's screen stays its own.
/// </summary>
public class ColorWheelWeapon : MonoBehaviour
{
    // The sheet in Assets/Resources. Loaded by name the same way the mute / quit / options button
    // art is, so this component needs no Inspector wiring at all.
    public const string SheetName = "color-wheel-spritesheet";

    [Header("Sheet")]
    [Tooltip("Left blank, the 'color-wheel-spritesheet' texture is loaded from Resources.")]
    public Texture2D sheet;
    [Tooltip("Wheels across the sheet.")]
    public int columns = 6;
    [Tooltip("Wheels down the sheet.")]
    public int rows = 4;

    [Header("HUD placement (bottom-left corner)")]
    [Tooltip("Dial size in reference (1920x1080) pixels.")]
    public float wheelSize = 190f;
    [Tooltip("Gap from the bottom-left corner of the screen, in reference pixels.")]
    public Vector2 corner = new Vector2(34f, 34f);

    [Header("Spin")]
    [Tooltip("Seconds the wheel takes to spin down to its answer. Kept short so firing stays snappy.")]
    public float spinDuration = 0.55f;
    [Tooltip("Fewest / most frames the wheel advances during one spin. Several times around the " +
             "24-frame sheet, so it reads as a fast blur that slows to a stop.")]
    public int minSteps = 70;
    public int maxSteps = 120;

    [Header("Firing")]
    [Tooltip("The shooter. Auto-finds 'Player' if empty.")]
    public Transform player;
    [Tooltip("Shot speed in world units per second.")]
    public float projectileSpeed = 9f;
    [Tooltip("How far in front of the player a shot appears, so it never spawns inside the sprite.")]
    public float muzzleOffset = 0.45f;
    [Tooltip("Seconds between shots, on top of the spin itself.")]
    public float cooldown = 0.15f;

    /// <summary>Raised when the wheel settles, with the color it landed on.</summary>
    public event Action<Color> OnColorSelected;

    /// <summary>True while the wheel is spinning down; a second Fire() is ignored until it stops.</summary>
    public bool IsSpinning { get; private set; }

    /// <summary>The color the last spin landed on (white before the first shot).</summary>
    public Color SelectedColor { get; private set; } = Color.white;

    Sprite[] frames;
    Image wheelImage;
    Image arrowImage;
    int frameIndex;
    float nextFireTime;
    bool wasFrozen = true;   // the level-select freeze is already up when the HUD is built
    bool sheetUnreadable;    // GetPixel refused once — fall back to a random hue and stop retrying

    /// <summary>Build the weapon and its corner HUD. `shooter` may be null; 'Player' is found then.</summary>
    public static ColorWheelWeapon Spawn(Transform shooter = null)
    {
        var go = new GameObject("ColorWheelWeapon");
        var weapon = go.AddComponent<ColorWheelWeapon>();
        weapon.player = shooter;
        return weapon;
    }

    void Awake()
    {
        BuildFrames();
        BuildHud();
    }

    void Start()
    {
        if (player == null)
        {
            GameObject p = GameObject.Find("Player");
            if (p != null) player = p.transform;
        }
    }

    void Update()
    {
        // The wheel is a gameplay control, so it stays quiet whenever the game is frozen — the
        // level-select picker, the pause overlay and the win / game-over screens all hold timeScale
        // at 0. The frame the freeze LIFTS is skipped too: the Space that starts a level would
        // otherwise be read again here and fire a shot the instant play begins.
        if (Time.timeScale <= 0f) { wasFrozen = true; return; }
        if (wasFrozen) { wasFrozen = false; return; }

        var over = GameOverManager.Instance;
        if (over != null && over.HasEnded) return;

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.spaceKey.wasPressedThisFrame) Fire();
    }

    /// <summary>Spin the wheel and shoot on the color it stops at. Ignored while already spinning.</summary>
    public void Fire()
    {
        if (IsSpinning || Time.time < nextFireTime) return;
        if (frames == null || frames.Length == 0) return;
        StartCoroutine(SpinRoutine());
    }

    IEnumerator SpinRoutine()
    {
        IsSpinning = true;

        int start = frameIndex;
        int steps = UnityEngine.Random.Range(minSteps, maxSteps + 1);

        // Ease-out cubic over scaled time, so a spin freezes along with everything else if the
        // player pauses mid-shot rather than resolving behind the pause menu.
        float t = 0f;
        while (t < spinDuration)
        {
            t += Time.deltaTime;
            float p = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / spinDuration), 3f);
            ShowFrame(start + Mathf.RoundToInt(steps * p));
            yield return null;
        }
        ShowFrame(start + steps); // land exactly on the frame the color is read from

        SelectedColor = ColorForFrame(frameIndex);
        if (arrowImage != null) arrowImage.color = SelectedColor; // the arrow shows what just loaded
        OnColorSelected?.Invoke(SelectedColor);
        FireProjectile(SelectedColor);

        nextFireTime = Time.time + cooldown;
        IsSpinning = false;
    }

    void ShowFrame(int index)
    {
        if (frames == null || frames.Length == 0) return;
        frameIndex = ((index % frames.Length) + frames.Length) % frames.Length;
        if (wheelImage != null) wheelImage.sprite = frames[frameIndex];
    }

    void FireProjectile(Color color)
    {
        if (player == null) return;

        // Shoot the way the player is looking, so standing still and firing still aims sensibly.
        Vector2 dir = Vector2.right;
        var control = player.GetComponent<PlayerController>();
        if (control != null) dir = control.Facing;
        if (dir.sqrMagnitude < 0.0001f) dir = Vector2.right;
        dir.Normalize();

        ColorProjectile.Spawn(player.position + (Vector3)(dir * muzzleOffset), dir, color, projectileSpeed);
    }

    // --- the sheet -------------------------------------------------------
    // Cut the sheet into its 24 wheels. Rows read top-to-bottom the way the art does, while texture
    // coordinates count up from the bottom, hence the flipped y. Same runtime Sprite.Create trick
    // LevelGrid uses for the tile and sawblade sheets — no asset re-slicing needed.
    void BuildFrames()
    {
        if (sheet == null) sheet = Resources.Load<Texture2D>(SheetName);
        if (sheet == null)
        {
            Debug.LogError("[ColorWheelWeapon] '" + SheetName + "' not found in Assets/Resources.");
            return;
        }

        int cols = Mathf.Max(1, columns), rws = Mathf.Max(1, rows);
        float w = sheet.width / (float)cols;
        float h = sheet.height / (float)rws;
        if (w <= 0f || h <= 0f) return;

        frames = new Sprite[cols * rws];
        for (int i = 0; i < frames.Length; i++)
        {
            int col = i % cols, row = i / cols;
            var rect = new Rect(col * w, sheet.height - (row + 1) * h, w, h);
            frames[i] = Sprite.Create(sheet, rect, new Vector2(0.5f, 0.5f), h);
        }
    }

    /// <summary>
    /// The color the fixed arrow points at on a given frame, read straight out of the sheet.
    /// A small patch of pixels around the arrow's tip is sampled and the most saturated one wins.
    /// The patch matters: on every other frame of the rotation a black segment divider lands
    /// exactly on the horizontal right radius, so reading that single line would return the
    /// divider's near-black on half the shots (and the hub in the middle is dark too).
    /// </summary>
    public Color ColorForFrame(int frame)
    {
        if (sheet == null || frames == null || frames.Length == 0 || sheetUnreadable)
            return RandomHue();

        int cols = Mathf.Max(1, columns), rws = Mathf.Max(1, rows);
        frame = ((frame % frames.Length) + frames.Length) % frames.Length;
        float w = sheet.width / (float)cols;
        float h = sheet.height / (float)rws;

        // Centre of this frame's cell, in texture pixels.
        float cx = (frame % cols) * w + w * 0.5f;
        float cy = sheet.height - (frame / cols + 1) * h + h * 0.5f;

        try
        {
            Color best = Color.white;
            float bestScore = -1f;
            // Walk right from just outside the hub to just inside the rim — the band the arrow
            // overlaps — and sweep a little above and below the radius at each step, so a divider
            // sitting on the radius is stepped over rather than read. Most vivid opaque pixel wins.
            for (float f = 0.16f; f <= 0.34f; f += 0.02f)
            {
                for (float dy = -0.08f; dy <= 0.08f; dy += 0.02f)
                {
                    Color c = sheet.GetPixel(Mathf.RoundToInt(cx + w * f), Mathf.RoundToInt(cy + h * dy));
                    if (c.a < 0.5f) continue;

                    float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                    float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                    float score = (max - min) + max * 0.25f; // saturation, brightness as a tiebreak
                    if (score > bestScore) { bestScore = score; best = c; }
                }
            }
            if (bestScore < 0f) return RandomHue();
            best.a = 1f;
            return best;
        }
        catch (UnityException)
        {
            // The texture got imported without Read/Write enabled. Keep the weapon working — the
            // point is a random color per shot — and don't ask the texture again.
            sheetUnreadable = true;
            Debug.LogWarning("[ColorWheelWeapon] '" + SheetName + "' is not readable; tick Read/Write on " +
                             "its import settings to take shot colors from the artwork itself.");
            return RandomHue();
        }
    }

    static Color RandomHue() => Color.HSVToRGB(UnityEngine.Random.value, 0.95f, 1f);

    // --- HUD -------------------------------------------------------------
    // Its own overlay canvas, ordered below every menu in the scene (pause HUD 40, game-over 50,
    // level-select 200, pause overlay 250, editor 300) so the dial tucks under whatever opens.
    void BuildHud()
    {
        var canvasGO = new GameObject("ColorWheelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform, false);
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // The dial itself, pinned to the bottom-left corner. It is also a button, so the wheel can
        // be fired with the mouse; being a raycast target means a click on it counts as UI and so
        // never starts a camera drag-pan (see CameraController.PointerOverUI).
        var wheelGO = new GameObject("Wheel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        wheelGO.transform.SetParent(canvasGO.transform, false);
        wheelImage = wheelGO.GetComponent<Image>();
        wheelImage.preserveAspect = true;
        if (frames != null && frames.Length > 0) wheelImage.sprite = frames[0];

        var button = wheelGO.GetComponent<Button>();
        button.targetGraphic = wheelImage;
        button.transition = Selectable.Transition.None; // the spin is the feedback
        button.onClick.AddListener(Fire);

        var wheelRT = wheelGO.GetComponent<RectTransform>();
        wheelRT.anchorMin = wheelRT.anchorMax = wheelRT.pivot = Vector2.zero; // bottom-left corner
        wheelRT.sizeDelta = new Vector2(wheelSize, wheelSize);
        wheelRT.anchoredPosition = corner;

        // The fixed arrow: it never turns, and the segment it points into is what fires. Sits just
        // off the wheel's right edge, aiming back in at it.
        var arrowGO = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        arrowGO.transform.SetParent(wheelGO.transform, false);
        arrowImage = arrowGO.GetComponent<Image>();
        arrowImage.sprite = ArrowSprite();
        arrowImage.color = SelectedColor;
        arrowImage.raycastTarget = false;

        float arrowH = wheelSize * 0.26f;
        var arrowRT = arrowGO.GetComponent<RectTransform>();
        arrowRT.anchorMin = arrowRT.anchorMax = arrowRT.pivot = new Vector2(1f, 0.5f);
        arrowRT.sizeDelta = new Vector2(arrowH, arrowH);
        arrowRT.anchoredPosition = new Vector2(arrowH * 0.75f, 0f);
    }

    // A left-pointing arrowhead, drawn in code so the HUD needs no extra art. The outline is left
    // black because a tint multiplies: black stays black whatever color the arrow is showing, so
    // the head keeps its shape against the wheel behind it.
    static Sprite arrowSprite;
    static Sprite ArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;

        const int S = 32;
        var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        float half = (S - 1) * 0.5f;
        for (int y = 0; y < S; y++)
        {
            for (int x = 0; x < S; x++)
            {
                // Apex at the left edge, base filling the right edge.
                float reach = x / (float)(S - 1) * half;
                float dy = Mathf.Abs(y - half);
                Color c = Color.clear;
                if (dy <= reach) c = (dy > reach - 3f || x < 3) ? Color.black : Color.white;
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        arrowSprite = Sprite.Create(tex, new Rect(0f, 0f, S, S), new Vector2(0.5f, 0.5f), S);
        return arrowSprite;
    }
}
