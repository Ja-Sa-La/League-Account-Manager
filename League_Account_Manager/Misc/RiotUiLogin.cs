using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core.Input;

namespace League_Account_Manager.Misc;

/// <summary>
///     Enters credentials into the Riot Client sign-in form with real
///     (SendInput) keyboard and mouse events after bringing the client window
///     to the foreground.
///     Recent Riot Client builds (CEF, v136+) no longer expose a UI Automation
///     tree — there is no Chrome_RenderWidgetHostHWND child and no elements
///     with the "username"/"password" automation ids — so the old FlaUI
///     element lookup found nothing and typed nothing. Posted window messages
///     (WM_CHAR) are also dropped inconsistently by this client, so real input
///     against the focused window is the only dependable path.
/// </summary>
internal static class RiotUiLogin
{
    private const int SwRestore = 9;

    // Sign-in form layout measured in a 1536x864 window; scaled to the actual
    // window size before use.
    private const double RefWidth = 1536;
    private const double RefHeight = 864;
    private static readonly (double X, double Y) UsernameField = (200, 281);
    private static readonly (double X, double Y) PasswordField = (200, 345);
    private static readonly (double X, double Y) StaySignedInBox = (64, 453);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    public static bool RiotClientRunning =>
        Process.GetProcessesByName("Riot Client").Length != 0 ||
        Process.GetProcessesByName("RiotClientUx").Length != 0;

