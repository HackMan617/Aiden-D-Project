using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-shot editor builder for the Aiden D player (directional animation).
//
// Sprite sources (both already sliced):
//   - "aidend sprite sheet.png"      : _0 idle, _1.._3 horizontal walk cycle
//   - "aiden d up sprite sheet.png"  : _0.._3 vertical (up/down) walk cycle
//
// It builds:
//   * Player_Idle          (idle frame)
//   * Player_WalkSide      (horizontal walk; flipX handles left vs right)
//   * Player_WalkVertical  (used for both up and down movement)
//   * PlayerAnimator       (Speed float + IsVertical bool drive the 3 states)
// and configures the Player GameObject + 2D camera.
//
// Auto-runs once on load (guarded), and is available under Tools > Aiden D.
[InitializeOnLoad]
public static class AidenPlayerSetup
{
    const string SideSheetPath = "Assets/Sprites/aidend sprite sheet.png";
    const string UpSheetPath = "Assets/Sprites/aiden d up sprite sheet.png";
    const string DownSheetPath = "Assets/Sprites/aiden d down sprite sheet.png";
    const string AnimFolder = "Assets/Animation";
    const string IdleClipPath = AnimFolder + "/Player_Idle.anim";
    const string SideClipPath = AnimFolder + "/Player_WalkSide.anim";
    const string UpClipPath = AnimFolder + "/Player_WalkUp.anim";
    const string DownClipPath = AnimFolder + "/Player_WalkDown.anim";
    // legacy names, cleaned up on rebuild
    const string OldWalkClipPath = AnimFolder + "/Player_Walk.anim";
    const string OldVerticalClipPath = AnimFolder + "/Player_WalkVertical.anim";
    const string ControllerPath = AnimFolder + "/PlayerAnimator.controller";

    static AidenPlayerSetup()
    {
        EditorApplication.delayCall += AutoRun;
    }

