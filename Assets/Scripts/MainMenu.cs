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
            BuildMuteButton(); // sits just to the right of the volume slider
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

    // Creates the mute toggle in code (the Options panel is authored in the scene, but the sprite
    // and MuteButton logic live in the project so we spawn it here) and places it immediately to
    // the right of the volume slider. MuteButton handles the icon frames and the actual audio mute.
    private void BuildMuteButton()
    {
        var sliderRT = volumeSlider.GetComponent<RectTransform>();

        var go = new GameObject("MuteButton",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(MuteButton));
        go.transform.SetParent(sliderRT.parent, false); // same container as the slider

        var img = go.GetComponent<Image>();
        img.preserveAspect = true;

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = sliderRT.anchorMin;
        rt.anchorMax = sliderRT.anchorMax;
        rt.pivot = sliderRT.pivot;
        rt.sizeDelta = new Vector2(80f, 80f);
        // Nudge past the slider's right edge: clear the slider (its right half), leave a gap, then
        // add the button's own half-width so the icon doesn't overlap the slider.
        rt.anchoredPosition = sliderRT.anchoredPosition
            + new Vector2(sliderRT.sizeDelta.x * (1f - sliderRT.pivot.x) + 24f + 40f, 0f);
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
