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
// obstacle frames, sawblade sheet, turret / bullet / lift sheets) instead of duplicating all that
// wiring in a second scene. The whole UI is built in code at runtime, like every other screen in
// this project.
//
//   palette    Floor / Wall / Brick / Hazard / Water / Spawner / Elevator / Start / Goal / Saw /
//              Erase, click or drag
//   board      up to 15x9 — the size the camera frames in play
//   sawblades  a lane is a whole row (that's how the blades sweep)
//   spawners   a turret is one cell, and clicking it again turns it; how fast and how often every
//              turret in the level fires is one pair of sliders
//   elevators  a lift is one cell — where the platform starts before it rides the whole column
//   save       named JSON files in the player's own save folder, via LevelStore. The name is asked
//              for at save time, in a prompt, rather than sitting on the screen the whole session
//   test play  builds the level for real and hands the player back here afterwards
//
// Only three tools carry settings of their own (Saw, Spawner, Elevator), and the side panel shows
// exactly the one belonging to the tool in hand — see BuildSidePanel / SelectTool.
public class LevelEditor : MonoBehaviour
{
    public enum Tool { Floor, Wall, Brick, Hazard, Water, Spawner, Elevator, Start, Goal, Saw, Erase }

    // --- layout (1920x1080 reference canvas, origin at centre) ---------------
    const float GridAreaWidth = 1290f, GridAreaHeight = 560f;
    static readonly Vector2 GridAreaCentre = new Vector2(-190f, -70f);
    const float MinCellPx = 28f, MaxCellPx = 86f;

    // The settings panel down the right-hand side. It is centred in the strip left over between the
    // palette captions (which bottom out at y 271) and the footer's status line (top y -373), so it
    // sits on the screen edge without covering the board or crowding the Back button below it. The
    // palette row above it now runs the full width of the screen, so the two clear each other
    // vertically rather than horizontally.
    static readonly Vector2 SidePanelCentre = new Vector2(735f, -50f);
    static readonly Vector2 SidePanelSize = new Vector2(400f, 620f);

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
    Sprite floorSprite, wallSprite, brickSprite, hazardSprite, startSprite, goalSprite, sawSprite;
    Sprite spawnerSprite;

    // A set of Images all showing the same sheet, stepped together rather than one flipbook each, so
    // what the board shows reads as one thing: every pool ripples in step so the water is a single
    // body of water, every lift scrolls in step, every turret's bullet flickers in step. The palette
    // swatch rides along too, so a tool advertises exactly what it paints.
    class SpriteCycle
    {
        public Sprite[] frames;
        public float fps = 8f;
        public Image paletteIcon;
        public readonly List<Image> cells = new List<Image>();

        float timer;
        int index;

        public bool HasArt => frames != null && frames.Length > 0;
        public Sprite Current => HasArt ? frames[index % frames.Length] : null;

        // Unscaled time, so these keep moving even if something has parked the clock at zero while
        // the editor is up.
        public void Step(float deltaTime)
        {
            if (frames == null || frames.Length < 2 || fps <= 0f) return;

            timer += deltaTime;
            float frameTime = 1f / fps;
            if (timer < frameTime) return;
            while (timer >= frameTime)
            {
                timer -= frameTime;
                index = (index + 1) % frames.Length;
            }

            Sprite frame = frames[index];
            if (paletteIcon != null) paletteIcon.sprite = frame;
            foreach (Image cell in cells)
                if (cell != null) cell.sprite = frame;
        }
    }

    readonly SpriteCycle water = new SpriteCycle();
    readonly SpriteCycle lift = new SpriteCycle();
    readonly SpriteCycle bullet = new SpriteCycle();

    Font font;
    GameObject canvasGO;
    RectTransform boardRoot;
    Image[,] cellBg;
    Image[,] cellIcon;
    float cellPx;

