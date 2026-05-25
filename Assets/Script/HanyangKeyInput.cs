using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

public static class HanyangKeyInput
{
    public static bool GetKeyDown(KeyCode keyCode)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && TryGetKeyControl(keyCode, out var keyControl))
            return keyControl.wasPressedThisFrame;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.GetKeyDown(keyCode);
#else
        return false;
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static bool TryGetKeyControl(KeyCode keyCode, out KeyControl keyControl)
    {
        var keyboard = Keyboard.current;
        keyControl = keyCode switch
        {
            KeyCode.Space => keyboard.spaceKey,
            KeyCode.P => keyboard.pKey,
            KeyCode.M => keyboard.mKey,
            KeyCode.Alpha1 => keyboard.digit1Key,
            KeyCode.Alpha2 => keyboard.digit2Key,
            KeyCode.Alpha3 => keyboard.digit3Key,
            _ => null
        };
        return keyControl != null;
    }
#endif
}
