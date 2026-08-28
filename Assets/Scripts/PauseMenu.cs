using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

// In-game pause menu. Escape toggles pause: it freezes the game (Time.timeScale = 0) and shows a
// menu overlay over the dimmed, frozen game — Resume / Options / Main Menu / Quit. Escape again,
// or the Resume button, unfreezes and continues exactly where you left off (so it reads "Resume",
// unlike the start menu's "Play"). The whole UI is built in code at runtime.
//
// Escape is ignored while the level-select or a game-over/win screen is up (those own their state).
public class PauseMenu : MonoBehaviour
{
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    Font font;
    GameObject canvasGO;
    GameObject mainPanel;
    GameObject optionsPanel;
    Slider volumeSlider;
    Toggle fullscreenToggle;
    bool paused;

    GameObject hudPauseGO;        // the corner pause button
    GameObject levelSelectCanvas; // cached picker canvas, resolved once (see CanPause)
    bool levelSelectResolved;

    void Awake()
    {
        // The level editor reuses the game scene and owns Escape there (it closes the level
        // browser), so the pause menu and its corner button stand down entirely in edit mode.
        if (GameSession.IsEditing) { enabled = false; return; }

        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
        BuildHudPauseButton();
    }

    void Update()
    {
        // Offer the corner button only when Escape would work too, so it can't be clicked during
        // the level picker, a game-over screen, or while the pause menu is already up.
        if (hudPauseGO != null)
        {
            bool show = !paused && CanPause();
            if (hudPauseGO.activeSelf != show) hudPauseGO.SetActive(show);
        }

        Keyboard kb = Keyboard.current;
        if (kb == null) return;
        if (kb.escapeKey.wasPressedThisFrame)
        {
            if (paused) Resume();
            else if (CanPause()) Pause();
        }
    }

    bool CanPause()
    {
        if (GameOverManager.Instance != null && GameOverManager.Instance.HasEnded) return false;

        // The picker is built once, during the level-select manager's Start — which has already run
        // by the first Update. Resolve it a single time: this now runs every frame for the corner
        // button, and GameObject.Find walks the whole scene (135 grid tiles and counting).
        if (!levelSelectResolved)
        {
            levelSelectCanvas = GameObject.Find("LevelSelectCanvas");
            levelSelectResolved = true;
        }
        return !(levelSelectCanvas != null && levelSelectCanvas.activeInHierarchy);
    }

    public void Pause()
    {
        if (canvasGO == null) BuildUI(); // heal if the overlay was never built or got wiped
        paused = true;
        Time.timeScale = 0f;
        MusicManager.PlayPauseMusic(); // swap gameplay music for the pause-menu track
        ShowMain();
        if (volumeSlider != null) volumeSlider.SetValueWithoutNotify(AudioListener.volume);
        if (fullscreenToggle != null) fullscreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
        canvasGO.SetActive(true);
    }

    public void Resume()
    {
        paused = false;
        Time.timeScale = 1f;
        MusicManager.StopPauseMusic(); // resume the gameplay track where it left off
        if (canvasGO != null) canvasGO.SetActive(false);
    }

    void ShowMain() { mainPanel.SetActive(true); optionsPanel.SetActive(false); }
    void ShowOptions() { mainPanel.SetActive(false); optionsPanel.SetActive(true); }

