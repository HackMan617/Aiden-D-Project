using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Keyboard navigation for a panel of menu buttons — on the main menu, the Play / Options / Quit
// stack sitting on the start screen.
//
//   Down / Right   next option        Up / Left      previous option
//   Tab            next option        Shift+Tab      previous option
//   Enter / Space  activate the highlighted option
//
// Both directions wrap, so holding Tab walks the menu in a loop.
//
// The options are whatever Buttons live under this GameObject, ordered the way they are laid out on
// screen (top to bottom, then left to right) rather than by hierarchy order — moving a button in the
// scene keeps the navigation order matching what the player sees.
//
// None of the buttons has selected-state artwork of its own (Play and Quit are SpriteButtons, whose
// selected frame is the idle frame), so the highlight is drawn here: a tinted plate sized to the
// option and parented just behind it.
//
// Input is read straight from Keyboard.current rather than left to the EventSystem's own navigation,
// for two reasons: Tab is not a navigation key as far as the UI module is concerned, and the module's
// Navigate action would move the selection a second time on every arrow press. For the same reason
// the options' own navigation is switched off and the EventSystem's selection is cleared whenever
// this takes over, so Submit can never fire on a stale selection on top of our own activation.
[DisallowMultipleComponent]
public class MenuNavigator : MonoBehaviour
{
    // Gold: the level editor's "this one is selected" accent, and the one colour that stands out
    // against all three options (green Play, dark Options, red Quit) and the pale sky behind them.
    [Tooltip("Colour of the plate drawn behind the highlighted option.")]
    [SerializeField] private Color highlightColor = new Color(0.95f, 0.80f, 0.25f, 0.9f);

    [Tooltip("How far the highlight plate extends past the option's own rect, in canvas units.")]
    [SerializeField] private float highlightPadding = 18f;

    private readonly List<Button> options = new List<Button>();
    private RectTransform highlight;
    private int index;

    // Re-collected on every enable rather than once in Awake: the panel is switched off while the
    // Options screen is up, and comes back with the highlight where the player left it.
    private void OnEnable()
    {
        CollectOptions();
        if (options.Count == 0) return;

        EnsureHighlight();
        Select(Mathf.Clamp(index, 0, options.Count - 1));
    }

    private void Update()
    {
        if (options.Count == 0) return;

        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        int step = 0;
        if (kb.downArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame) step = 1;
        else if (kb.upArrowKey.wasPressedThisFrame || kb.leftArrowKey.wasPressedThisFrame) step = -1;
        else if (kb.tabKey.wasPressedThisFrame)
            step = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? -1 : 1;

        if (step != 0) Select(index + step);

        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
            Activate();
    }

    // ---- options -----------------------------------------------------------

    private void CollectOptions()
    {
        options.Clear();
        foreach (Button button in GetComponentsInChildren<Button>(false))
            if (button.interactable) options.Add(button);

        // Screen order, not hierarchy order.
        options.Sort((a, b) =>
        {
            Vector3 pa = a.transform.position, pb = b.transform.position;
            return Mathf.Approximately(pa.y, pb.y) ? pa.x.CompareTo(pb.x) : pb.y.CompareTo(pa.y);
        });

        foreach (Button button in options)
        {
            // The EventSystem must not walk the same arrow presses we do — see the note above.
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;

            HookHover(button);
        }
    }

    // Pointing at an option highlights it too, so the keyboard highlight never sits somewhere other
    // than where the mouse is about to click.
    private void HookHover(Button button)
    {
        if (button.GetComponent<EventTrigger>() != null) return; // already hooked on an earlier enable

        Button captured = button; // captured per button for the callback
        var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entry.callback.AddListener(_ =>
        {
            int i = options.IndexOf(captured);
            if (i >= 0) Select(i); // a button dropped from the list on a later enable just does nothing
        });
        button.gameObject.AddComponent<EventTrigger>().triggers.Add(entry);
    }

    // `i` may be off either end — stepping back off the first option lands on the last one.
    private void Select(int i)
    {
        if (options.Count == 0) return;

        index = ((i % options.Count) + options.Count) % options.Count; // wraps in both directions
        MoveHighlight();

        // A mouse click leaves the clicked button selected in the EventSystem; dropping that here
        // means its Submit action can never fire a second activation on top of ours.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // Runs the button the way a click does — the press frame flashes, then onClick fires — rather
    // than invoking onClick behind the button's back with no feedback.
    private void Activate()
    {
        Button option = options[index];

        if (EventSystem.current != null)
            ExecuteEvents.Execute(option.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        else
            option.onClick.Invoke();
    }

    // ---- highlight ---------------------------------------------------------

    private void EnsureHighlight()
    {
        if (highlight != null) return;

        var go = new GameObject("SelectionHighlight", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        highlight = (RectTransform)go.transform;
        highlight.SetParent(transform, false);

        var image = go.GetComponent<Image>();
        image.color = highlightColor;
        image.raycastTarget = false; // it sits under the option; it must never take the option's clicks
    }

    private void MoveHighlight()
    {
        if (highlight == null) return;

        var target = (RectTransform)options[index].transform;
        var parent = (RectTransform)target.parent;

        highlight.SetParent(parent, false); // same frame of reference as the option

        // Land immediately BEFORE the option, so the option draws on top of its own highlight.
        // Going through the end first makes that unambiguous: moving a child down the list takes the
        // index asked for and pushes the option up one, whereas moving it up the list would land it
        // after the option and cover the artwork.
        highlight.SetAsLastSibling();
        highlight.SetSiblingIndex(target.GetSiblingIndex());
        highlight.anchorMin = target.anchorMin;
        highlight.anchorMax = target.anchorMax;
        highlight.pivot = target.pivot;
        highlight.anchoredPosition = target.anchoredPosition;
        highlight.localScale = target.localScale;

        // sizeDelta is a size only for point anchors; for stretched ones it is the difference from
        // the span the anchors already cover, so take that off to land on the size we actually want.
        Vector2 anchorSpan = Vector2.Scale(target.anchorMax - target.anchorMin, parent.rect.size);
        highlight.sizeDelta = target.rect.size + Vector2.one * (highlightPadding * 2f) - anchorSpan;
    }
}
