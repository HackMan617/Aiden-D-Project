using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.UI;

// Shows the game-over sequence: when the player hits a hazard, the screen freezes and the
// game_over sprite-sheet animation loops as a full-screen overlay, with Retry / Quit buttons
// appearing after the first pass. Also shows a small always-on Retry button in the top-left
// corner so the player can restart at any time (e.g. if they box themselves in with red tiles).
//
// The whole UI (canvases, image, buttons, and an Input-System EventSystem) is built in
// code at runtime so nothing has to be wired up by hand in the scene — the only thing
// to assign in the Inspector is the game-over sheet texture.
public class GameOverManager : MonoBehaviour
{
    static GameOverManager _instance;
    // Lazily re-finds the manager if the cached reference was cleared (e.g. by an editor
    // domain reload during play). Keeps collision detection robust against static-reset quirks.
    public static GameOverManager Instance
    {
        get
        {
            if (_instance == null) _instance = FindAnyObjectByType<GameOverManager>();
            return _instance;
        }
    }

    [Tooltip("The game_over_sprite.png texture (448x64 = 7 frames of 64x64).")]
    public Texture2D gameOverSheet;
    [Tooltip("How many equal-width frames the sheet is divided into.")]
    public int frameCount = 7;
    [Tooltip("Playback speed of the game-over animation, in frames per second.")]
    public float fps = 8f;

    public bool IsGameOver { get; private set; }

    Sprite[] frames;
    GameObject canvasGO;
    Image animImage;
    GameObject buttonsRow;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        BuildFrames();
        BuildUI();
        BuildHud();
    }

    // Slice the 448x64 sheet into `frameCount` equal frames in memory (no asset re-import).
    void BuildFrames()
    {
        if (gameOverSheet == null)
        {
            Debug.LogError("[GameOverManager] gameOverSheet texture is not assigned.");
            return;
        }
        frames = new Sprite[frameCount];
        float fw = gameOverSheet.width / (float)frameCount; // 64
        float fh = gameOverSheet.height;                    // 64
        for (int i = 0; i < frameCount; i++)
            frames[i] = Sprite.Create(gameOverSheet, new Rect(i * fw, 0f, fw, fh),
                                      new Vector2(0.5f, 0.5f), fh);
    }

    void BuildUI()
    {
        // The Input System needs an EventSystem with the Input-System UI module for clicks.
        if (Object.FindFirstObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        canvasGO = new GameObject("GameOverCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100; // draw on top of everything
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Dim the frozen game behind the overlay.
        var dim = CreateChild("Dim", canvasGO.transform);
        var dimImg = dim.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.65f);
        StretchFull(dim.GetComponent<RectTransform>());

        // The animated game-over graphic, centred and lifted a little to leave room for buttons.
        var imgGO = CreateChild("GameOverImage", canvasGO.transform);
        animImage = imgGO.AddComponent<Image>();
        animImage.preserveAspect = true;
        var irt = imgGO.GetComponent<RectTransform>();
        irt.sizeDelta = new Vector2(440f, 440f);
        irt.anchoredPosition = new Vector2(0f, 90f);
        if (frames != null && frames.Length > 0) animImage.sprite = frames[0];

        // Retry / Quit, hidden until the animation finishes.
        buttonsRow = CreateChild("Buttons", canvasGO.transform);
        StretchFull(buttonsRow.GetComponent<RectTransform>());
        CreateButton("RetryButton", "Retry", buttonsRow.transform, new Vector2(-160f, -210f), RetryGame);
        CreateButton("QuitButton", "Quit", buttonsRow.transform, new Vector2(160f, -210f), QuitGame);

        canvasGO.SetActive(false); // shown only on game over
    }

    // A small, always-visible "Retry" button in the top-left corner so the player can restart
    // at any time (handy if they box themselves in with the red-tile walls). It lives on its own
    // canvas below the game-over overlay, so the overlay covers it during the game-over screen.
    void BuildHud()
    {
        var hudGO = new GameObject("HudCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = hudGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50; // below the game-over overlay (100)
        var scaler = hudGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var go = CreateChild("HudRetryButton", hudGO.transform);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // top-left corner
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(24f, -24f);
        rt.sizeDelta = new Vector2(150f, 56f);

        var img = go.AddComponent<Image>();
        img.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(RetryGame);

        var txtGO = CreateChild("Text", go.transform);
        StretchFull(txtGO.GetComponent<RectTransform>());
        var txt = txtGO.AddComponent<Text>();
        txt.text = "Retry";
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = 26;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    // Called by LevelGrid when the player steps onto a hazard cell.
    public void TriggerGameOver()
    {
        if (IsGameOver) return;
        IsGameOver = true;

        canvasGO.SetActive(true);
        buttonsRow.SetActive(false);
        Time.timeScale = 0f; // freeze the game; the coroutine uses real time so it still animates
        StartCoroutine(PlayThenPrompt());
    }

    IEnumerator PlayThenPrompt()
    {
        if (frames == null || frames.Length == 0)
        {
            buttonsRow.SetActive(true);
            yield break;
        }

        float delay = 1f / Mathf.Max(1f, fps);
        bool prompted = false;
        // Loop the animation forever. After the first full play-through the Retry/Quit
        // prompt appears, and the animation keeps cycling behind it. The loop ends only
        // when Retry/Quit reloads the scene (which destroys this manager and its coroutine).
        while (true)
        {
            for (int i = 0; i < frames.Length; i++)
            {
                animImage.sprite = frames[i];
                yield return new WaitForSecondsRealtime(delay);
            }
            if (!prompted)
            {
                buttonsRow.SetActive(true); // now ask: retry or quit?
                prompted = true;
            }
        }
    }

    void RetryGame()
    {
        Time.timeScale = 1f; // timeScale survives scene loads, so restore it first
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false; // stop play mode while testing in the editor
#else
        Application.Quit();
#endif
    }

    // --- tiny UI builders ---------------------------------------------------
    static GameObject CreateChild(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void CreateButton(string name, string label, Transform parent, Vector2 pos, UnityAction onClick)
    {
        var go = CreateChild(name, parent);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(270f, 90f);
        rt.anchoredPosition = pos;

        var img = go.AddComponent<Image>();
        img.color = new Color(0.85f, 0.2f, 0.2f, 1f);
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(onClick);

        var txtGO = CreateChild("Text", go.transform);
        StretchFull(txtGO.GetComponent<RectTransform>());
        var txt = txtGO.AddComponent<Text>();
        txt.text = label;
        txt.alignment = TextAnchor.MiddleCenter;
        txt.color = Color.white;
        txt.fontSize = 38;
        txt.raycastTarget = false;
        txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