    /// <summary>
    ///     Waits for a Riot Client process that owns a visible top-level window
    ///     and returns that window's current handle. CEF spawns several
    ///     subprocesses all named "Riot Client" and recreates its window during
    ///     startup, so the handle must be resolved fresh before every use —
    ///     never cached across attempts.
    /// </summary>
    public static async Task<IntPtr> WaitForLoginWindowAsync(int timeoutMs = 60000)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var proc = Process.GetProcessesByName("Riot Client")
                .Concat(Process.GetProcessesByName("RiotClientUx"))
                .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
            if (proc != null) return proc.MainWindowHandle;
            await Task.Delay(200);
        }

        return IntPtr.Zero;
    }

    /// <summary>
    ///     Brings the Riot window to the foreground, clicks each field, clears
    ///     it, types the credential, optionally ticks "Stay signed in", and
    ///     submits with Enter. Returns false when the window could not be
    ///     focused (or disappeared) — callers should retry after a delay.
    ///     The stay-signed-in click is a blind toggle and the form starts
    ///     unchecked, so only pass true on the first attempt.
    /// </summary>
    public static async Task<bool> EnterCredentialsAsync(string? username, string? password,
        bool staySignedIn = false, bool submit = true)
    {
        // the window handle churns during client startup; always resolve fresh
        var hwnd = await WaitForLoginWindowAsync(10000);
        if (hwnd == IntPtr.Zero) return false;

        if (!await ForceForegroundAsync(hwnd))
        {
            DebugConsole.WriteLine("[RiotUiLogin] Could not bring Riot Client window to foreground");
            return false;
        }

        if (!GetWindowRect(hwnd, out var rect) || rect.Right - rect.Left < 100) return false;

        ClickAt(rect, UsernameField);
        await Task.Delay(250);
        ClearFocusedField();
        TypeText(username ?? "");
        await Task.Delay(150);

        ClickAt(rect, PasswordField);
        await Task.Delay(250);
        ClearFocusedField();
        TypeText(password ?? "");
        await Task.Delay(150);

        if (staySignedIn)
        {
            ClickAt(rect, StaySignedInBox);
            await Task.Delay(250);
        }

        if (submit)
        {
            // Enter submits the form while focus is in a text field
            ClickAt(rect, PasswordField);
            await Task.Delay(250);
            PressKey(VkReturn);
        }

        return true;
    }

    /// <summary>
    ///     Polls the Riot Client EULA endpoint until it reports a logged-in
    ///     state ("Accepted" or "AcceptanceRequired", which is accepted
    ///     automatically). Returns false once the timeout elapses — e.g. when
    ///     the page was not ready yet or a captcha is waiting for the user.
    /// </summary>
    public static async Task<bool> WaitForAuthAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try
            {
                var resp = await Lcu.Connector("riot", "get", "/eula/v1/agreement/acceptance", "");
                var status = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (status == "\"Accepted\"") return true;
                if (status == "\"AcceptanceRequired\"")
                {
                    DebugConsole.WriteLine("[RiotUiLogin] Accepting EULA");
                    await Lcu.Connector("riot", "put", "/eula/v1/agreement/acceptance", "");
                    return true;
                }
            }
            catch
            {
                // client not ready to answer yet
            }

            await Task.Delay(500);
        }

        return false;
    }

    private static async Task<bool> ForceForegroundAsync(IntPtr hwnd)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (GetForegroundWindow() == hwnd) return true;

            if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);

            // attach to the current foreground thread so Windows lets us hand
            // the foreground over even when another app currently holds it
            var foreground = GetForegroundWindow();
            var foregroundThread = GetWindowThreadProcessId(foreground, out _);
            var ourThread = GetCurrentThreadId();
            var attached = foregroundThread != ourThread &&
                           AttachThreadInput(ourThread, foregroundThread, true);
            try
            {
                // a benign key tap marks our process as "recently received
                // input", which also satisfies the foreground-lock rules
                PressKey(VkMenu);
                SetForegroundWindow(hwnd);
            }
            finally
            {
                if (attached) AttachThreadInput(ourThread, foregroundThread, false);
            }

            await Task.Delay(250);
            if (GetForegroundWindow() == hwnd) return true;
        }

        return GetForegroundWindow() == hwnd;
    }

    private static void ClickAt(Rect rect, (double X, double Y) refPoint)
    {
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        var x = rect.Left + (int)(refPoint.X * width / RefWidth);
        var y = rect.Top + (int)(refPoint.Y * height / RefHeight);
        Mouse.MoveTo(x, y);
        Mouse.Click();
    }

    private static void ClearFocusedField()
    {
        // Ctrl+A, Delete — all as scancode events
        SendScan(ScanFromVk(VkControl), false);
        Thread.Sleep(KeyDelayMs);
        SendScan(ScanFromVk(VkA), false);
        Thread.Sleep(KeyDelayMs);
        SendScan(ScanFromVk(VkA), true);
        Thread.Sleep(KeyDelayMs);
        SendScan(ScanFromVk(VkControl), true);
        Thread.Sleep(KeyDelayMs);
        PressKey(VkDelete);
        Thread.Sleep(KeyDelayMs);
    }

    // ----- scancode keyboard input -----
    // Characters are sent as hardware scancode events (KEYEVENTF_SCANCODE)
    // resolved through the active keyboard layout, with a real Shift press
    // around characters that need it — identical to physical typing. The
    // KEYEVENTF_UNICODE path is used only for characters the layout cannot
    // produce.

    private const int KeyDelayMs = 25;

    private const ushort VkReturn = 0x0D;
    private const ushort VkControl = 0x11;
    private const ushort VkMenu = 0x12;
    private const ushort VkDelete = 0x2E;
    private const ushort VkA = 0x41;

    private const uint InputKeyboard = 1;
    private const uint KeyEventFExtended = 0x01;
    private const uint KeyEventFKeyUp = 0x02;
    private const uint KeyEventFUnicode = 0x04;
    private const uint KeyEventFScancode = 0x08;
    private const uint MapVkToVsc = 0;

    // extended keys live in the e0-prefixed scancode set
    private static readonly ushort[] ExtendedVks = { 0x2E /*DEL*/, 0x23 /*END*/, 0x24 /*HOME*/, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28, 0x2D };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScanW(char ch);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    private static void TypeText(string text)
    {
        foreach (var c in text)
        {
            var vkScan = VkKeyScanW(c);
            if (vkScan == -1)
            {
                // not producible on the active layout — unicode fallback
                SendUnicode(c);
                Thread.Sleep(KeyDelayMs);
                continue;
            }

            var vk = (ushort)(vkScan & 0xFF);
            var needShift = (vkScan & 0x100) != 0;
            var scan = ScanFromVk(vk);
            if (scan == 0)
            {
                SendUnicode(c);
                Thread.Sleep(KeyDelayMs);
                continue;
            }

            if (needShift)
            {
                SendScan(0x2A, false); // left shift
                Thread.Sleep(KeyDelayMs);
            }

            SendScan(scan, false, IsExtended(vk));
            Thread.Sleep(KeyDelayMs);
            SendScan(scan, true, IsExtended(vk));

            if (needShift)
            {
                Thread.Sleep(KeyDelayMs);
                SendScan(0x2A, true);
            }

            Thread.Sleep(KeyDelayMs);
        }
    }

    private static void PressKey(ushort vk)
    {
        var scan = ScanFromVk(vk);
        SendScan(scan, false, IsExtended(vk));
        Thread.Sleep(KeyDelayMs);
        SendScan(scan, true, IsExtended(vk));
        Thread.Sleep(KeyDelayMs);
    }

    private static ushort ScanFromVk(ushort vk)
    {
        return (ushort)MapVirtualKeyW(vk, MapVkToVsc);
    }

    private static bool IsExtended(ushort vk)
    {
        return ExtendedVks.Contains(vk);
    }

    private static void SendScan(ushort scan, bool keyUp, bool extended = false)
    {
        var flags = KeyEventFScancode | (keyUp ? KeyEventFKeyUp : 0) | (extended ? KeyEventFExtended : 0);
        var input = new Input
        {
            Type = InputKeyboard,
            Data = { Keyboard = new KeyboardInput { Scan = scan, Flags = flags } }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
    }

    private static void SendUnicode(char c)
    {
        var down = new Input
        {
            Type = InputKeyboard,
            Data = { Keyboard = new KeyboardInput { Scan = c, Flags = KeyEventFUnicode } }
        };
        var up = new Input
        {
            Type = InputKeyboard,
            Data = { Keyboard = new KeyboardInput { Scan = c, Flags = KeyEventFUnicode | KeyEventFKeyUp } }
        };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInputPadding Mouse;
    }

    // ensures the union is at least as large as MOUSEINPUT, which the OS expects
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInputPadding
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
