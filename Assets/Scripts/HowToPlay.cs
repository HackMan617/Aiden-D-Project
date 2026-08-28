using UnityEngine;
using UnityEngine.UI;

// The "How to Play" control on the options screen: a thumbnail of the movement diagram that opens
// the same picture full-size when it is clicked.
//
// The artwork is a single image rather than a sheet, and the enlarged view draws that exact sprite
// scaled up, so the controls shown small and the controls shown large can never drift apart —
// there is only one asset to keep current.
//
// The enlarged view is built the first time it is opened and then reused, and it is parented to the
// ROOT canvas rather than to the options panel. As the canvas's last child it draws over every
// other control on screen; as a child of the panel it would be laid out among them and the panel's
// own buttons could sit on top of the diagram.
//
// Its size comes from stretch anchors rather than a pixel size, so it fills the same share of the
// screen at any resolution, and preserveAspect keeps the square diagram square inside that box.
// Dismissal is a click anywhere, which is why there is no close button.
[RequireComponent(typeof(Button)), RequireComponent(typeof(Image))]
public class HowToPlay : MonoBehaviour
{
    [Header("Artwork")]
    [Tooltip("Image name inside Assets/Resources. Used only when the sprite below is left empty.")]
    [SerializeField] private string spriteName = "movement graphic";
    [Tooltip("The controls diagram, shown as the thumbnail and again enlarged. Left blank, it is " +
             "loaded from Resources by the name above.")]
    [SerializeField] private Sprite graphic;

    [Header("Enlarged view")]
    [Tooltip("Heading above the enlarged diagram.")]
    [SerializeField] private string title = "HOW TO PLAY";
    [Tooltip("Line under the enlarged diagram telling the player how to get out of it.")]
    [SerializeField] private string dismissHint = "Click anywhere to close";
    [Tooltip("How much of the screen's height the diagram fills, 0-1.")]
    [Range(0.3f, 0.95f)]
    [SerializeField] private float screenFill = 0.76f;

    private GameObject overlay;

    private void Awake()
    {
        if (graphic == null) graphic = Resources.Load<Sprite>(spriteName);
        if (graphic == null)
        {
            Debug.LogError($"[HowToPlay] No image found named '{spriteName}'. It must live in Assets/Resources.");
            return;
        }

        var image = GetComponent<Image>();
        image.sprite = graphic;
        image.preserveAspect = true; // the thumbnail rect is not the diagram's aspect; letterbox it
        image.color = Color.white;   // drop any placeholder tint the Image was built with
        image.type = Image.Type.Simple;

        // The default ColorTint transition is the whole hover/press feedback here: there is only one
        // frame of artwork, so there is nothing to swap to.
        GetComponent<Button>().onClick.AddListener(Show);
    }

    // Leaving the options screen closes the panel this lives on, which must take the enlarged view
    // with it — the overlay hangs off the canvas, not off the panel, so it would otherwise be left
    // covering whatever screen came next.
    private void OnDisable()
    {
        if (overlay != null) overlay.SetActive(false);
    }

    public void Show()
    {
        if (overlay == null) overlay = BuildOverlay();
        if (overlay == null) return;

        overlay.SetActive(true);
        overlay.transform.SetAsLastSibling(); // stay on top of anything added since it was built
    }

    public void Hide()
    {
        if (overlay != null) overlay.SetActive(false);
    }

    private GameObject BuildOverlay()
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[HowToPlay] No Canvas above this button, so the enlarged view has nowhere to go.");
            return null;
        }
        Transform root = canvas.rootCanvas.transform;

        var go = new GameObject("HowToPlayOverlay", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(root, false);
        Stretch(go.GetComponent<RectTransform>());

        // The dim is also the dismiss target: it covers the screen, so a click anywhere that isn't
        // swallowed by something else lands on it. It is nearly opaque because the options panel
        // behind it is white text on a dark background — at a gentler alpha the headings read
        // straight through the diagram and compete with it.
        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.96f);
        var close = go.GetComponent<Button>();
        close.transition = Selectable.Transition.None; // it is a backdrop, not a button to look at
        close.onClick.AddListener(Hide);

        // The diagram. Anchored as a band across the middle of the screen and left to letterbox
        // itself inside it, so it scales with the window instead of being pinned to a pixel size.
        var pic = new GameObject("Diagram", typeof(RectTransform), typeof(Image));
        pic.transform.SetParent(go.transform, false);
        var picRT = pic.GetComponent<RectTransform>();
        float margin = (1f - screenFill) * 0.5f;
        picRT.anchorMin = new Vector2(0.1f, margin);
        picRT.anchorMax = new Vector2(0.9f, 1f - margin);
        picRT.offsetMin = Vector2.zero;
        picRT.offsetMax = Vector2.zero;

        var picImg = pic.GetComponent<Image>();
        picImg.sprite = graphic;
        picImg.preserveAspect = true;
        picImg.raycastTarget = false; // clicking the diagram should close too, so let the dim have it

        Caption(go.transform, title, new Vector2(0f, 1f - margin * 0.55f), 64, FontStyle.Bold, Color.white);
        Caption(go.transform, dismissHint, new Vector2(0f, margin * 0.5f), 30, FontStyle.Normal,
                new Color(1f, 1f, 1f, 0.75f));

        return go;
    }

    // A line of text centred on a point given as a fraction of the screen's height.
    private static void Caption(Transform parent, string content, Vector2 anchor, int fontSize,
                                FontStyle style, Color color)
    {
        var go = new GameObject("Caption", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, anchor.y);
        rt.anchorMax = new Vector2(0.5f, anchor.y);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(anchor.x, 0f);
        rt.sizeDelta = new Vector2(1200f, fontSize * 1.6f);

        var text = go.GetComponent<Text>();
        text.text = content;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false; // never block the click-anywhere dismissal
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