    static void AutoRun()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        // Only auto-build on a fresh project (no controller yet). Re-runs go through the menu.
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) return;
        Build();
    }

    [MenuItem("Tools/Aiden D/Build Player + Animations")]
    public static void Build()
    {
        // 1. Load all sliced sheets in name order (_0 .. _n).
        List<Sprite> side = LoadFrames(SideSheetPath);
        List<Sprite> up = LoadFrames(UpSheetPath);
        List<Sprite> down = LoadFrames(DownSheetPath);
        if (side.Count < 4)
        {
            Debug.LogError($"[AidenPlayerSetup] Expected 4 sliced sprites in '{SideSheetPath}', found {side.Count}.");
            return;
        }
        if (up.Count < 1 || down.Count < 1)
        {
            Debug.LogError($"[AidenPlayerSetup] Expected sliced sprites in up/down sheets (up={up.Count}, down={down.Count}).");
            return;
        }

        Sprite idleSprite = side[0];
        Sprite[] sideWalk = { side[1], side[2], side[3] };
        // The dedicated "up" sheet is drawn far flatter/squished than the others (its frames are
        // short, mostly-purple shapes), so walking up reuses the well-proportioned DOWN frames.
        // The character faces the screen while walking up, but stays consistently sized with the
        // other directions. (The 'up' sheet is still loaded above only for the sanity check.)
        Sprite[] upWalk = down.ToArray();
        Sprite[] downWalk = down.ToArray();

        // 2. Ensure the Animation folder exists, then clear any previous build.
        if (!AssetDatabase.IsValidFolder(AnimFolder))
            AssetDatabase.CreateFolder("Assets", "Animation");
        foreach (string p in new[] { IdleClipPath, SideClipPath, UpClipPath, DownClipPath,
                                     OldWalkClipPath, OldVerticalClipPath, ControllerPath })
            if (AssetDatabase.LoadMainAssetAtPath(p) != null) AssetDatabase.DeleteAsset(p);

        // 3. Build the clips.
        AnimationClip idleClip = CreateSpriteClip(new[] { idleSprite }, 1f, false, IdleClipPath);
        AnimationClip sideClip = CreateSpriteClip(sideWalk, 10f, true, SideClipPath);
        AnimationClip upClip = CreateSpriteClip(upWalk, 10f, true, UpClipPath);
        AnimationClip downClip = CreateSpriteClip(downWalk, 10f, true, DownClipPath);

        // 4. Build the controller: Speed (float) + Direction (int: 0 side, 1 up, 2 down)
        //    -> Idle / WalkSide / WalkUp / WalkDown.
        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Direction", AnimatorControllerParameterType.Int);
        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimatorState idle = sm.AddState("Idle");
        idle.motion = idleClip;
        AnimatorState walkSide = sm.AddState("WalkSide");
        walkSide.motion = sideClip;
        AnimatorState walkUp = sm.AddState("WalkUp");
        walkUp.motion = upClip;
        AnimatorState walkDown = sm.AddState("WalkDown");
        walkDown.motion = downClip;
        sm.defaultState = idle;

        AnimatorState[] walks = { walkSide, walkUp, walkDown }; // index == Direction value
        for (int d = 0; d < walks.Length; d++)
        {
            // Idle -> this walk when moving and facing this direction.
            AddTransition(idle, walks[d], Speed(true), Dir(d));
            // Walk -> idle when stopped.
            AddTransition(walks[d], idle, Speed(false));
            // Walk -> other walks when the facing direction changes mid-move.
            for (int e = 0; e < walks.Length; e++)
                if (e != d) AddTransition(walks[d], walks[e], Speed(true), Dir(e));
        }

        EditorUtility.SetDirty(controller);

        // 5. Spawn / configure the Player in the active scene.
        GameObject player = GameObject.Find("Player");
        if (player == null) player = new GameObject("Player");

        SpriteRenderer sr = player.GetComponent<SpriteRenderer>();
        if (sr == null) sr = player.AddComponent<SpriteRenderer>();
        sr.sprite = idleSprite;

        Animator animator = player.GetComponent<Animator>();
        if (animator == null) animator = player.AddComponent<Animator>();
        animator.runtimeAnimatorController = controller;

        if (player.GetComponent<PlayerController>() == null)
            player.AddComponent<PlayerController>();

        // 6. Make sure the camera is set up for 2D so the sprite is visible.
        Camera cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
        if (cam != null)
        {
            cam.orthographic = true;
            if (cam.orthographicSize < 1f) cam.orthographicSize = 5f;
            Vector3 cp = cam.transform.position;
            if (cp.z >= 0f) cam.transform.position = new Vector3(cp.x, cp.y, -10f);
            EditorUtility.SetDirty(cam);
        }

        // 7. Save assets + scene.
        AssetDatabase.SaveAssets();
        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Selection.activeGameObject = player;
        Debug.Log("[AidenPlayerSetup] Done. Idle / WalkSide / WalkUp / WalkDown built. " +
                  "Press Play: A/D = side, W = up, S = down.");
    }

    static List<Sprite> LoadFrames(string sheetPath) =>
        AssetDatabase.LoadAllAssetsAtPath(sheetPath)
            .OfType<Sprite>()
            .OrderBy(s => s.name, System.StringComparer.Ordinal)
            .ToList();

    // Condition descriptors -------------------------------------------------
    struct Cond { public AnimatorConditionMode mode; public float threshold; public string param; }
    static Cond Speed(bool moving) => new Cond
    {
        mode = moving ? AnimatorConditionMode.Greater : AnimatorConditionMode.Less,
        threshold = 0.01f,
        param = "Speed"
    };
    static Cond Dir(int direction) => new Cond
    {
        mode = AnimatorConditionMode.Equals,
        threshold = direction,
        param = "Direction"
    };

    static void AddTransition(AnimatorState from, AnimatorState to, params Cond[] conds)
    {
        AnimatorStateTransition t = from.AddTransition(to);
        t.hasExitTime = false;
        t.duration = 0f;
        foreach (Cond c in conds) t.AddCondition(c.mode, c.threshold, c.param);
    }

    // Builds an AnimationClip that drives SpriteRenderer.m_Sprite through the given frames.
    static AnimationClip CreateSpriteClip(Sprite[] frames, float fps, bool loop, string path)
    {
        AnimationClip clip = new AnimationClip { frameRate = fps };

        EditorCurveBinding binding = new EditorCurveBinding
        {
            type = typeof(SpriteRenderer),
            path = "",
            propertyName = "m_Sprite"
        };

        ObjectReferenceKeyframe[] keys = new ObjectReferenceKeyframe[frames.Length];
        for (int i = 0; i < frames.Length; i++)
            keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }
}
