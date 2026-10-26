using System;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace InFalsusAdvVN;

/// <summary>
/// Mouse wheel capture.
///
/// Every direct read of Mouse.scroll from managed code returns 0 in this build: the game runs the Input
/// System itself and the delta control is already reset when a MonoBehaviour callback runs. The wheel is
/// therefore taken from the raw event stream (InputSystem.onEvent), which is also what the game uses for
/// the LOG list, the story choice UI and the song list.
///
/// The listener must be installed *after* the game's own InputManager has installed its listener,
/// otherwise the game overwrites it; it is therefore hooked the first time a story scene exists.
/// MouseState layout: position @0, delta @8, scroll @16, buttons @24, displayIndex @26 (28 bytes); the
/// state data starts after the base InputEvent header, derived from the event's own sizeInBytes.
/// </summary>
internal static class WheelListener
{
    private const int MouseStateSize = 32;
    private const int ScrollOffsetInState = 16;

    public static bool Installed { get; private set; }

    private static float _pending;
    private static int _rightClicks;
    private static int _lastButtons;
    private static Action<string> _log;
    private static int _reports;
    private static int _probes;
    private static int _mouseEvents;

    public static void SetLog(Action<string> log) => _log = log;

    /// <summary>Hooks InputSystem.onEvent once; safe to call repeatedly.</summary>
    public static void InstallLate()
    {
        if (Installed)
            return;

        try
        {
            var handler = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<InputEventPtr, InputDevice>>(
                new Action<InputEventPtr, InputDevice>(OnEvent));

            InputSystem.onEvent = InputSystem.onEvent + handler;
            Installed = true;
            _log?.Invoke("WheelListener attached to InputSystem.onEvent.");
        }
        catch (Exception e)
        {
            _log?.Invoke("WheelListener failed: " + e.Message);
        }
    }

    public static float Consume()
    {
        float value = _pending;
        _pending = 0f;
        return value;
    }

    /// <summary>Right-button presses seen in the raw event stream (edge triggered).</summary>
    public static int ConsumeRightClicks()
    {
        int value = _rightClicks;
        _rightClicks = 0;
        return value;
    }

    private static unsafe void OnEvent(InputEventPtr eventPtr, InputDevice device)
    {
        try
        {
            if (device == null)
                return;

            Mouse mouse = device.TryCast<Mouse>();
            if (mouse == null)
                return;

            _mouseEvents++;

            try
            {
                int size = (int)eventPtr.sizeInBytes;
                int baseSize = size - MouseStateSize;
                if (baseSize >= 16 && baseSize <= 64)
                {
                    int buttons = Marshal.ReadInt16((IntPtr)eventPtr.data, baseSize + 24);
                    if ((buttons & 0x02) != 0 && (_lastButtons & 0x02) == 0)
                        _rightClicks++;
                    _lastButtons = buttons;
                }
            }
            catch
            {
                // ignore probe failures
            }

            float value = mouse.scroll != null ? mouse.scroll.ReadValue().y : 0f;
            if (value == 0f)
                value = ReadScrollFromEvent(eventPtr);

            if (value == 0f)
                return;

            _pending += value;

            if (_reports < 12)
            {
                _reports++;
                _log?.Invoke("[debug] wheel event=" + value + " device=" + mouse.name +
                             " mouseEvents=" + _mouseEvents);
            }
        }
        catch
        {
            // never disturb the game's input processing
        }
    }
    private static unsafe float ReadScrollFromEvent(InputEventPtr eventPtr)
    {
        try
        {
            int size = (int)eventPtr.sizeInBytes;
            int baseSize = size - MouseStateSize;
            if (baseSize < 16 || baseSize > 64)
                return 0f;

            IntPtr data = (IntPtr)eventPtr.data;
            if (data == IntPtr.Zero)
                return 0f;

            IntPtr scroll = IntPtr.Add(data, baseSize + ScrollOffsetInState);
            float x = Marshal.PtrToStructure<float>(scroll);
            float y = Marshal.PtrToStructure<float>(IntPtr.Add(scroll, 4));

            if (_probes < 10)
            {
                _probes++;
                _log?.Invoke("[debug] mouse event size=" + size + " base=" + baseSize +
                             " pos=(" + Marshal.PtrToStructure<float>(IntPtr.Add(data, baseSize)) + "," +
                             Marshal.PtrToStructure<float>(IntPtr.Add(data, baseSize + 4)) + ")" +
                             " scroll=(" + x + "," + y + ")");
            }

            return y;
        }
        catch
        {
            return 0f;
        }
    }
}
