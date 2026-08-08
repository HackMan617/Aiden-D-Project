using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Orthographic 2D camera: scroll wheel zooms, and (optionally) the camera smoothly
// follows the player so it stays centred when zoomed in. Input System only.
//
// Mouse input is read straight from the device rather than through the event system, which is what
// makes the grab-to-pan feel right — but it also means this component cannot tell on its own that
// the cursor is over a button or the volume slider. Every mouse gesture is therefore gated on
// PointerOverUI so dragging a slider doesn't drag the map along with it.
[RequireComponent(typeof(Camera))]
public class CameraController : MonoBehaviour
{
    [Header("Zoom")]
    [Tooltip("Orthographic size change per scroll notch.")]
    public float zoomStep = 0.5f;
    public float minZoom = 2f;
    public float maxZoom = 6f;
    [Tooltip("How quickly the zoom eases to the target size.")]
    public float zoomSmooth = 10f;

    [Header("Follow")]
    public bool followPlayer = true;
    [Tooltip("Followed target. Auto-finds 'Player' if empty.")]
    public Transform target;
    [Tooltip("How quickly the camera eases toward the player.")]
    public float followSmooth = 8f;

    [Header("Drag-pan")]
    [Tooltip("Hold the left mouse button and drag to pan around the map (grab style).")]
    public bool enableDragPan = true;

    Camera cam;
    public float targetSize; // public so it can be driven/tested externally
    bool dragging;
    Vector3 dragOrigin;     // world point grabbed at mouse-down (stays under the cursor)
    bool followSuspended;   // true after a manual pan, until the player moves again
    Vector3 lastTargetPos;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        targetSize = cam.orthographicSize;
    }

    void Start()
    {
        if (target == null)
        {
            GameObject p = GameObject.Find("Player");
            if (p != null) target = p.transform;
        }
        if (target != null) lastTargetPos = target.position;
    }

    void LateUpdate()
    {
        Mouse mouse = Mouse.current;

        // Zoom from the scroll wheel — ignored over the UI, so spinning the wheel while the cursor
        // rests on a menu doesn't zoom the map behind it.
        if (mouse != null)
        {
            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scrollY) > 0.01f && !PointerOverUI(mouse.position.ReadValue()))
                targetSize = Mathf.Clamp(targetSize - Mathf.Sign(scrollY) * zoomStep, minZoom, maxZoom);
        }
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, targetSize, zoomSmooth * Time.deltaTime);

        // Hold-left-drag to pan: keep the grabbed world point locked under the cursor.
        if (enableDragPan && mouse != null)
        {
            // Only the START of a grab is refused over the UI — that is what stops a drag on the
            // volume slider from also panning the map. Once a grab has begun on open map it keeps
            // panning even if the cursor crosses a button, which is how grabbing should feel.
            if (mouse.leftButton.wasPressedThisFrame && !PointerOverUI(mouse.position.ReadValue()))
            {
                dragging = true;
                dragOrigin = ScreenToWorld(mouse.position.ReadValue());
            }
            else if (dragging && mouse.leftButton.isPressed)
            {
                PanByWorldDelta(dragOrigin - ScreenToWorld(mouse.position.ReadValue()));
            }
            if (mouse.leftButton.wasReleasedThisFrame) dragging = false;
        }

        // Smoothly follow the player, unless a manual pan suspended it. Follow resumes
        // automatically once the player moves again, so you can free-look then snap back.
        if (followPlayer && target != null)
        {
            if (!dragging && followSuspended &&
                (target.position - lastTargetPos).sqrMagnitude > 0.0001f)
                followSuspended = false;

            if (!dragging && !followSuspended)
            {
                Vector3 goal = new Vector3(target.position.x, target.position.y, transform.position.z);
                transform.position = Vector3.Lerp(transform.position, goal, followSmooth * Time.deltaTime);
            }
            lastTargetPos = target.position;
        }
    }

    // Pan the camera in the XY plane and suspend auto-follow until the player moves again.
    public void PanByWorldDelta(Vector2 worldDelta)
    {
        transform.position += new Vector3(worldDelta.x, worldDelta.y, 0f);
        followSuspended = true;
    }

    Vector3 ScreenToWorld(Vector2 screen) => cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f));

    // Reused so the UI test doesn't allocate a fresh list each time.
    static readonly List<RaycastResult> uiHits = new List<RaycastResult>();

    // Is anything from the UI under the cursor? Asked by raycasting the event system directly
    // rather than via EventSystem.IsPointerOverGameObject(), whose no-argument form depends on
    // which input module is installed and on the mouse's pointer id — this project builds its
    // canvases in code and drives them through the Input System's UI module.
    //
    // Only called on a scroll or a mouse-down, never every frame, so the PointerEventData it
    // builds costs nothing in the common case.
    static bool PointerOverUI(Vector2 screenPosition)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false; // no UI in the scene at all

        var pointer = new PointerEventData(events) { position = screenPosition };
        uiHits.Clear();
        events.RaycastAll(pointer, uiHits);
        return uiHits.Count > 0;
    }
}
