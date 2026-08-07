// Tiny cross-scene progression holder. There is no per-maze "level asset" in this project — each
// win simply reloads SampleScene at a higher difficulty — so the current level is just an int that
// lives in a static (it survives scene reloads within a play session, and resets on a fresh launch).
//
//   * MainMenu.PlayGame  -> Reset()   (start a new run at level 1)
//   * GameOverManager win "Continue" -> Advance()  (next level, harder)
//   * death "Retry"       -> neither   (replay the same level)
//
// LevelGrid reads CurrentLevel to scale the sawblade hazards.
public static class GameProgress
{
    public static int CurrentLevel = 1;

    public static void Reset() => CurrentLevel = 1;
    public static void Advance() => CurrentLevel++;
}
