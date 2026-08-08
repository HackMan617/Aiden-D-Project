using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// The in-game level editor — a Mario-Maker-style board you paint with a palette of tools, tune,
// save, and drop straight into the game.
//
// It is spawned by LevelGrid when the scene loads in GameSession.Mode.Edit, and lives on the same
// GameObject so it can borrow LevelGrid's already-assigned art (tile sheet, start / win markers,
// obstacle frames, sawblade sheet) instead of duplicating all that wiring in a second scene. The
// whole UI is built in code at runtime, like every other screen in this project.
//
//   palette    Floor / Wall / Hazard / Start / Goal / Saw / Erase, click or drag to paint
//   board      up to 15x9 — the size the camera frames in play
//   sawblades  a lane is a whole row (that's how the blades sweep); speed and spawn gap are sliders
//   save       named JSON files in the player's own save folder, via LevelStore
//   test play  builds the level for real and hands the player back here afterwards
public class LevelEditor : MonoBehaviour
{
    public enum Tool { Floor, Wall, Hazard, Start, Goal, Saw, Erase }

    // --- layout (1920x1080 reference canvas, origin at centre) ---------------
    const float GridAreaWidth = 1290f, GridAreaHeight = 560f;
    static readonly Vector2 GridAreaCentre = new Vector2(-190f, -70f);
    const float MinCellPx = 28f, MaxCellPx = 86f;

    static readonly Color PanelColor = new Color(0.16f, 0.17f, 0.22f, 0.95f);
    static readonly Color ButtonColor = new Color(0.26f, 0.28f, 0.36f, 1f);
    static readonly Color AccentColor = new Color(0.85f, 0.24f, 0.24f, 1f);
    static readonly Color GoColor = new Color(0.20f, 0.66f, 0.34f, 1f);
    static readonly Color ToolIdle = new Color(0.24f, 0.26f, 0.33f, 1f);
    static readonly Color ToolActive = new Color(0.95f, 0.80f, 0.25f, 1f);
    // Rows carrying a sawblade lane are tinted so the hazard reads at a glance.
    static readonly Color LaneTint = new Color(1f, 0.72f, 0.72f, 1f);

    LevelData level;
    Tool tool = Tool.Wall;

    // Art borrowed from LevelGrid. Any of these may be null if the matching sheet was never
    // assigned in the scene, so every use is guarded and falls back to a flat colour.
    Sprite floorSprite, wallSprite, hazardSprite, startSprite, goalSprite, sawSprite;

    Font font;
    GameObject canvasGO;
    RectTransform boardRoot;
    Image[,] cellBg;
    Image[,] cellIcon;
    float cellPx;

    readonly Dictionary<Tool, Image> toolBackgrounds = new Dictionary<Tool, Image>();
    InputField nameField;
    Text statusText, speedText, intervalText, widthText, heightText, pageText;
    Slider speedSlider, intervalSlider;

    GameObject browserPanel;
    RectTransform browserRows;
    List<string> browserNames = new List<string>();
    int browserPage;
    const int BrowserPageSize = 6;

    float statusClearAt;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Reopen whatever was being built before a test play; otherwise start from a blank board.
        level = GameSession.EditorDraft ?? LevelData.CreateDefault();
        level.Validate();

