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

    void Awake()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
    }

    void Update()
    {
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
        GameObject ls = GameObject.Find("LevelSelectCanvas"); // only found while it's active
        return !(ls != null && ls.activeInHierarchy);
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
        Btn(mainPanel.transform, "Resume", new Vector2(0f, 110f), Resume);
        Btn(mainPanel.transform, "Options", new Vector2(0f, 10f), ShowOptions);
        Btn(mainPanel.transform, "Main Menu", new Vector2(0f, -90f), GoMainMenu);
        Btn(mainPanel.transform, "Quit", new Vector2(0f, -190f), QuitGame);

        optionsPanel = Child("PauseOptions", canvasGO.transform);
        Stretch(optionsPanel);
        Label(optionsPanel.transform, "OPTIONS", new Vector2(0f, 280f), new Vector2(800f, 110f), 72, Color.white, TextAnchor.MiddleCenter);
        Label(optionsPanel.transform, "Volume", new Vector2(-260f, 100f), new Vector2(240f, 50f), 34, Color.white, TextAnchor.MiddleLeft);
        volumeSlider = MakeSlider(optionsPanel.transform, new Vector2(90f, 100f));
        volumeSlider.onValueChanged.AddListener(v => AudioListener.volume = v);
        MakeMuteButton(optionsPanel.transform, new Vector2(350f, 100f)); // just right of the slider
        fullscreenToggle = MakeToggle(optionsPanel.transform, new Vector2(-90f, 0f), "Fullscreen");
        fullscreenToggle.onValueChanged.AddListener(f => Screen.fullScreen = f);
        Btn(optionsPanel.transform, "Back", new Vector2(0f, -170f), ShowMain);

        canvasGO.SetActive(false);
    }

    Slider MakeSlider(Transform parent, Vector2 pos)
    {
        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.name = "PauseVolumeSlider";
        go.transform.SetParent(parent, false);
        RT(go, pos, new Vector2(360f, 26f));
        var s = go.GetComponent<Slider>();
        s.minValue = 0f; s.maxValue = 1f; s.value = 1f;
        return s;
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
