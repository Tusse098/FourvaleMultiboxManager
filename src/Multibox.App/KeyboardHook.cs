using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Multibox.App;

/// <summary>
/// Low-level keyboard hook (spec §9 "host-level hooks"). Plain keys typed into a game view go straight to
/// WebView2 and never reach WPF, so app shortcuts such as <c>1</c> or <c>Space</c> are seen here.
/// The handler decides per event; returning true swallows the event. The hook only observes the player's own
/// key presses; it never creates key events.
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;

    private readonly LowLevelKeyboardProc _callback; // kept alive for the hook's lifetime
    private readonly Func<Key, bool, bool> _handler;
    private IntPtr _hook;

    /// <param name="handler">(key, isDown) → swallow? Called on the UI thread; must return quickly.</param>
    public KeyboardHook(Func<Key, bool, bool> handler)
    {
        _handler = handler;
        _callback = OnHook;
        using var module = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _callback, GetModuleHandle(module?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the keyboard hook.");
        }
    }

    private IntPtr OnHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(data);
            var msg = message.ToInt32();
            var isDown = msg is WmKeyDown or WmSysKeyDown;
            var isUp = msg is WmKeyUp or WmSysKeyUp;

            // Synthetic events (from other software) are never treated as shortcuts.
            if ((isDown || isUp) && (info.Flags & LlkhfInjected) == 0)
            {
                try
                {
                    if (_handler(KeyInterop.KeyFromVirtualKey((int)info.VkCode), isDown))
                    {
                        return 1;
                    }
                }
                catch (Exception)
                {
                    // Never let a handler problem block the keyboard.
                }
            }
        }

        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
