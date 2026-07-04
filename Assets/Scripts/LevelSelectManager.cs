using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

// Level-select start screen. When the game starts it freezes gameplay and shows the "WORLD 1"
// title with a 5x2 grid of ten level boxes. WASD (or the arrow keys) move the RED selection box
// around the grid. Only level 1 (the current level) is accessible — press Enter or Space on it
// to start playing; the other levels are locked. (No on-screen control hints, by request.)
//
// Everything (canvas, title, boxes, numbers) is built in code at runtime. The three graphics come
// from the "aiden d world 1 selection" sheet (256x64: WORLD text + grey box + red box), sliced by
// the rects below (found by scanning the art).
public class LevelSelectManager : MonoBehaviour
{
    [Tooltip("The 'aiden d world 1 selection' texture (256x64: WORLD text, grey box, red box).")]
    public Texture2D selectionSheet;
    [Tooltip("How many level boxes to show.")]
    public int levelCount = 10;
    [Tooltip("Boxes per row.")]
    public int columns = 5;

    // Sub-sprite rects within the 256x64 sheet (Unity texture coords, y up from bottom).
    static readonly Rect TitleRect = new Rect(2f, 28f, 52f, 34f);
    static readonly Rect GreyRect = new Rect(137f, 7f, 46f, 43f);
    static readonly Rect RedRect = new Rect(196f, 7f, 50f, 43f);

    Sprite titleSprite, greyBox, redBox;
    Image[] boxes;
    int index;
    GameObject canvasGO;
    bool started;

    void Start()
    {
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

        // "WORLD 1" title near the top.
        var titleGO = Child("Title", canvasGO.transform);
        var titleImg = titleGO.AddComponent<Image>();
        titleImg.sprite = titleSprite;
        titleImg.preserveAspect = true;
        var trt = titleGO.GetComponent<RectTransform>();
        trt.sizeDelta = new Vector2(620f, 240f);
        trt.anchoredPosition = new Vector2(0f, 290f);

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
    }

    void Update()
    {
        if (started) return;
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
            if (index == 0) StartGame(); // only level 1 is accessible
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
        started = true;
        Time.timeScale = 1f;
        canvasGO.SetActive(false);
    }

    // --- helpers ---------------------------------------------------------
    static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
