using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

// Level-select start screen. When the game starts it freezes gameplay and shows the "WORLD 1"
// title with a 5x2 grid of ten level boxes. WASD (or the arrow keys) move the RED selection box
// around the grid. Only level 1 (the current level) is accessible — press Enter or Space on it
// to start playing; the other levels are locked.
//
// Along the bottom is a navigation bar (Main Menu / Options / Quit) so the player can leave the
// run at any time: Main Menu and Options return to the first page (resetting progress to level 1),
// with Options jumping straight to the main-menu settings panel; Quit exits the game.
//
// Everything (canvas, title, boxes, numbers) is built in code at runtime. The grey/red level boxes
// come from the "aiden d world 1 selection" sheet (256x64), sliced by the rects below (found by
// scanning the art). The heading is an animated "World" (22 frames from the "aiden d world
// animation" sheet, 704x64) with the "world 1 number" sprite shown next to it -> reads "World 1".
public class LevelSelectManager : MonoBehaviour
{
    [Tooltip("The 'aiden d world 1 selection' texture (256x64: WORLD text, grey box, red box).")]
    public Texture2D selectionSheet;
    [Tooltip("The 'aiden d world animation' texture (704x64: 22 frames of the wiggling 'World').")]
    public Texture2D worldAnimationSheet;
    [Tooltip("The 'world 1 number' texture (64x64: the stylised red '1').")]
    public Texture2D worldNumberSheet;
    [Tooltip("Playback speed of the animated 'World' heading, in frames per second.")]
    public float titleFps = 12f;
    [Tooltip("How many level boxes to show.")]
    public int levelCount = 10;
    [Tooltip("Boxes per row.")]
    public int columns = 5;

    [Tooltip("Background music for gameplay. Played (looping) the moment a level starts and kept " +
             "playing across the level 1 -> 2 transition and retries via MusicManager. Routed through " +
             "an AudioSource, so it is scaled by the master volume (the volume slider) and silenced " +
             "by the mute button.")]
    public AudioClip startLevelSound;

    // Sub-sprite rects within the 256x64 selection sheet (Unity texture coords, y up from bottom).
    static readonly Rect TitleRect = new Rect(2f, 28f, 52f, 34f);
    static readonly Rect GreyRect = new Rect(137f, 7f, 46f, 43f);
    static readonly Rect RedRect = new Rect(196f, 7f, 50f, 43f);

    // The 704x64 animation sheet holds 11 copies of the word "World", each in a 64px-wide cell,
    // drawn at slightly different vertical offsets so it bobs up and down. Each cell is sliced whole
    // (TitleFrameWidth) over a fixed vertical band (TitleBandY..+TitleBandH) so the bob is preserved,
    // then cycled in order to play the animation. (The sheet's own auto-slice split each word into
    // two half-word pieces, which is not what we want here.)
    const int TitleFrameCount = 11;
    const float TitleFrameWidth = 64f;
    const float TitleBandY = 14f, TitleBandH = 42f;
    // Tight crop of the "1" within the 64x64 number sheet.
    static readonly Rect NumberRect = new Rect(11f, 6f, 48f, 45f);

    Sprite titleSprite, greyBox, redBox, numberSprite;
    Sprite[] titleFrames;
    Image[] boxes;
    Image titleImg;      // the animated "World" heading
    float frameTimer;
    int frameIndex;
    int index;
    GameObject canvasGO;
    bool started;

    // Number of levels that are actually playable/designed so far (boxes 1..UnlockedLevels).
    // The rest of the grid is shown but locked (dimmed, can't be entered).
    const int UnlockedLevels = 2;

    // Set true when a level has already been decided (a level-select pick that changed the level,
    // a Retry, or a Continue) so the next scene load should skip the picker and drop straight into
    // the game. Consumed on the load it applies to. LevelGrid has already built the chosen level by
    // the time this runs, so we only need to unfreeze.
    public static bool AutoStart = false;

