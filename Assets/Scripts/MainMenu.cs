using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main-menu controller. Lives in its own MainMenu scene (build index 0) and loads the game
// scene on Play. The buttons' OnClick events are wired to these public methods; the volume
// slider and fullscreen toggle are wired at runtime in Start().
//
// NOTE: this project uses the Input System package only, so the menu scene's EventSystem must
// use InputSystemUIInputModule (not the legacy StandaloneInputModule) or the UI won't respond.
//
// Keyboard control of the Play / Options / Quit stack (arrows, Tab, Enter) is not here — it lives on
// a MenuNavigator component on the main panel itself.
public class MainMenu : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject mainPanel;
    [SerializeField] private GameObject optionsPanel;

    [Header("Scene")]
    [SerializeField] private string gameSceneName = "SampleScene";

    [Header("Options")]
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private Toggle fullscreenToggle;

    // Set by other screens (e.g. the level-select "Options" button) to request that the menu open
    // straight to the Options panel on load instead of the main panel. Cleared once honoured.
    public static bool OpenOptionsOnLoad = false;

    private void Start()
    {
        ShowMain();

        // Main-menu music. MusicManager persists across scenes, so this also switches back to the
        // menu track whenever the player returns here from a level (clearing any gameplay/pause music).
        MusicManager.PlayMenuMusic();

        if (volumeSlider != null)
        {
            volumeSlider.value = AudioListener.volume;
            volumeSlider.onValueChanged.AddListener(SetVolume);
            BuildVolumeRow();  // the level icon and the mute button, right of the slider
            BuildHowToPlay();  // the controls diagram, in the row below the fullscreen toggle
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = Screen.fullScreen;
            fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        }

        if (OpenOptionsOnLoad)
        {
            OpenOptionsOnLoad = false;
            ShowOptions();
        }
    }

    public void PlayGame()
    {
        GameProgress.Reset(); // a fresh run always starts at level 1 (easiest sawblades)
        // Non-additive load unloads this whole menu scene, so no manual Destroy is needed.
        SceneManager.LoadScene(gameSceneName);
    }

    public void ShowMain()
    {
        if (mainPanel != null) mainPanel.SetActive(true);
        if (optionsPanel != null) optionsPanel.SetActive(false);
    }

    public void ShowOptions()
    {
        if (mainPanel != null) mainPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(true);
    }

    // Everything below is built in code rather than authored in the scene: the Options panel itself
    // is in the scene, but the artwork and the behaviour live in the project, so spawning the
    // controls here leaves the scene with nothing to wire up.

    private const float Gap = 24f;            // breathing room between controls in a row
    private const float ControlSize = 80f;    // the square controls beside the volume slider

    private const float VolumeLabelX = -260f; // the column the "Volume" label sits in, from the scene
    private const float HowToPlayRow = -120f; // clear of the fullscreen toggle above and Back below
    private const float ThumbnailSize = 150f;

    // The two controls to the right of the volume slider, laid out left to right from its edge.
    private void BuildVolumeRow()
    {
        var sliderRT = volumeSlider.GetComponent<RectTransform>();
        float edge = sliderRT.anchoredPosition.x + sliderRT.sizeDelta.x * (1f - sliderRT.pivot.x);

        // The speaker icon reads the slider, so it comes first — right beside the handle it follows.
        GameObject icon = RowItem("VolumeIcon", sliderRT, ref edge);
        icon.AddComponent<VolumeIcon>().Track(volumeSlider);

        // Mute stays a control of its own: it works through AudioListener.pause rather than the
        // volume, so it is not just the far-left end of the slider. The icon shows the crossed-out
        // speaker while it is on, so the two still read as one thing.
        GameObject mute = RowItem("MuteButton", sliderRT, ref edge);
        mute.AddComponent<Button>();
        mute.AddComponent<MuteButton>();
    }

    // Places one square control after the last, advancing `edge` past it.
    private static GameObject RowItem(string name, RectTransform sliderRT, ref float edge)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(sliderRT.parent, false); // same container as the slider

        go.GetComponent<Image>().preserveAspect = true;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = sliderRT.anchorMin;
        rt.anchorMax = sliderRT.anchorMax;
        rt.pivot = sliderRT.pivot;
        rt.sizeDelta = new Vector2(ControlSize, ControlSize);
        rt.anchoredPosition = new Vector2(edge + Gap + ControlSize * 0.5f, sliderRT.anchoredPosition.y);

        edge += Gap + ControlSize;
        return go;
    }

    // The "How to Play" row: a label in the same column as "Volume", and a thumbnail of the controls
    // diagram starting where the slider starts, so the panel reads as two columns of rows. Clicking
    // the thumbnail opens the same diagram full-size — see HowToPlay.
    private void BuildHowToPlay()
    {
        var sliderRT = volumeSlider.GetComponent<RectTransform>();
        Transform parent = sliderRT.parent;
        float left = sliderRT.anchoredPosition.x - sliderRT.sizeDelta.x * sliderRT.pivot.x;

        var label = new GameObject("HowToPlayLabel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        label.transform.SetParent(parent, false);
        Place(label, sliderRT, new Vector2(VolumeLabelX, HowToPlayRow), new Vector2(260f, 50f));

        var text = label.GetComponent<Text>();
        text.text = "How to Play";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 34; // matches the "Volume" label authored in the scene
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft;
        text.raycastTarget = false;

        var thumb = new GameObject("HowToPlayButton", typeof(RectTransform), typeof(CanvasRenderer),
            typeof(Image), typeof(Button), typeof(HowToPlay));
        thumb.transform.SetParent(parent, false);
        Place(thumb, sliderRT, new Vector2(left + ThumbnailSize * 0.5f, HowToPlayRow),
              new Vector2(ThumbnailSize, ThumbnailSize));
    }

    // Anchors a control the same way the slider is anchored, so it stays put alongside it.
    private static void Place(GameObject go, RectTransform sliderRT, Vector2 pos, Vector2 size)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = sliderRT.anchorMin;
        rt.anchorMax = sliderRT.anchorMax;
        rt.pivot = sliderRT.pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    public void SetVolume(float value)
    {
        AudioListener.volume = value;
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
