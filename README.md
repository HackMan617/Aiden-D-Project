# Aiden D Project

A 2D pixel-art maze game built in Unity. You walk Aiden across a grid of tiles;
every tile you step on flashes grey → white → red and then **stays red forever**.
Red tiles are walls, so your own trail boxes you in — the maze you have to solve
is mostly the one you are drawing behind yourself. Reach the end marker without
touching a hazard, and the next level is harder.

It also ships with an in-game, Mario-Maker-style **level editor**, so you can
paint your own boards and play them without leaving the game.

---

## Requirements

| | |
|---|---|
| **Unity** | **6000.4.8f1** (Unity 6.4) — install this exact version via Unity Hub |
| **Render pipeline** | Universal RP (2D renderer) |
| **Input** | Input System package **only** — legacy `Input` is disabled project-wide |
| **Platform** | Developed on Windows; nothing in the project is Windows-specific |

All package dependencies are listed in `Packages/manifest.json` and are restored
automatically by Unity the first time the project opens.

## Getting it running

1. Clone the repo.
2. In Unity Hub choose **Add → Add project from disk** and pick the cloned folder.
3. Open it with Unity **6000.4.8f1**. The first import takes a few minutes while
   Unity builds its `Library/` folder (not in the repo, by design).
4. Open **`Assets/Scenes/MainMenu.unity`** and press Play.

> Open `MainMenu`, not `SampleScene`. `SampleScene` is the gameplay/editor scene
> and expects the static state the main menu sets up before it loads; entering it
> cold works, but skips the menu flow.

Both scenes are already in Build Settings (`MainMenu` at index 0), so
**File → Build And Run** works without further setup.

## Controls

| Action | Keys |
|---|---|
| Move | `W` `A` `S` `D` or the arrow keys |
| Fire the color wheel | `Space`, or click the dial |
| Pause / unpause | `Escape` |
| Menu navigation | Arrows / `Tab` / `Shift+Tab` to move, `Enter` or `Space` to confirm |
| Level select | `WASD` or arrows to move the red box, `Enter` / `Space` to start |
| Camera | Scroll wheel zooms; hold the left mouse button and drag to pan |

## How a session goes

```
MainMenu scene ──Play──▶ SampleScene
                            │
                            ├── Level select ──▶ play a numbered maze ──▶ win ──▶ next level
                            │                                          └─▶ death ──▶ retry
                            └── Level Editor ──▶ paint / save / test-play your own board
```

- **Level select** is the "WORLD 1" screen: a 5×2 grid of level boxes. Only the
  level you have reached is unlocked. Its bottom bar has Main Menu, Options,
  Quit and **Level Editor**.
- **Progression** is a plain counter (`GameProgress`) — each win reloads
  `SampleScene` at a higher difficulty, which mostly means more and faster
  sawblades. It resets on a fresh launch.
- **Pause** (`Escape`) freezes the game and swaps in its own music track; the
  gameplay track resumes exactly where it left off.

## Features

**Gameplay**
- Reactive tile grid — tiles flash to red under your feet and become walls.
- Hazards: static animated obstacles, sweeping **sawblades**, and **water**.
- **Color-wheel weapon** — a roulette dial in the bottom-left corner. Press
  `Space` and the wheel spins down onto a random segment; the color it lands on
  is read straight out of the artwork's own pixels and tints your shot. Shots
  destroy sawblades.
- Win / game-over overlays with Retry and Continue, plus an always-on Retry
  button in case you box yourself in.
- Zoom-and-pan camera that follows the player.

**Level editor**
- Palette: Floor, Wall, Brick, Hazard, Water, Start, Goal, Saw, Erase — click or
  drag to paint.
- Boards up to 15×9, the size the camera frames in play; resizable in place.
- Sawblade lanes are whole rows; their speed and spawn gap are sliders that
  appear with the Saw tool.
- Save named levels, browse them, and **test-play** a board for real, landing
  back in the editor afterwards.
- Saved levels are JSON files in `<persistentDataPath>/CustomLevels`, so they
  survive quitting and are never bundled into a build.

**Presentation**
- Every screen — menus, HUD, editor, end-game overlays — is **built in code at
  runtime**, so there is almost nothing to wire up in the Inspector.
- Sprite sheets are sliced at runtime via `Sprite.Create`; sheets in
  `Assets/Resources` are loaded by name.
- Artwork buttons (`SpriteButton`) and self-animating ones
  (`AnimatedSpriteButton`, e.g. the turning options gear).
- Persistent music manager with per-screen tracks, a volume slider, and a mute
  button.

## Project layout

```
Assets/
  Scenes/         MainMenu.unity (index 0) and SampleScene.unity (game + editor)
  Scripts/        all gameplay and UI code — see the header comment on each file
  Sprites/        source pixel art
  Resources/      sheets loaded by name at runtime (tiles, buttons, color wheel)
  Sound Effects/  music tracks
  Animation/      animator controllers and clips
```

Notable scripts:

| Script | What it does |
|---|---|
| `LevelGrid` | Builds the board, the reactive tiles, hazards and sawblades |
| `LevelEditor` | The in-game editor UI and painting tools |
| `LevelData` / `LevelStore` | Level format and the on-disk JSON library |
| `ColorWheelWeapon` / `ColorProjectile` | The dial and its shots |
| `PlayerController` | Four-directional movement and facing |
| `GameSession` / `GameProgress` | What `SampleScene` builds, and how far you've got |
| `LevelSelectManager` / `MainMenu` / `PauseMenu` | The three menu screens |
| `GameOverManager` | Win and game-over overlays |
| `MusicManager` | Per-screen background music |

Most scripts carry a header comment explaining not just what they do but why
they are built that way — start there rather than here.

## Notes for contributors

- **The project path must not contain spaces** if you use the AI Game Developer
  MCP tooling; it logs an error otherwise. Plain Unity work is unaffected.
- `.mcp.json` points at a local MCP endpoint and is only relevant if you use
  that tooling.
- `Library/`, `Temp/`, `Logs/`, `Build/` and the generated `.sln`/`.csproj`
  files are gitignored — Unity regenerates them on open.