    void Start()
    {
        // The level editor shares this scene (see GameSession) — there is no level to pick there,
        // so stand down completely and leave the screen to LevelEditor.
        if (GameSession.IsEditing)
        {
            started = true;
            Time.timeScale = 1f;
            return;
        }

        if (AutoStart)
        {
            AutoStart = false;
            started = true;         // stops Update() from running the picker logic
            Time.timeScale = 1f;    // make sure the game is running (timeScale survives scene loads)
            MusicManager.Ensure(startLevelSound); // keep level 1's music going into level 2 / a retry
            return;                 // no picker UI this load — play immediately
        }

        // The picker is a menu screen, so it carries the menu track. Coming from the main menu this
        // is a no-op (already playing); coming back from the level editor it swaps the building
        // music back out.
        MusicManager.PlayMenuMusic();

        BuildSprites();
        BuildUI();

        Time.timeScale = 0f; // freeze the game until a level is entered
        Select(0);
    }

    void BuildSprites()
    {
        if (selectionSheet == null)
        {
            Debug.LogError("[LevelSelectManager] selectionSheet texture is not assigned.");
            return;
        }
        var pivot = new Vector2(0.5f, 0.5f);
        titleSprite = Sprite.Create(selectionSheet, TitleRect, pivot, 100f);
        greyBox = Sprite.Create(selectionSheet, GreyRect, pivot, 100f);
        redBox = Sprite.Create(selectionSheet, RedRect, pivot, 100f);

        // Animated "World" heading frames (one full word per 64px cell).
        if (worldAnimationSheet != null)
        {
            titleFrames = new Sprite[TitleFrameCount];
            for (int f = 0; f < TitleFrameCount; f++)
            {
                var r = new Rect(f * TitleFrameWidth, TitleBandY, TitleFrameWidth, TitleBandH);
                titleFrames[f] = Sprite.Create(worldAnimationSheet, r, pivot, 100f);
            }
        }
        // The "1" shown next to the heading.
        if (worldNumberSheet != null)
            numberSprite = Sprite.Create(worldNumberSheet, NumberRect, pivot, 100f);
    }

