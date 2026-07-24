using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Main-menu controller. Lives in its own MainMenu scene (build index 0) and loads the game
// scene on Play. The buttons' OnClick events are wired to these public methods; the volume
// slider and fullscreen toggle are wired at runtime in Start().
//
// NOTE: this project uses the Input System package only, so the menu scene's EventSystem must
// use InputSystemUIInputModule (not the legacy StandaloneInputModule) or the UI won't respond.
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

    private void Start()
    {
        ShowMain();

        if (volumeSlider != null)
        {
            volumeSlider.value = AudioListener.volume;
            volumeSlider.onValueChanged.AddListener(SetVolume);
        }

        if (fullscreenToggle != null)
        {
            fullscreenToggle.isOn = Screen.fullScreen;
            fullscreenToggle.onValueChanged.AddListener(SetFullscreen);
        }
    }

    public void PlayGame()
    {
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