        SuspendGameplay();
        BorrowSprites();
        BuildUI();
        RebuildBoard();
        SelectTool(tool);
    }

    // The editor shares SampleScene with the game, so anything that would fight it for input or
    // draw over it is switched off. Nothing needs restoring: changing mode always reloads the scene.
    void SuspendGameplay()
    {
        Time.timeScale = 1f;

        GameObject player = GameObject.Find("Player");
        if (player != null) player.SetActive(false);

        // Its left-drag pan reads the mouse directly, which would pan the world while painting.
        if (Camera.main != null)
        {
            var camControl = Camera.main.GetComponent<CameraController>();
            if (camControl != null) camControl.enabled = false;
        }

        // PauseMenu and GameOverManager stand themselves down in edit mode (they check
        // GameSession.IsEditing in their own Awake), so Escape and the corner pause button are
        // already the editor's to use — nothing to switch off here.
    }

    // Slice the palette icons out of the same sheets the game plays with, so what you paint is
    // exactly what you get. LevelGrid exposes its runtime-sliced frames lazily, so this works even
    // though the grid itself never built a level in edit mode.
    void BorrowSprites()
    {
        var grid = GetComponent<LevelGrid>();
        if (grid == null) return;

        Sprite[] tileFrames = grid.TileFrames;
        if (tileFrames != null && tileFrames.Length > 0)
        {
            floorSprite = tileFrames[0];                        // grey, untouched
            wallSprite = tileFrames[tileFrames.Length - 1];     // red, the "already painted" look
        }
        else floorSprite = wallSprite = grid.tileSprite;

        if (grid.obstacleFrames != null && grid.obstacleFrames.Length > 0)
            hazardSprite = grid.obstacleFrames[0];
        startSprite = grid.startMarkerSprite;
        goalSprite = grid.endMarkerSprite;

        Sprite[] sawFrames = grid.SawbladeFrames;
        if (sawFrames != null && sawFrames.Length > 0) sawSprite = sawFrames[0];
    }

    void Update()
    {
        if (statusText != null && statusClearAt > 0f && Time.unscaledTime >= statusClearAt)
        {
            statusText.text = string.Empty;
            statusClearAt = 0f;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame && browserPanel != null && browserPanel.activeSelf)
            browserPanel.SetActive(false);
    }

    // ---- painting ----------------------------------------------------------

    // Applies the active tool to one cell. `isDrag` is true when the pointer swept in with the
    // button held: tools that toggle (the saw lane) or move a unique marker only act on the
    // initial press, so a stroke can't flip a lane on and off as it crosses it.
    public void PaintCell(int x, int y, bool isDrag)
    {
        if (level == null || !level.InBounds(x, y)) return;
        if (browserPanel != null && browserPanel.activeSelf) return;

        switch (tool)
        {
            case Tool.Saw:
                if (isDrag) return;
                if (level.sawRows.Contains(y)) level.sawRows.Remove(y);
                else { level.sawRows.Add(y); level.sawRows.Sort(); }
                break;

            case Tool.Start:
                if (isDrag) return;
                if (level.IsGoal(x, y)) { Status("The goal tile is already there."); return; }
                level.Set(x, y, LevelTile.Floor); // the spawn always sits on plain floor
                level.startX = x; level.startY = y;
                break;

            case Tool.Goal:
                if (isDrag) return;
                if (level.IsStart(x, y)) { Status("That's the spawn tile."); return; }
                level.Set(x, y, LevelTile.Floor);
                level.endX = x; level.endY = y;
                break;

            case Tool.Wall:
            case Tool.Hazard:
                // A wall would seal the marker in and a hazard would kill on contact, so refuse
                // rather than silently producing an unplayable level.
                if (level.IsStart(x, y) || level.IsGoal(x, y))
                {
                    if (!isDrag) Status("Move the start or goal marker first.");
                    return;
                }
                level.Set(x, y, tool == Tool.Wall ? LevelTile.Wall : LevelTile.Hazard);
                break;

            case Tool.Floor:
                level.Set(x, y, LevelTile.Floor);
                break;

            case Tool.Erase:
                level.Set(x, y, LevelTile.Floor);
                if (!isDrag) level.sawRows.Remove(y); // a deliberate click also clears the lane
                break;
        }

        RefreshCells();
    }

    void SelectTool(Tool next)
    {
        tool = next;
        foreach (var pair in toolBackgrounds)
            pair.Value.color = pair.Key == tool ? ToolActive : ToolIdle;
    }

    // ---- board -------------------------------------------------------------

    // Rebuilds every cell widget. Called on load and whenever the board is resized; ordinary
    // painting only re-tints through RefreshCells.
    void RebuildBoard()
    {
        ClearChildren(boardRoot);

        // Fit the board to the drawing area — small boards get chunky cells, the full 15x9 board
        // still fits without spilling into the side panel.
        cellPx = Mathf.Floor(Mathf.Min(GridAreaWidth / level.width, GridAreaHeight / level.height));
        cellPx = Mathf.Clamp(cellPx, MinCellPx, MaxCellPx);

        cellBg = new Image[level.width, level.height];
        cellIcon = new Image[level.width, level.height];

        float boardW = level.width * cellPx, boardH = level.height * cellPx;
        for (int x = 0; x < level.width; x++)
            for (int y = 0; y < level.height; y++)
            {
                var cellGO = Child($"Cell_{x}_{y}", boardRoot);
                // Cell (0,0) is bottom-left, matching LevelGrid's world layout, so the board you
                // paint is oriented the same way as the level you play.
                Place(cellGO, new Vector2((x + 0.5f) * cellPx - boardW * 0.5f,
                                          (y + 0.5f) * cellPx - boardH * 0.5f),
                              new Vector2(cellPx - 2f, cellPx - 2f));

                var bg = cellGO.AddComponent<Image>();
                bg.sprite = floorSprite;
                cellBg[x, y] = bg;

                var iconGO = Child("Icon", cellGO.transform);
                Place(iconGO, Vector2.zero, new Vector2(cellPx * 0.82f, cellPx * 0.82f));
                var icon = iconGO.AddComponent<Image>();
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                cellIcon[x, y] = icon;

                var hit = cellGO.AddComponent<LevelEditorCell>();
                hit.editor = this;
                hit.cellX = x;
                hit.cellY = y;
            }

        RefreshCells();
        if (widthText != null) widthText.text = level.width.ToString();
        if (heightText != null) heightText.text = level.height.ToString();
    }

    // Repaints the existing cell widgets from the level data.
    void RefreshCells()
    {
        if (cellBg == null) return;

        for (int x = 0; x < level.width; x++)
            for (int y = 0; y < level.height; y++)
            {
                LevelTile t = level.Get(x, y);
                bool lane = level.sawRows.Contains(y);

                Image bg = cellBg[x, y];
                bool wall = t == LevelTile.Wall;
                bg.sprite = wall ? wallSprite : floorSprite;
                // Without a tile sheet the sprites are null, so carry the state in the colour.
                Color baseColor = bg.sprite != null
                    ? Color.white
                    : (wall ? new Color(0.80f, 0.18f, 0.18f) : new Color(0.62f, 0.64f, 0.68f));
                bg.color = lane ? baseColor * LaneTint : baseColor;

                // One icon per cell, most specific first: the two unique markers outrank a hazard,
                // and the blade lane only shows where nothing else is drawn.
                Sprite iconSprite = null;
                float iconAlpha = 1f;
                if (level.IsStart(x, y)) iconSprite = startSprite;
                else if (level.IsGoal(x, y)) iconSprite = goalSprite;
                else if (t == LevelTile.Hazard) iconSprite = hazardSprite;
                else if (lane) { iconSprite = sawSprite; iconAlpha = 0.55f; }

                Image icon = cellIcon[x, y];
                icon.sprite = iconSprite;
                icon.enabled = iconSprite != null;
                icon.color = new Color(1f, 1f, 1f, iconAlpha);
            }
    }

    void ResizeBoard(int deltaWidth, int deltaHeight)
    {
        int w = Mathf.Clamp(level.width + deltaWidth, LevelData.MinWidth, LevelData.MaxWidth);
        int h = Mathf.Clamp(level.height + deltaHeight, LevelData.MinHeight, LevelData.MaxHeight);
        if (w == level.width && h == level.height) return;

        level.Resize(w, h);
        RebuildBoard();
    }

    void ClearBoard()
    {
        var blank = LevelData.CreateDefault();
        // Keep the name and the blade tuning — clearing is about wiping the layout, not the level.
        blank.levelName = level.levelName;
        blank.sawSpeed = level.sawSpeed;
        blank.sawInterval = level.sawInterval;
        blank.Resize(level.width, level.height);
        blank.endX = blank.width - 1;
        blank.endY = blank.height - 1;
        blank.Validate();

        level = blank;
        RebuildBoard();
        Status("Board cleared.");
    }

    // ---- save / load / play -------------------------------------------------

    void SaveLevel()
    {
        level.levelName = nameField != null ? nameField.text : level.levelName;
        if (LevelStore.Save(level, out string error))
        {
            if (nameField != null) nameField.SetTextWithoutNotify(level.levelName); // show it sanitized
            Status($"Saved \"{level.levelName}\".");
        }
        else Status(error);
    }

    void TestPlay()
    {
        level.levelName = nameField != null ? nameField.text : level.levelName;
        level.Validate();

        // A level whose goal can't be walked to is simply broken, so catch it here rather than
        // letting the player discover it by pacing the board.
        if (!level.GoalIsReachable())
        {
            Status("No route from the spawn to the goal — clear a path first.");
            return;
        }

        // Two independent copies: one for the game to build, one for the editor to come back to.
        GameSession.PlayCustom(level.Clone(), level.Clone());
    }

    void ExitEditor() => GameSession.ExitToLevelSelect();

    // ---- the saved-level browser -------------------------------------------

    void OpenBrowser()
    {
        browserNames = LevelStore.ListLevels();
        browserPage = 0;
        RefreshBrowser();
        browserPanel.SetActive(true);
    }

    void RefreshBrowser()
    {
        ClearChildren(browserRows);

        int pageCount = Mathf.Max(1, Mathf.CeilToInt(browserNames.Count / (float)BrowserPageSize));
        browserPage = Mathf.Clamp(browserPage, 0, pageCount - 1);
        pageText.text = browserNames.Count == 0 ? "" : $"Page {browserPage + 1} / {pageCount}";

        if (browserNames.Count == 0)
        {
            var empty = Label(browserRows, "No saved levels yet — build one and press Save.",
                              new Vector2(0f, 120f), new Vector2(900f, 60f), 30, new Color(0.8f, 0.8f, 0.85f));
            empty.alignment = TextAnchor.MiddleCenter;
            return;
        }

        int first = browserPage * BrowserPageSize;
        int last = Mathf.Min(first + BrowserPageSize, browserNames.Count);
        for (int i = first; i < last; i++)
        {
            string name = browserNames[i]; // captured per row for the button callbacks
            var row = Child("Row" + i, browserRows);
            Place(row, new Vector2(0f, 250f - (i - first) * 86f), new Vector2(960f, 76f));
            row.AddComponent<Image>().color = new Color(0.22f, 0.24f, 0.30f, 1f);

            var label = Label(row.transform, name, new Vector2(-260f, 0f), new Vector2(420f, 60f), 30, Color.white);
            label.alignment = TextAnchor.MiddleLeft;

            MakeButton(row.transform, "Edit", new Vector2(150f, 0f), new Vector2(140f, 60f), ButtonColor,
                   () => LoadIntoEditor(name), 26);
            MakeButton(row.transform, "Play", new Vector2(300f, 0f), new Vector2(140f, 60f), GoColor,
                   () => PlaySaved(name), 26);
            MakeButton(row.transform, "X", new Vector2(410f, 0f), new Vector2(70f, 60f), AccentColor,
                   () => DeleteSaved(name), 28);
        }
    }

    void LoadIntoEditor(string name)
    {
        LevelData loaded = LevelStore.Load(name);
        if (loaded == null) { Status($"Could not open \"{name}\"."); return; }

        level = loaded;
        if (nameField != null) nameField.SetTextWithoutNotify(level.levelName);
        if (speedSlider != null) speedSlider.SetValueWithoutNotify(level.sawSpeed);
        if (intervalSlider != null) intervalSlider.SetValueWithoutNotify(level.sawInterval);
        RefreshSawLabels();
        RebuildBoard();
        browserPanel.SetActive(false);
        Status($"Opened \"{level.levelName}\".");
    }

    // Loading a saved level into the game IS opening it in the editor and starting a test play, so
    // that quitting the level lands back on something the player can immediately keep editing.
    void PlaySaved(string name)
    {
        LoadIntoEditor(name);
        if (level != null && level.levelName == LevelStore.Sanitize(name)) TestPlay();
    }

    void DeleteSaved(string name)
    {
        if (LevelStore.Delete(name)) Status($"Deleted \"{name}\".");
        browserNames = LevelStore.ListLevels();
        RefreshBrowser();
    }

    void Status(string message)
    {
        if (statusText == null) return;
        statusText.text = message;
        statusClearAt = Time.unscaledTime + 4f;
    }

    // ---- UI construction ---------------------------------------------------

    void BuildUI()
    {
        // UI clicks need an EventSystem running the Input-System module (this project is
        // Input-System only); the game scene may not have built one yet in edit mode.
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        canvasGO = new GameObject("LevelEditorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300; // above every other overlay in the scene
        var scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = Child("Backdrop", canvasGO.transform);
        Stretch(backdrop);
        backdrop.AddComponent<Image>().color = new Color(0.09f, 0.10f, 0.14f, 0.93f);

        BuildHeader();
        BuildPalette();
        BuildBoardArea();
        BuildSidePanel();
        BuildFooter();
        BuildBrowser();
    }

    void BuildHeader()
    {
        var title = Label(canvasGO.transform, "LEVEL EDITOR", new Vector2(-580f, 458f),
                          new Vector2(720f, 80f), 56, ToolActive);
        title.alignment = TextAnchor.MiddleLeft;
        title.fontStyle = FontStyle.Bold;

        var nameLabel = Label(canvasGO.transform, "NAME", new Vector2(240f, 458f),
                              new Vector2(140f, 50f), 30, Color.white);
        nameLabel.alignment = TextAnchor.MiddleRight;

        var fieldGO = DefaultControls.CreateInputField(new DefaultControls.Resources());
        fieldGO.name = "LevelNameField";
        fieldGO.transform.SetParent(canvasGO.transform, false);
        Place(fieldGO, new Vector2(590f, 458f), new Vector2(560f, 62f));
        nameField = fieldGO.GetComponent<InputField>();
        nameField.characterLimit = LevelStore.MaxNameLength;
        nameField.text = level.levelName;
        foreach (Text t in fieldGO.GetComponentsInChildren<Text>(true))
        {
            t.font = font;
            t.fontSize = 30;
        }
    }

    void BuildPalette()
    {
        // Tool, caption, icon. Erase has no art of its own, so it draws as a bare marked square.
        var tools = new (Tool tool, string caption, Sprite icon)[]
        {
            (Tool.Floor,  "Floor",  floorSprite),
            (Tool.Wall,   "Wall",   wallSprite),
            (Tool.Hazard, "Hazard", hazardSprite),
            (Tool.Start,  "Start",  startSprite),
            (Tool.Goal,   "Goal",   goalSprite),
            (Tool.Saw,    "Saw",    sawSprite),
            (Tool.Erase,  "Erase",  null),
        };

        const float spacing = 116f, buttonSize = 100f, rowY = 356f;
        float firstX = -(tools.Length - 1) * spacing * 0.5f;

        for (int i = 0; i < tools.Length; i++)
        {
            var entry = tools[i];
            Tool captured = entry.tool; // captured per button for the click callback

            var go = Child("Tool" + entry.tool, canvasGO.transform);
            Place(go, new Vector2(firstX + i * spacing, rowY), new Vector2(buttonSize, buttonSize));
            var bg = go.AddComponent<Image>();
            bg.color = ToolIdle;
            var button = go.AddComponent<Button>();
            button.targetGraphic = bg;
            button.onClick.AddListener(() => SelectTool(captured));
            toolBackgrounds[entry.tool] = bg;

            if (entry.icon != null)
            {
                var iconGO = Child("Icon", go.transform);
                Place(iconGO, Vector2.zero, new Vector2(buttonSize * 0.7f, buttonSize * 0.7f));
                var icon = iconGO.AddComponent<Image>();
                icon.sprite = entry.icon;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
            }
            else
            {
                // Erase has no art of its own; any other missing icon means that sheet was never
                // assigned in the scene, so fall back to the caption's initial rather than a blank.
                string mark = entry.tool == Tool.Erase ? "X" : entry.caption.Substring(0, 1);
                var markText = Label(go.transform, mark, Vector2.zero, new Vector2(buttonSize, buttonSize), 46,
                                     entry.tool == Tool.Erase ? AccentColor : Color.white);
                markText.alignment = TextAnchor.MiddleCenter;
                markText.fontStyle = FontStyle.Bold;
            }

            var caption = Label(canvasGO.transform, entry.caption,
                                new Vector2(firstX + i * spacing, rowY - 68f),
                                new Vector2(spacing, 34f), 24, new Color(0.85f, 0.86f, 0.9f));
            caption.alignment = TextAnchor.MiddleCenter;
        }
    }

    void BuildBoardArea()
    {
        var frame = Child("BoardFrame", canvasGO.transform);
        Place(frame, GridAreaCentre, new Vector2(GridAreaWidth + 24f, GridAreaHeight + 24f));
        frame.AddComponent<Image>().color = new Color(0.13f, 0.14f, 0.19f, 1f);

        var board = Child("Board", frame.transform);
        Place(board, Vector2.zero, new Vector2(GridAreaWidth, GridAreaHeight));
        boardRoot = board.GetComponent<RectTransform>();
    }

    void BuildSidePanel()
    {
        var panel = Child("SidePanel", canvasGO.transform);
        Place(panel, new Vector2(700f, 20f), new Vector2(400f, 700f));
        panel.AddComponent<Image>().color = PanelColor;
        Transform p = panel.transform;

        Header(p, "SAWBLADES", 300f);

        // The blades sweep along whole rows, so speed and spacing are level-wide settings; which
        // rows they sweep is painted on the board with the Saw tool.
        Label(p, "Speed", new Vector2(-110f, 245f), new Vector2(180f, 44f), 28, Color.white)
            .alignment = TextAnchor.MiddleLeft;
        speedText = Label(p, "", new Vector2(110f, 245f), new Vector2(160f, 44f), 28, ToolActive);
        speedText.alignment = TextAnchor.MiddleRight;
        speedSlider = MakeSlider(p, new Vector2(0f, 205f), LevelData.MinSawSpeed, LevelData.MaxSawSpeed, level.sawSpeed);
        speedSlider.onValueChanged.AddListener(v => { level.sawSpeed = v; RefreshSawLabels(); });

        Label(p, "Spawn gap", new Vector2(-110f, 150f), new Vector2(180f, 44f), 28, Color.white)
            .alignment = TextAnchor.MiddleLeft;
        intervalText = Label(p, "", new Vector2(110f, 150f), new Vector2(160f, 44f), 28, ToolActive);
        intervalText.alignment = TextAnchor.MiddleRight;
        intervalSlider = MakeSlider(p, new Vector2(0f, 110f), LevelData.MinSawInterval, LevelData.MaxSawInterval, level.sawInterval);
        intervalSlider.onValueChanged.AddListener(v => { level.sawInterval = v; RefreshSawLabels(); });

        RefreshSawLabels();

        Header(p, "BOARD SIZE", 40f);
        widthText = Stepper(p, "Width", -10f, () => ResizeBoard(-1, 0), () => ResizeBoard(1, 0));
        heightText = Stepper(p, "Height", -80f, () => ResizeBoard(0, -1), () => ResizeBoard(0, 1));

        MakeButton(p, "Clear Board", new Vector2(0f, -170f), new Vector2(300f, 64f), ButtonColor, ClearBoard, 28);

        var hint = Label(p, "Click or drag to paint.\nThe Saw tool toggles a whole row.",
                         new Vector2(0f, -270f), new Vector2(360f, 100f), 22, new Color(0.72f, 0.74f, 0.8f));
        hint.alignment = TextAnchor.MiddleCenter;
    }

    void BuildFooter()
    {
        statusText = Label(canvasGO.transform, "", new Vector2(GridAreaCentre.x, -396f),
                           new Vector2(1300f, 46f), 28, ToolActive);
        statusText.alignment = TextAnchor.MiddleCenter;

        var size = new Vector2(300f, 80f);
        MakeButton(canvasGO.transform, "Save", new Vector2(-520f, -462f), size, ButtonColor, SaveLevel, 32);
        MakeButton(canvasGO.transform, "My Levels", new Vector2(-175f, -462f), size, ButtonColor, OpenBrowser, 32);
        // Test Play is the play artwork — the same green button the rest of the game starts with.
        MakeSpriteButton(canvasGO.transform, "TestPlayButton", PlayButtonSheet, new Vector2(170f, -462f), 88f, TestPlay);
        MakeButton(canvasGO.transform, "Back", new Vector2(515f, -462f), size, AccentColor, ExitEditor, 32);
    }

    void BuildBrowser()
    {
        browserPanel = Child("LevelBrowser", canvasGO.transform);
        Stretch(browserPanel);
        browserPanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.8f);

        var panel = Child("Panel", browserPanel.transform);
        Place(panel, Vector2.zero, new Vector2(1040f, 800f));
        panel.AddComponent<Image>().color = PanelColor;

        var title = Label(panel.transform, "MY LEVELS", new Vector2(0f, 340f), new Vector2(800f, 70f), 46, ToolActive);
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;

        var rows = Child("Rows", panel.transform);
        Stretch(rows);
        browserRows = rows.GetComponent<RectTransform>();

        MakeButton(panel.transform, "Prev", new Vector2(-330f, -260f), new Vector2(160f, 60f), ButtonColor,
               () => { browserPage--; RefreshBrowser(); }, 26);
        pageText = Label(panel.transform, "", new Vector2(0f, -260f), new Vector2(320f, 60f), 26, Color.white);
        pageText.alignment = TextAnchor.MiddleCenter;
        MakeButton(panel.transform, "Next", new Vector2(330f, -260f), new Vector2(160f, 60f), ButtonColor,
               () => { browserPage++; RefreshBrowser(); }, 26);

        MakeButton(panel.transform, "Close", new Vector2(0f, -345f), new Vector2(240f, 64f), AccentColor,
               () => browserPanel.SetActive(false), 28);

        browserPanel.SetActive(false);
    }

    void RefreshSawLabels()
    {
        if (speedText != null) speedText.text = level.sawSpeed.ToString("0.0") + " u/s";
        if (intervalText != null) intervalText.text = level.sawInterval.ToString("0.0") + " s";
    }

    // ---- tiny UI builders --------------------------------------------------

    void Header(Transform parent, string caption, float y)
    {
        var t = Label(parent, caption, new Vector2(0f, y), new Vector2(360f, 50f), 30, new Color(0.62f, 0.66f, 0.78f));
        t.alignment = TextAnchor.MiddleCenter;
        t.fontStyle = FontStyle.Bold;
    }

    // A "label  [-] value [+]" row; returns the value Text so the caller can keep it up to date.
    Text Stepper(Transform parent, string caption, float y, UnityAction onMinus, UnityAction onPlus)
    {
        Label(parent, caption, new Vector2(-110f, y), new Vector2(160f, 50f), 28, Color.white)
            .alignment = TextAnchor.MiddleLeft;
        MakeButton(parent, "-", new Vector2(40f, y), new Vector2(56f, 56f), ButtonColor, onMinus, 32);
        var value = Label(parent, "", new Vector2(105f, y), new Vector2(60f, 50f), 30, ToolActive);
        value.alignment = TextAnchor.MiddleCenter;
        MakeButton(parent, "+", new Vector2(170f, y), new Vector2(56f, 56f), ButtonColor, onPlus, 32);
        return value;
    }

    Slider MakeSlider(Transform parent, Vector2 pos, float min, float max, float value)
    {
        var go = DefaultControls.CreateSlider(new DefaultControls.Resources());
        go.transform.SetParent(parent, false);
        Place(go, pos, new Vector2(340f, 26f));
        var slider = go.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        return slider;
    }

    // Sheet in Assets/Resources holding the play artwork's idle (_0) and clicked (_1) frames.
    const string PlayButtonSheet = "play button";

    // A button drawn from a sprite sheet instead of a coloured box with a label. SpriteButton loads
    // the frames and handles the pressed swap; the width follows the art's own aspect.
    Button MakeSpriteButton(Transform parent, string name, string sheet, Vector2 pos, float height, UnityAction onClick)
    {
        var go = Child(name, parent);
        var img = go.AddComponent<Image>();
        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        go.AddComponent<SpriteButton>().Configure(sheet);

        float aspect = img.sprite != null && img.sprite.rect.height > 0f
            ? img.sprite.rect.width / img.sprite.rect.height
            : 1f;
        Place(go, pos, new Vector2(height * aspect, height));
        return button;
    }

    Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color, UnityAction onClick, int fontSize)
    {
        var go = Child(label.Replace(" ", "") + "Button", parent);
        Place(go, pos, size);
        var img = go.AddComponent<Image>();
        img.color = color;
        var button = go.AddComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(onClick);

        var text = Label(go.transform, label, Vector2.zero, size, fontSize, Color.white);
        text.alignment = TextAnchor.MiddleCenter;
        return button;
    }

    Text Label(Transform parent, string content, Vector2 pos, Vector2 size, int fontSize, Color color)
    {
        var go = Child("Text", parent);
        Place(go, pos, size);
        var text = go.AddComponent<Text>();
        text.text = content;
        text.font = font;
        text.fontSize = fontSize;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        return text;
    }

    // Tears down a rebuilt section (the board on resize, the browser list on refresh). Destroy is
    // deferred to the end of the frame, so the outgoing widgets are deactivated first — otherwise
    // they would spend the rest of this frame as clickable hit targets carrying stale coordinates.
    static void ClearChildren(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }

    static GameObject Child(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return go;
    }

    static void Place(GameObject go, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static void Stretch(GameObject go)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