    readonly Dictionary<Tool, Image> toolBackgrounds = new Dictionary<Tool, Image>();
    Text nameText, statusText, hintText;
    Text speedText, intervalText, bulletSpeedText, fireIntervalText, liftSpeedText;
    Text widthText, heightText, pageText;
    Slider speedSlider, intervalSlider, bulletSpeedSlider, fireIntervalSlider, liftSpeedSlider;

    // The side panel's per-tool sections, and the tool-independent block underneath that slides up
    // to fill their place while none of them is showing. Every section is the same height, so that
    // is one offset rather than one per tool. See BuildSidePanel / SelectTool.
    GameObject sawSection, spawnerSection, elevatorSection;
    RectTransform boardSection;
    const float ToolSectionHeight = 245f;

    GameObject browserPanel;
    RectTransform browserRows;
    List<string> browserNames = new List<string>();
    int browserPage;
    const int BrowserPageSize = 6;

    // The naming prompt. A level is only named when it is being saved, so an unnamed draft can be
    // built and test-played without ever answering the question.
    GameObject savePanel;
    InputField saveNameField;
    Text saveHintText;

    float statusClearAt;

    void Start()
    {
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Reopen whatever was being built before a test play; otherwise start from a blank board.
        level = GameSession.EditorDraft ?? LevelData.CreateDefault();
        level.Validate();

        SuspendGameplay();

        // Building has its own track. Test Play and Back both reload the scene, and whatever they
        // land on sets its own music, so this only ever plays while the editor is up.
        MusicManager.PlayEditorMusic();

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

        brickSprite = grid.BrickSprite;
        spawnerSprite = grid.SpawnerSprite;

        water.frames = grid.WaterFrames;
        water.fps = grid.waterFps;
        lift.frames = grid.ElevatorFrames;
        lift.fps = grid.elevatorFps;
        bullet.frames = grid.ProjectileFrames;
        bullet.fps = grid.projectileFps;
    }