    void BuildUI()
    {
        canvasGO = new GameObject("LevelSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200; // above every other overlay
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Opaque backdrop.
        var bg = Child("Backdrop", canvasGO.transform);
        var bgImg = bg.AddComponent<Image>();
        bgImg.color = new Color(0.10f, 0.10f, 0.13f, 1f);
        Stretch(bg.GetComponent<RectTransform>());

        // Heading near the top. Prefer the animated "World" + "1" number; fall back to the old
        // static "WORLD 1" graphic if the new sheets weren't assigned.
        if (titleFrames != null && titleFrames.Length > 0)
        {
            // Animated "World" wordmark.
            var worldGO = Child("WorldTitle", canvasGO.transform);
            titleImg = worldGO.AddComponent<Image>();
            titleImg.sprite = titleFrames[0];
            titleImg.preserveAspect = true;
            var wrt = worldGO.GetComponent<RectTransform>();
            wrt.sizeDelta = new Vector2(400f, 262f);
            wrt.anchoredPosition = new Vector2(-140f, 360f);

            // The "1", sitting to the right so the heading reads "World 1".
            if (numberSprite != null)
            {
                var numImgGO = Child("WorldNumber", canvasGO.transform);
                var numImg = numImgGO.AddComponent<Image>();
                numImg.sprite = numberSprite;
                numImg.preserveAspect = true;
                var nrt = numImgGO.GetComponent<RectTransform>();
                nrt.sizeDelta = new Vector2(230f, 216f);
                nrt.anchoredPosition = new Vector2(215f, 355f);
            }
        }
        else
        {
            var titleGO = Child("Title", canvasGO.transform);
            var img = titleGO.AddComponent<Image>();
            img.sprite = titleSprite;
            img.preserveAspect = true;
            var trt = titleGO.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(620f, 240f);
            trt.anchoredPosition = new Vector2(0f, 290f);
        }

        // 5x2 grid of level boxes.
        boxes = new Image[levelCount];
        const float spacingX = 175f, spacingY = 165f, boxW = 135f, boxH = 125f;
        int rows = Mathf.CeilToInt(levelCount / (float)columns);
        for (int i = 0; i < levelCount; i++)
        {
            int col = i % columns, row = i / columns;
            float x = (col - (columns - 1) / 2f) * spacingX;
            float y = 30f - (row - (rows - 1) / 2f) * spacingY;

            var boxGO = Child("Box" + (i + 1), canvasGO.transform);
            var img = boxGO.AddComponent<Image>();
            img.sprite = greyBox;
            img.preserveAspect = true;
            var rt = boxGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(boxW, boxH);
            rt.anchoredPosition = new Vector2(x, y);
            boxes[i] = img;

            // Dim levels that aren't unlocked yet so it's clear which boxes can be entered. Select()
            // only swaps the sprite (grey <-> red), never the color, so this dim persists.
            if (i >= UnlockedLevels) img.color = new Color(1f, 1f, 1f, 0.4f);

            // Level number, with an outline so it reads on both the grey and red boxes.
            var numGO = Child("Num", boxGO.transform);
            Stretch(numGO.GetComponent<RectTransform>());
            var num = numGO.AddComponent<Text>();
            num.text = (i + 1).ToString();
            num.alignment = TextAnchor.MiddleCenter;
            num.color = Color.white;
            num.fontSize = 46;
            num.fontStyle = FontStyle.Bold;
            num.raycastTarget = false;
            num.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var outline = numGO.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            outline.effectDistance = new Vector2(2f, -2f);
        }

        // Navigation bar along the bottom. Clicks need an EventSystem with the Input-System UI
        // module (this project uses the Input System package); create one if nothing else has.
        EnsureEventSystem();
        var nav = Child("NavBar", canvasGO.transform);
        Stretch(nav.GetComponent<RectTransform>());
        var navSize = new Vector2(300f, 84f);
        CreateButton("Main Menu", nav.transform, new Vector2(-495f, -370f), navSize,
                     new Color(0.20f, 0.58f, 0.48f, 1f), ReturnToMainMenu);
        // Options is the turning gear, the same icon the main and pause menus use.
        CreateAnimatedSpriteButton("OptionsButton", OptionsButtonSheet, nav.transform, new Vector2(-165f, -370f), OpenOptions);
        // Build-your-own levels: opens the in-game editor, which also lists and launches everything
        // previously saved.
        CreateButton("Level Editor", nav.transform, new Vector2(165f, -370f), navSize,
                     new Color(0.72f, 0.52f, 0.16f, 1f), OpenLevelEditor);
        // Quit is the artwork button here too, so it reads the same on every screen that offers it.
        CreateSpriteButton("QuitButton", QuitButtonSheet, nav.transform, new Vector2(495f, -370f), QuitGame);
    }

    void Update()
    {
        if (started) return;

        // Advance the animated "World" heading. Uses unscaled time because the game is frozen
        // (Time.timeScale == 0) while the select screen is up.
        if (titleImg != null && titleFrames != null && titleFrames.Length > 1 && titleFps > 0f)
        {
            frameTimer += Time.unscaledDeltaTime;
            float frameTime = 1f / titleFps;
            while (frameTimer >= frameTime)
            {
                frameTimer -= frameTime;
                frameIndex = (frameIndex + 1) % titleFrames.Length;
                titleImg.sprite = titleFrames[frameIndex];
            }
        }

        var kb = Keyboard.current;
        if (kb == null) return;

        int prev = index;
        int col = index % columns;
        int row = index / columns;
        int rows = Mathf.CeilToInt(levelCount / (float)columns);

        // WASD / arrows move around the 2D grid, clamped at the edges.
        if ((kb.aKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) && col > 0) index -= 1;
        if ((kb.dKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) && col < columns - 1 && index + 1 < levelCount) index += 1;
        if ((kb.wKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame) && row > 0) index -= columns;
        if ((kb.sKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame) && row < rows - 1 && index + columns < levelCount) index += columns;

        if (index != prev) Select(index);

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
        {
            if (index < UnlockedLevels) StartGame(); // levels 1..UnlockedLevels are accessible
        }
    }

    // Highlight the selected box red (rest grey) and update the hint.
    void Select(int i)
    {
        index = i;
        if (boxes != null)
            for (int b = 0; b < boxes.Length; b++)
                boxes[b].sprite = (b == index) ? redBox : greyBox;
    }

    void StartGame()
    {
        int chosen = index + 1; // box 1 -> level 1, box 2 -> level 2, ...

        // If the grid is already built for the chosen level (the common fresh-entry case, where
        // MainMenu started a run at level 1), just unfreeze and play — this keeps the start sound.
        if (chosen == GameProgress.CurrentLevel)
        {
            started = true;
            Time.timeScale = 1f;
            canvasGO.SetActive(false);

            // Start the looping gameplay music. MusicManager persists across scene reloads, so this
            // same track keeps playing when the player continues to level 2. Scaled by
            // AudioListener.volume and silenced by the mute button.
            MusicManager.Ensure(startLevelSound);
            return;
        }

        // A different level was picked (e.g. level 2). LevelGrid builds the level from
        // GameProgress.CurrentLevel in its own Start(), so set the level and reload the scene to
        // rebuild it. AutoStart makes that reload skip the picker and drop straight into play.
        GameProgress.CurrentLevel = chosen;
        AutoStart = true;
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // --- navigation ------------------------------------------------------
    // Return to the main menu (the "first page"), resetting difficulty back to level 1. timeScale
    // must be restored first because it survives scene loads and the select screen froze the game.
    void ReturnToMainMenu()
    {
        // The menu track is switched in by MainMenu.Start on the next scene; nothing to stop here.
        GameProgress.Reset();
        Time.timeScale = 1f;
        SceneManager.LoadScene(0); // MainMenu is build index 0
    }

    // Open the level editor. It reuses this scene, so switching mode and reloading is all it takes;
    // GameSession reopens whatever level was last being built.
    void OpenLevelEditor() => GameSession.EnterEditor();

    // Same as Main Menu, but ask the menu to open its Options/settings panel on load.
    void OpenOptions()
    {
        MainMenu.OpenOptionsOnLoad = true;
        ReturnToMainMenu();
    }

    void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // --- helpers ---------------------------------------------------------
    static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    // The Input System needs an EventSystem with its UI module for UI clicks to register.
    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
    }

    // Sheets in Assets/Resources. Quit holds an idle (_0) and a clicked (_1) frame; the options
    // gear holds eight frames of a full turn, looped by AnimatedSpriteButton.
    const string QuitButtonSheet = "quit button";
    const string OptionsButtonSheet = "options icon";
    const float SpriteButtonHeight = 92f;

    // A button drawn from a sprite sheet instead of a coloured box with a label. SpriteButton loads
    // the frames and wires the pressed swap; the width follows the art's own aspect.
    static void CreateSpriteButton(string name, string sheet, Transform parent, Vector2 pos, UnityAction onClick)
    {
        var go = Child(name, parent);
        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        go.AddComponent<SpriteButton>().Configure(sheet);
        SizeToArt(go, img, pos);
    }

    // Same as CreateSpriteButton, but the artwork loops through every frame on the sheet instead of
    // holding a single idle one. For the options gear, which turns on its own.
    static void CreateAnimatedSpriteButton(string name, string sheet, Transform parent, Vector2 pos, UnityAction onClick)
    {
        var go = Child(name, parent);
        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        go.AddComponent<AnimatedSpriteButton>().Configure(sheet);
        SizeToArt(go, img, pos);
    }

    // Places an artwork button and gives it the loaded frame's own aspect, so the pixel art is never
    // stretched. The skin has already run by this point, so the Image is showing a real frame.
    static void SizeToArt(GameObject go, Image img, Vector2 pos)
    {
        float aspect = img.sprite != null && img.sprite.rect.height > 0f
            ? img.sprite.rect.width / img.sprite.rect.height
            : 1f;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(SpriteButtonHeight * aspect, SpriteButtonHeight);
        rt.anchoredPosition = pos;
    }

    // A labelled UI button on the select-screen canvas.
    static void CreateButton(string label, Transform parent, Vector2 pos, Vector2 size, Color color, UnityAction onClick)
    {
        var go = Child(label + "Button", parent);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        img.color = color;
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var txtGO = Child("Text", go.transform);
        Stretch(txtGO.GetComponent<RectTransform>());
        var txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = 30;
        txt.fontStyle = FontStyle.Bold;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
