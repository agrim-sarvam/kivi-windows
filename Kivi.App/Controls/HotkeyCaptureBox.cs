// Kivi.App/Controls/HotkeyCaptureBox.cs
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Kivi.App.Controls;

/// <summary>
/// Button that, when clicked, enters "listening" mode and captures the next key-down as
/// the new hotkey virtual-key code. Used by ConfigPage to rebind the dictation hotkey.
/// </summary>
public sealed class HotkeyCaptureBox : Button
{
    private bool _capturing;
    public uint VirtualKeyCode { get; private set; } = 0xA3;
    public event Action<uint>? HotkeyChanged;

    public HotkeyCaptureBox()
    {
        Content = Label(VirtualKeyCode);
        Click += (_, _) =>
        {
            _capturing = true;
            Content = "press a key…";
            Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
        };
        KeyDown += OnKeyDown;
    }

    public void SetInitial(uint vk)
    {
        VirtualKeyCode = vk;
        Content = Label(vk);
    }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!_capturing) return;

        if (e.Key == VirtualKey.Escape)
        {
            _capturing = false;
            Content = Label(VirtualKeyCode);
            e.Handled = true;
            return;
        }

        _capturing = false;
        VirtualKeyCode = (uint)e.Key;
        Content = Label(VirtualKeyCode);
        HotkeyChanged?.Invoke(VirtualKeyCode);
        e.Handled = true;
    }

    private static string Label(uint vk) => vk switch
    {
        0xA3 => "Right Ctrl",
        0xA2 => "Left Ctrl",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        _ => ((VirtualKey)vk).ToString()
    };
}
