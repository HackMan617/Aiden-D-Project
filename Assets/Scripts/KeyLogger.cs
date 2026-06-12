using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// In-editor key logger: prints every key the moment it's pressed to the Unity
// Console window, so you can watch input during Play mode for development testing.
// (Mirror of the standalone Tools/KeyLogger console app, but inside Unity.)
//
// Attach this to any GameObject in the scene to enable it.
public class KeyLogger : MonoBehaviour
{
    void Update()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null) return;

        foreach (KeyControl key in kb.allKeys)
        {
            if (key.wasPressedThisFrame)
                Debug.Log($"[KeyLogger] {key.keyCode}  (char '{key.displayName}')");
        }
    }
}