    void Update()
    {
        if (statusText != null && statusClearAt > 0f && Time.unscaledTime >= statusClearAt)
        {
            statusText.text = string.Empty;
            statusClearAt = 0f;
        }

        Keyboard kb = Keyboard.current;
        if (kb != null)
        {
            // Escape backs out of whichever overlay is up; Enter commits the naming prompt, so a
            // name can be typed and saved without reaching for the mouse.
            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (SavePromptOpen) CloseSavePrompt();
                else if (browserPanel != null && browserPanel.activeSelf) browserPanel.SetActive(false);
            }
            else if (SavePromptOpen &&
                     (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                ConfirmSave();
        }

        float dt = Time.unscaledDeltaTime;
        water.Step(dt);
        lift.Step(dt);
        bullet.Step(dt);
    }

    // ---- painting ----------------------------------------------------------

    // Applies the active tool to one cell. `isDrag` is true when the pointer swept in with the
    // button held: tools that toggle (the saw lane), turn (a turret already placed) or move a unique
    // marker only act on the initial press, so a stroke can't flip a lane on and off as it crosses
    // it or spin every turret it passes over.
    public void PaintCell(int x, int y, bool isDrag)
    {
        if (level == null || !level.InBounds(x, y)) return;
        if (SavePromptOpen) return;
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

            case Tool.Spawner:
            {
                if (RefuseOnMarker(x, y, isDrag)) return;
                LevelTile here = level.Get(x, y);
                // A click on a turret that is already there aims it rather than repainting it, so
                // the one palette button both places and turns. A drag only ever places.
                if (LevelData.IsSpawner(here))
                {
                    if (isDrag) return;
                    level.Set(x, y, LevelData.RotateSpawner(here));
                }
                else level.Set(x, y, LevelTile.SpawnerRight);
                break;
            }

            case Tool.Wall:
            case Tool.Brick:
            case Tool.Hazard:
            case Tool.Water:
            case Tool.Elevator:
                if (RefuseOnMarker(x, y, isDrag)) return;
                level.Set(x, y, TileFor(tool));
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

    // The start and goal cells are the one place nothing else may go. A wall or a turret would seal
    // the marker in and a hazard or a pool would kill on contact; even a lift, which is harmless,
    // would be quietly wiped by LevelData.Validate (it puts both markers back on plain floor). So
    // refuse outright rather than producing a level that is broken or that silently loses the tile.
    bool RefuseOnMarker(int x, int y, bool isDrag)
    {
        if (!level.IsStart(x, y) && !level.IsGoal(x, y)) return false;
        if (!isDrag) Status("Move the start or goal marker first.");
        return true;
    }

    // The tile each painting tool lays down. Tools that do something other than set a tile
    // (the markers, the blade lane) never reach this.
    static LevelTile TileFor(Tool t)
    {
        switch (t)
        {
            case Tool.Wall:     return LevelTile.Wall;
            case Tool.Brick:    return LevelTile.Brick;
            case Tool.Hazard:   return LevelTile.Hazard;
            case Tool.Water:    return LevelTile.Water;
            case Tool.Spawner:  return LevelTile.SpawnerRight;
            case Tool.Elevator: return LevelTile.Elevator;
            default:            return LevelTile.Floor;
        }
    }

    void SelectTool(Tool next)
    {
        tool = next;
        foreach (var pair in toolBackgrounds)
            pair.Value.color = pair.Key == tool ? ToolActive : ToolIdle;

        // Three tools have settings of their own and never more than one is in hand, so the panel
        // shows that one and the rest of it closes the gap when none of them is.
        if (sawSection != null) sawSection.SetActive(tool == Tool.Saw);
        if (spawnerSection != null) spawnerSection.SetActive(tool == Tool.Spawner);
        if (elevatorSection != null) elevatorSection.SetActive(tool == Tool.Elevator);

        bool sectionShowing = tool == Tool.Saw || tool == Tool.Spawner || tool == Tool.Elevator;
        if (boardSection != null)
            boardSection.anchoredPosition = new Vector2(0f, sectionShowing ? 0f : ToolSectionHeight);

        if (hintText != null) hintText.text = HintFor(tool);
    }

    static string HintFor(Tool t)
    {
        switch (t)
        {
            case Tool.Saw:      return "Click a row to give it a blade lane.\nClick it again to clear the lane.";
            case Tool.Spawner:  return "Click to place a turret.\nClick it again to turn it round.";
            case Tool.Elevator: return "Click to drop a lift into a column.\nRide it back over your own trail.";
            default:            return "Click or drag to paint.";
        }
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

        // Rebuilt from scratch each pass: a cell that stopped being water must stop being animated,
        // and after a resize the old Images have been destroyed outright.
        water.cells.Clear();
        lift.cells.Clear();
        bullet.cells.Clear();

        for (int x = 0; x < level.width; x++)
            for (int y = 0; y < level.height; y++)
            {
                LevelTile t = level.Get(x, y);
                bool lane = level.sawRows.Contains(y);

                // A cell's background carries whatever material fills it edge to edge — floor, the
                // two solid kinds, water, a lift, a turret — and its icon carries the things that
                // merely sit on top.
                Image bg = cellBg[x, y];
                bg.sprite = FillSprite(t);
                if (t == LevelTile.Water) water.cells.Add(bg);
                else if (t == LevelTile.Elevator) lift.cells.Add(bg);
                // Without the sheets the sprites are null, so carry the state in the colour instead.
                Color baseColor = bg.sprite != null ? Color.white : FallbackColor(t);
                bg.color = lane ? baseColor * LaneTint : baseColor;
                // A turret is shown turned to face the way it fires, exactly as it is in the level.
                bg.rectTransform.localRotation =
                    LevelData.IsSpawner(t) ? SpawnerRotation(t) : Quaternion.identity;

                // One icon per cell, most specific first: the two unique markers outrank everything,
                // then a turret's bullet, then a hazard, and the blade lane only shows where nothing
                // else is drawn.
                Sprite iconSprite = null;
                float iconAlpha = 1f;
                float iconScale = 0.82f;
                Vector2 iconOffset = Vector2.zero;
                Quaternion iconRotation = Quaternion.identity;
                bool isBullet = false;

                if (level.IsStart(x, y)) iconSprite = startSprite;
                else if (level.IsGoal(x, y)) iconSprite = goalSprite;
                else if (LevelData.IsSpawner(t))
                {
                    // The bullet it fires, parked at the muzzle and pointing the way it will go —
                    // aiming a turret is what a second click on it does, so the facing has to read at
                    // a glance. The icon is a child of the cell, and the cell has already been turned
                    // to face that way, so inside it "forward" is simply up: the offset goes up, and
                    // the nose-right bullet art turns a quarter to match.
                    iconSprite = bullet.Current;
                    isBullet = iconSprite != null;
                    iconScale = 0.42f;
                    iconOffset = new Vector2(0f, cellPx * 0.32f);
                    iconRotation = Quaternion.Euler(0f, 0f, 90f);
                }
                else if (t == LevelTile.Hazard) iconSprite = hazardSprite;
                else if (lane) { iconSprite = sawSprite; iconAlpha = 0.55f; }

                Image icon = cellIcon[x, y];
                icon.sprite = iconSprite;
                icon.enabled = iconSprite != null;
                icon.color = new Color(1f, 1f, 1f, iconAlpha);
                icon.rectTransform.anchoredPosition = iconOffset;
                icon.rectTransform.localRotation = iconRotation;
                icon.rectTransform.sizeDelta = new Vector2(cellPx * iconScale, cellPx * iconScale);
                if (isBullet) bullet.cells.Add(icon);
            }
    }

    static Vector2 Facing(LevelTile t)
    {
        Vector2Int f = LevelData.SpawnerFacing(t);
        return new Vector2(f.x, f.y);
    }

    // The turret art is drawn muzzle-up, so a facing is a turn away from +y — the same angle
    // LevelGrid gives the turret it builds, so the board and the level agree.
    static Quaternion SpawnerRotation(LevelTile t) =>
        Quaternion.Euler(0f, 0f, Vector2.SignedAngle(Vector2.up, Facing(t)));

    // The art filling a whole cell, or null when the sheet it comes from was never found — the
    // caller then falls back to FallbackColor so the board still reads.
    Sprite FillSprite(LevelTile t)
    {
        switch (t)
        {
            case LevelTile.Wall:  return wallSprite;
            // Brick borrows the red wall look only if its own block is missing, so the two solid
            // kinds never silently become indistinguishable when the art is there.
            case LevelTile.Brick: return brickSprite != null ? brickSprite : wallSprite;
            case LevelTile.Water: return water.Current;
            case LevelTile.Elevator: return lift.Current;
            default: return LevelData.IsSpawner(t) ? spawnerSprite : floorSprite;
        }
    }

    // Flat stand-in colours for when the sheets are missing, so an unwired project still shows a
    // board you can tell apart rather than a grid of identical blanks.
    static Color FallbackColor(LevelTile t)
    {
        switch (t)
        {
            case LevelTile.Wall:     return new Color(0.80f, 0.18f, 0.18f);
            case LevelTile.Brick:    return new Color(0.55f, 0.50f, 0.46f);
            case LevelTile.Water:    return new Color(0.22f, 0.45f, 0.85f);
            case LevelTile.Elevator: return new Color(0.47f, 0.49f, 0.58f);
            default: return LevelData.IsSpawner(t) ? new Color(0.30f, 0.31f, 0.35f)
                                                   : new Color(0.62f, 0.64f, 0.68f);
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
        // Keep the name and every tuning slider — clearing is about wiping the layout, not the
        // numbers the designer already settled on.
        blank.levelName = level.levelName;
        blank.sawSpeed = level.sawSpeed;
        blank.sawInterval = level.sawInterval;
        blank.bulletSpeed = level.bulletSpeed;
        blank.fireInterval = level.fireInterval;
        blank.liftSpeed = level.liftSpeed;
        blank.Resize(level.width, level.height);
        blank.endX = blank.width - 1;
        blank.endY = blank.height - 1;
        blank.Validate();

        level = blank;
        RebuildBoard();
        Status("Board cleared.");
    }

    // ---- save / load / play -------------------------------------------------

    bool SavePromptOpen => savePanel != null && savePanel.activeSelf;

    // Saving is where a level gets its name. Nothing on the editor screen asks for one until this
    // point, so a level can be built and test-played as an unnamed draft and only has to be called
    // something once the player actually wants to keep it.
    void OpenSavePrompt()
    {
        if (browserPanel != null) browserPanel.SetActive(false);

        // A level that has been saved before opens on its own name, ready to be saved over. One that
        // hasn't opens empty rather than on the "New Level" placeholder, which the player would only
        // have to clear out by hand.
        string suggestion = level.levelName == LevelData.DefaultName ? string.Empty : level.levelName;
        saveNameField.SetTextWithoutNotify(suggestion);

        savePanel.SetActive(true);
        RefreshSaveHint();
        saveNameField.Select();
        saveNameField.ActivateInputField();
    }

    void CloseSavePrompt()
    {
        if (savePanel != null) savePanel.SetActive(false);
    }

    // Warns before a save replaces a level already on disk. It updates as the name is typed, so the
    // warning arrives while the player can still change their mind cheaply.
    void RefreshSaveHint()
    {
        if (saveHintText == null) return;
        string typed = LevelStore.Sanitize(saveNameField != null ? saveNameField.text : string.Empty);
        saveHintText.text = typed.Length > 0 && LevelStore.Exists(typed)
            ? $"\"{typed}\" already exists — saving replaces it."
            : string.Empty;
    }

    void ConfirmSave()
    {
        string typed = saveNameField != null ? saveNameField.text : level.levelName;
        string previousName = level.levelName;

        level.levelName = typed;
        if (LevelStore.Save(level, out string error))
        {
            // Save sanitizes the name in place, so the header shows what actually reached disk.
            CloseSavePrompt();
            RefreshNameText();
            Status($"Saved \"{level.levelName}\".");
            return;
        }

        // Leave the prompt up with the reason on it — the player is one keystroke from fixing it.
        level.levelName = previousName;
        if (saveHintText != null) saveHintText.text = error;
    }

    void TestPlay()
    {
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
        RefreshNameText();
        if (speedSlider != null) speedSlider.SetValueWithoutNotify(level.sawSpeed);
        if (intervalSlider != null) intervalSlider.SetValueWithoutNotify(level.sawInterval);
        if (bulletSpeedSlider != null) bulletSpeedSlider.SetValueWithoutNotify(level.bulletSpeed);
        if (fireIntervalSlider != null) fireIntervalSlider.SetValueWithoutNotify(level.fireInterval);
        if (liftSpeedSlider != null) liftSpeedSlider.SetValueWithoutNotify(level.liftSpeed);
        RefreshToolLabels();
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
        // Last, so it is the canvas's last child and draws over the browser as well as the board.
        BuildSavePrompt();
    }

    void BuildHeader()
    {
        var title = Label(canvasGO.transform, "LEVEL EDITOR", new Vector2(-580f, 458f),
                          new Vector2(720f, 80f), 56, ToolActive);
        title.alignment = TextAnchor.MiddleLeft;
        title.fontStyle = FontStyle.Bold;

        // The name is settled at save time now, so up here it is only a reminder of which level is
        // on the board — read-only, and nothing to tab through while painting.
        var caption = Label(canvasGO.transform, "EDITING", new Vector2(280f, 458f),
                            new Vector2(220f, 50f), 28, new Color(0.62f, 0.66f, 0.78f));
        caption.alignment = TextAnchor.MiddleRight;

        nameText = Label(canvasGO.transform, "", new Vector2(680f, 458f),
                         new Vector2(560f, 62f), 38, Color.white);
        nameText.alignment = TextAnchor.MiddleLeft;
        RefreshNameText();
    }

    void RefreshNameText()
    {
        if (nameText == null) return;
        bool unnamed = level.levelName == LevelData.DefaultName;
        nameText.text = unnamed ? "Unsaved level" : level.levelName;
        nameText.color = unnamed ? new Color(0.62f, 0.66f, 0.78f) : Color.white;
    }

    void BuildPalette()
    {
        // Tool, caption, icon. Erase has no art of its own, so it draws as a bare marked square.
        var tools = new (Tool tool, string caption, Sprite icon)[]
        {
            (Tool.Floor,    "Floor",    floorSprite),
            (Tool.Wall,     "Wall",     wallSprite),
            (Tool.Brick,    "Brick",    brickSprite),
            (Tool.Hazard,   "Hazard",   hazardSprite),
            (Tool.Water,    "Water",    water.Current),
            (Tool.Spawner,  "Spawner",  spawnerSprite),
            (Tool.Elevator, "Elevator", lift.Current),
            (Tool.Start,    "Start",    startSprite),
            (Tool.Goal,     "Goal",     goalSprite),
            (Tool.Saw,      "Saw",      sawSprite),
            (Tool.Erase,    "Erase",    null),
        };

        const float spacing = 110f, buttonSize = 94f, rowY = 356f;
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
                // The water and lift swatches move in the palette too, so those tools advertise
                // what they paint rather than showing a still frame of it.
                if (entry.tool == Tool.Water) water.paletteIcon = icon;
                else if (entry.tool == Tool.Elevator) lift.paletteIcon = icon;
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

            // The Spawner swatch also carries the bullet it fires, flickering in the corner, because
            // the turret and its ammunition are two halves of the one tool.
            if (entry.tool == Tool.Spawner && bullet.HasArt)
            {
                var shotGO = Child("Bullet", go.transform);
                Place(shotGO, new Vector2(buttonSize * 0.26f, -buttonSize * 0.28f),
                              new Vector2(buttonSize * 0.44f, buttonSize * 0.44f));
                var shot = shotGO.AddComponent<Image>();
                shot.sprite = bullet.Current;
                shot.preserveAspect = true;
                shot.raycastTarget = false;
                bullet.paletteIcon = shot;
            }

            var caption = Label(canvasGO.transform, entry.caption,
                                new Vector2(firstX + i * spacing, rowY - 68f),
                                new Vector2(spacing, 34f), 22, new Color(0.85f, 0.86f, 0.9f));
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
        // Parked against the right edge in the gap the rest of the screen leaves it: below the
        // palette captions and stopping short of the footer, so it covers no artwork and never
        // reaches the Back button. See SidePanelCentre / SidePanelSize.
        var panel = Child("SidePanel", canvasGO.transform);
        Place(panel, SidePanelCentre, SidePanelSize);
        panel.AddComponent<Image>().color = PanelColor;
        Transform p = panel.transform;

        // One section per tool that has settings, each a zero-sized container pinned to the panel's
        // centre so the widgets inside keep the coordinates they would have had as direct children
        // of the panel. SelectTool shows the one belonging to the tool in hand and hides the rest.
        // They are all ToolSectionHeight tall, so the block underneath has a single offset to slide
        // by rather than one per tool.
        sawSection = BuildSection(p, "SawSection");
        Transform s = sawSection.transform;

        Header(s, "SAWBLADES", 260f);

        // The blades sweep along whole rows, so speed and spacing are level-wide settings; which
        // rows they sweep is painted on the board with the Saw tool.
        speedText = SliderRow(s, "Speed", 205f, LevelData.MinSawSpeed, LevelData.MaxSawSpeed,
                              level.sawSpeed, out speedSlider);
        speedSlider.onValueChanged.AddListener(v => { level.sawSpeed = v; RefreshToolLabels(); });

        intervalText = SliderRow(s, "Spawn gap", 110f, LevelData.MinSawInterval, LevelData.MaxSawInterval,
                                 level.sawInterval, out intervalSlider);
        intervalSlider.onValueChanged.AddListener(v => { level.sawInterval = v; RefreshToolLabels(); });

        // Turrets: every one on the board fires at the same speed and rate, the way every blade
        // sweeps at the same speed. Where they are and which way each points is painted on the board.
        spawnerSection = BuildSection(p, "SpawnerSection");
        Transform sp = spawnerSection.transform;

        Header(sp, "BULLET SPAWNERS", 260f);
        bulletSpeedText = SliderRow(sp, "Bullet speed", 205f, LevelData.MinBulletSpeed, LevelData.MaxBulletSpeed,
                                    level.bulletSpeed, out bulletSpeedSlider);
        bulletSpeedSlider.onValueChanged.AddListener(v => { level.bulletSpeed = v; RefreshToolLabels(); });

        fireIntervalText = SliderRow(sp, "Fire every", 110f, LevelData.MinFireInterval, LevelData.MaxFireInterval,
                                     level.fireInterval, out fireIntervalSlider);
        fireIntervalSlider.onValueChanged.AddListener(v => { level.fireInterval = v; RefreshToolLabels(); });

        elevatorSection = BuildSection(p, "ElevatorSection");
        Transform el = elevatorSection.transform;

        Header(el, "ELEVATORS", 260f);
        liftSpeedText = SliderRow(el, "Lift speed", 205f, LevelData.MinLiftSpeed, LevelData.MaxLiftSpeed,
                                  level.liftSpeed, out liftSpeedSlider);
        liftSpeedSlider.onValueChanged.AddListener(v => { level.liftSpeed = v; RefreshToolLabels(); });

        var note = Label(el, "A lift rides the whole height of the\ncolumn you drop it in, and carries\nyou over tiles you already painted.",
                         new Vector2(0f, 105f), new Vector2(370f, 100f), 22, new Color(0.72f, 0.74f, 0.8f));
        note.alignment = TextAnchor.MiddleCenter;

        RefreshToolLabels();

        // Everything below belongs to no particular tool. It rides up into the sections' space
        // whenever none of them is showing, so the panel never shows a gap.
        var general = Child("BoardSection", p);
        Place(general, Vector2.zero, Vector2.zero);
        boardSection = general.GetComponent<RectTransform>();
        Transform g = general.transform;

        Header(g, "BOARD SIZE", 15f);
        widthText = Stepper(g, "Width", -35f, () => ResizeBoard(-1, 0), () => ResizeBoard(1, 0));
        heightText = Stepper(g, "Height", -105f, () => ResizeBoard(0, -1), () => ResizeBoard(0, 1));

        MakeButton(g, "Clear Board", new Vector2(0f, -195f), new Vector2(300f, 64f), ButtonColor, ClearBoard, 28);

        hintText = Label(g, "", new Vector2(0f, -252f), new Vector2(360f, 66f), 22, new Color(0.72f, 0.74f, 0.8f));
        hintText.alignment = TextAnchor.MiddleCenter;
    }

    GameObject BuildSection(Transform parent, string name)
    {
        var section = Child(name, parent);
        Place(section, Vector2.zero, Vector2.zero);
        return section;
    }

    void BuildFooter()
    {
        statusText = Label(canvasGO.transform, "", new Vector2(GridAreaCentre.x, -396f),
                           new Vector2(1300f, 46f), 28, ToolActive);
        statusText.alignment = TextAnchor.MiddleCenter;

        var size = new Vector2(300f, 80f);
        MakeButton(canvasGO.transform, "Save", new Vector2(-520f, -462f), size, ButtonColor, OpenSavePrompt, 32);
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

    // The naming prompt the Save button opens. It is the only place in the editor that asks for a
    // level name, so the question is put once, when it is actually being answered.
    void BuildSavePrompt()
    {
        savePanel = Child("SavePrompt", canvasGO.transform);
        Stretch(savePanel);
        savePanel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.82f);

        var panel = Child("Panel", savePanel.transform);
        Place(panel, Vector2.zero, new Vector2(840f, 440f));
        panel.AddComponent<Image>().color = PanelColor;

        var title = Label(panel.transform, "SAVE LEVEL", new Vector2(0f, 150f), new Vector2(700f, 70f), 46, ToolActive);
        title.alignment = TextAnchor.MiddleCenter;
        title.fontStyle = FontStyle.Bold;

        var prompt = Label(panel.transform, "What should this level be called?",
                           new Vector2(0f, 82f), new Vector2(760f, 46f), 28, Color.white);
        prompt.alignment = TextAnchor.MiddleCenter;

        var fieldGO = DefaultControls.CreateInputField(new DefaultControls.Resources());
        fieldGO.name = "SaveNameField";
        fieldGO.transform.SetParent(panel.transform, false);
        Place(fieldGO, new Vector2(0f, 14f), new Vector2(660f, 68f));
        saveNameField = fieldGO.GetComponent<InputField>();
        saveNameField.characterLimit = LevelStore.MaxNameLength;
        saveNameField.onValueChanged.AddListener(_ => RefreshSaveHint());
        foreach (Text t in fieldGO.GetComponentsInChildren<Text>(true))
        {
            t.font = font;
            t.fontSize = 32;
        }
        if (saveNameField.placeholder is Text placeholder) placeholder.text = "Type a name";

        saveHintText = Label(panel.transform, "", new Vector2(0f, -52f), new Vector2(760f, 44f), 24, ToolActive);
        saveHintText.alignment = TextAnchor.MiddleCenter;

        MakeButton(panel.transform, "Save", new Vector2(-160f, -145f), new Vector2(280f, 74f), GoColor, ConfirmSave, 32);
        MakeButton(panel.transform, "Cancel", new Vector2(160f, -145f), new Vector2(280f, 74f), ButtonColor, CloseSavePrompt, 32);

        savePanel.SetActive(false);
    }

    void RefreshToolLabels()
    {
        if (speedText != null) speedText.text = level.sawSpeed.ToString("0.0") + " u/s";
        if (intervalText != null) intervalText.text = level.sawInterval.ToString("0.0") + " s";
        if (bulletSpeedText != null) bulletSpeedText.text = level.bulletSpeed.ToString("0.0") + " u/s";
        if (fireIntervalText != null) fireIntervalText.text = level.fireInterval.ToString("0.0") + " s";
        if (liftSpeedText != null) liftSpeedText.text = level.liftSpeed.ToString("0.0") + " u/s";
    }

    // ---- tiny UI builders --------------------------------------------------

    void Header(Transform parent, string caption, float y)
    {
        var t = Label(parent, caption, new Vector2(0f, y), new Vector2(360f, 50f), 30, new Color(0.62f, 0.66f, 0.78f));
        t.alignment = TextAnchor.MiddleCenter;
        t.fontStyle = FontStyle.Bold;
    }

    // A "caption ..... value" line with a slider under it. Returns the value Text so the caller can
    // keep it up to date, and hands back the Slider itself through `slider` to wire the change to.
    Text SliderRow(Transform parent, string caption, float y, float min, float max, float value, out Slider slider)
    {
        Label(parent, caption, new Vector2(-110f, y), new Vector2(180f, 44f), 28, Color.white)
            .alignment = TextAnchor.MiddleLeft;
        var readout = Label(parent, "", new Vector2(110f, y), new Vector2(160f, 44f), 28, ToolActive);
        readout.alignment = TextAnchor.MiddleRight;
        slider = MakeSlider(parent, new Vector2(0f, y - 40f), min, max, value);
        return readout;
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