    void GoMainMenu()
    {
        // MainMenu.Start switches to the menu track (and clears the pause overlay) on the next scene.
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
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

    // ---- UI construction ------------------------------------------------
    void BuildUI()
    {
        if (Object.FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        canvasGO = new GameObject("PauseCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 250;
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var dim = Child("Dim", canvasGO.transform);
        Stretch(dim);
        dim.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);

        mainPanel = Child("PauseMain", canvasGO.transform);
        Stretch(mainPanel);
        Label(mainPanel.transform, "PAUSED", new Vector2(0f, 280f), new Vector2(900f, 130f), 92, Color.white, TextAnchor.MiddleCenter);
        // Resume, Options and Quit are artwork buttons. Resume and Quit hold an idle frame and swap
        // to a clicked one; Options is a gear that turns the whole time it is on screen, which needs
        // the animated skin instead. Main Menu has no art yet, so it stays a labelled box; the
        // vertical spacing accounts for the taller sprite buttons.
        SpriteBtn(mainPanel.transform, "ResumeButton", PlayButtonSheet, new Vector2(0f, 120f), Resume);
        AnimatedSpriteBtn(mainPanel.transform, "OptionsButton", OptionsButtonSheet, new Vector2(0f, 10f), ShowOptions);
        Btn(mainPanel.transform, "Main Menu", new Vector2(0f, -80f), GoMainMenu);
        SpriteBtn(mainPanel.transform, "QuitButton", QuitButtonSheet, new Vector2(0f, -190f), QuitGame);

        optionsPanel = Child("PauseOptions", canvasGO.transform);
        Stretch(optionsPanel);
        Label(optionsPanel.transform, "OPTIONS", new Vector2(0f, 280f), new Vector2(800f, 110f), 72, Color.white, TextAnchor.MiddleCenter);
        Label(optionsPanel.transform, "Volume", new Vector2(-260f, 100f), new Vector2(240f, 50f), 34, Color.white, TextAnchor.MiddleLeft);
        // The slider sits between the "Volume" label (which ends at -140) and the controls after it,
        // and is centred at 150 so its wider body grows to the right rather than over the label.
        volumeSlider = MakeSlider(optionsPanel.transform, new Vector2(150f, 100f));
        volumeSlider.onValueChanged.AddListener(v => AudioListener.volume = v);
        // The speaker icon comes first, right beside the handle it follows; mute stays a control of
        // its own after it, since it works through AudioListener.pause rather than the volume.
        MakeVolumeIcon(optionsPanel.transform, new Vector2(454f, 100f), volumeSlider);
        MakeMuteButton(optionsPanel.transform, new Vector2(558f, 100f));
        fullscreenToggle = MakeToggle(optionsPanel.transform, new Vector2(-90f, 0f), "Fullscreen");
        fullscreenToggle.onValueChanged.AddListener(f => Screen.fullScreen = f);
        // How to Play: a thumbnail of the controls diagram that opens full-size when clicked. Its
        // label sits in the "Volume" column and the thumbnail starts where the slider starts, so the
        // panel reads as two columns of rows. Back moved down to clear the taller row.
        Label(optionsPanel.transform, "How to Play", new Vector2(-260f, -115f), new Vector2(260f, 50f), 34, Color.white, TextAnchor.MiddleLeft);
        MakeHowToPlay(optionsPanel.transform, new Vector2(-35f, -115f));
        Btn(optionsPanel.transform, "Back", new Vector2(0f, -245f), ShowMain);

        canvasGO.SetActive(false);
    }

    Slider MakeSlider(Transform parent, Vector2 pos)
    {
        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.name = "PauseVolumeSlider";
        go.transform.SetParent(parent, false);
        RT(go, pos, VolumeSliderSize);
        var s = go.GetComponent<Slider>();
        s.minValue = 0f; s.maxValue = 1f; s.value = 1f;
        go.AddComponent<SpriteSlider>().Configure(VolumeSliderSheet); // track + knob artwork
        return s;
    }

    // A small pause button in the top-right corner, so the game can be paused with the mouse and
    // not only with Escape. It sits on its own canvas beneath the game-over HUD and the pause
    // overlay, and Update() hides it whenever pausing isn't allowed.
    void BuildHudPauseButton()
    {
        var hudGO = new GameObject("PauseHudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = hudGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40; // below the game-over HUD (50) and the pause overlay (250)
        var scaler = hudGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        hudPauseGO = Child("HudPauseButton", hudGO.transform);
        var img = hudPauseGO.AddComponent<Image>();
        var btn = hudPauseGO.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(Pause);
        hudPauseGO.AddComponent<SpriteButton>().Configure(PauseButtonSheet);

        // Pinned to the top-right corner; the game-over HUD's Retry owns the top-left.
        var rt = hudPauseGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(HudButtonHeight * SpriteAspect(img), HudButtonHeight);
        rt.anchoredPosition = new Vector2(-24f, -24f);
    }

    // Mute toggle for the in-game pause menu, sitting next to the volume slider. MuteButton is
    // self-contained: it loads its own sprite frames and shares the same saved mute state as the
    // main-menu button, so toggling here stays in sync with the rest of the game.
    void MakeMuteButton(Transform parent, Vector2 pos)
    {
        var go = new GameObject("MuteButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(MuteButton));
        go.transform.SetParent(parent, false);
        RT(go, pos, new Vector2(80f, 80f));
        go.GetComponent<Image>().preserveAspect = true;
    }

    // The speaker that shows how loud the game is: full at the slider's right end, crossed out at
    // its left. Not a button — it only reports. See VolumeIcon.
    void MakeVolumeIcon(Transform parent, Vector2 pos, Slider follows)
    {
        var go = new GameObject("VolumeIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RT(go, pos, new Vector2(80f, 80f));
        go.GetComponent<Image>().preserveAspect = true;
        go.AddComponent<VolumeIcon>().Track(follows);
    }

    // The controls diagram, small. HowToPlay loads the artwork and opens the enlarged copy on click.
    void MakeHowToPlay(Transform parent, Vector2 pos)
    {
        var go = new GameObject("HowToPlayButton", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(HowToPlay));
        go.transform.SetParent(parent, false);
        RT(go, pos, new Vector2(150f, 150f));
    }

    Toggle MakeToggle(Transform parent, Vector2 pos, string label)
    {
        var go = DefaultControls.CreateToggle(new DefaultControls.Resources());
        go.name = "PauseFullscreenToggle";
        go.transform.SetParent(parent, false);
        RT(go, pos, new Vector2(340f, 36f));
        var t = go.GetComponent<Toggle>();
        var lbl = go.GetComponentInChildren<Text>(true);
        if (lbl != null) { lbl.text = label; lbl.font = font; lbl.fontSize = 30; lbl.color = Color.white; }
        return t;
    }

    // ---- tiny helpers ---------------------------------------------------

    // Sheets in Assets/Resources, each sliced into an idle (_0) and clicked (_1) frame — except the
    // volume sheet, which is a track (_0) plus a knob (_1). See SpriteButton / SpriteSlider.
    const string QuitButtonSheet = "quit button";
    const string PlayButtonSheet = "play button";
    const string PauseButtonSheet = "pause button";
    // The options gear: eight frames of a full turn, looped by AnimatedSpriteButton.
    const string OptionsButtonSheet = "options icon";
    const string VolumeSliderSheet = "volume slider";
    // Body of the volume slider. Wide and tall enough that the 3x-scaled track and knob have room
    // and the control is comfortable to grab with the mouse.
    static readonly Vector2 VolumeSliderSize = new Vector2(480f, 48f);

    // On-screen height of an artwork button; the width follows from the frame's own aspect so the
    // pixel art is never stretched.
    const float SpriteButtonHeight = 100f;
    const float HudButtonHeight = 70f; // the corner pause button, smaller than a menu button

    // A button whose artwork replaces the label entirely. SpriteButton pulls the two frames from
    // the sheet and wires the pressed-state swap; the click action is passed in as usual.
    void SpriteBtn(Transform parent, string name, string sheet, Vector2 pos, UnityAction onClick)
    {
        var go = Child(name, parent);
        var img = go.AddComponent<Image>();
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);

        go.AddComponent<SpriteButton>().Configure(sheet);
        RT(go, pos, new Vector2(SpriteButtonHeight * SpriteAspect(img), SpriteButtonHeight));
    }

    // Same as SpriteBtn, but the artwork loops through every frame on the sheet instead of holding
    // a single idle one. For the options gear, which turns on its own. See AnimatedSpriteButton for
    // why it can't just be a SpriteButton with more frames.
    void AnimatedSpriteBtn(Transform parent, string name, string sheet, Vector2 pos, UnityAction onClick)
    {
        var go = Child(name, parent);
        var img = go.AddComponent<Image>();
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);

        go.AddComponent<AnimatedSpriteButton>().Configure(sheet);
        RT(go, pos, new Vector2(SpriteButtonHeight * SpriteAspect(img), SpriteButtonHeight));
    }

    // Width-to-height ratio of whatever frame the skin loaded, so a rect can be sized to the art.
    static float SpriteAspect(Image img) =>
        img.sprite != null && img.sprite.rect.height > 0f ? img.sprite.rect.width / img.sprite.rect.height : 1f;

    void Btn(Transform parent, string label, Vector2 pos, UnityAction onClick)
    {
        var go = Child(label.Replace(" ", "") + "Button", parent);
        RT(go, pos, new Vector2(320f, 74f));
        var img = go.AddComponent<Image>();
        img.color = new Color(0.86f, 0.86f, 0.92f, 1f);
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener(onClick);
        var txtGO = Child("Text", go.transform);
        Stretch(txtGO);
        var t = txtGO.AddComponent<Text>();
        t.text = label; t.font = font; t.fontSize = 32; t.color = new Color(0.1f, 0.1f, 0.15f);
        t.alignment = TextAnchor.MiddleCenter; t.raycastTarget = false;
    }

    Text Label(Transform parent, string content, Vector2 pos, Vector2 size, int fontSize, Color color, TextAnchor align)
    {
        var go = Child("Text", parent);
        RT(go, pos, size);
        var t = go.AddComponent<Text>();
        t.text = content; t.font = font; t.fontSize = fontSize; t.color = color; t.alignment = align; t.raycastTarget = false;
        return t;
    }

    static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static RectTransform RT(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }
}
