using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace D2R96TZ
{
    public sealed class LobbyUiController
    {
        private const byte VkBack = 0x08;
        private const byte VkEscape = 0x1B;
        private const byte VkControl = 0x11;
        private const byte VkA = 0x41;
        private const byte VkC = 0x43;
        private const byte VkV = 0x56;
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint MouseWheel = 0x0800;
        private const uint MonitorDefaultToNearest = 2;
        private const uint SwpNoZOrder = 0x0004;
        private const int SwRestore = 9;
        private const int SwShow = 5;

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public Rect Monitor;
            public Rect WorkArea;
            public uint Flags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetClientRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ClientToScreen(IntPtr window, ref Point point);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr window, out Rect rect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char character);

        private readonly IntPtr window;
        private readonly AppConfig config;
        private DateTime lastLobbyRowClickUtc = DateTime.MinValue;

        public LobbyUiController(ProcessMemoryReader memory, AppConfig config)
        {
            window = memory.MainWindowHandle;
            this.config = config;
            if (window == IntPtr.Zero) throw new InvalidOperationException("D2R 主窗口不存在。");
            EnsureUsableWindow();
        }

        public void SearchAndRefresh(string keyword)
        {
            Activate();
            ClickNormalized(1415.0 / 1920.0, 65.0 / 1080.0);
            Thread.Sleep(100);
            ClickNormalized(1450.0 / 1920.0, 210.0 / 1080.0);
            for (int index = 0; index < 32; index++) PressKey(VkBack);
            if (RequiresUnicodePaste(keyword)) PasteTextPreservingClipboard(keyword);
            else foreach (char character in keyword) TypeCharacter(character);
            ClickNormalized(1720.0 / 1920.0, 210.0 / 1080.0);
            Thread.Sleep(config.LobbyRefreshWaitMs);
        }

        public void ClearSearchBox()
        {
            Activate();
            ClickNormalized(1450.0 / 1920.0, 210.0 / 1080.0);
            for (int index = 0; index < 32; index++) PressKey(VkBack);
            Thread.Sleep(50);
        }

        public void RefreshCurrentSearch()
        {
            Activate();
            ClickNormalized(1720.0 / 1920.0, 210.0 / 1080.0);
            Thread.Sleep(config.LobbyRefreshWaitMs);
        }

        public string ReadSearchKeyword()
        {
            Activate();
            ClickNormalized(1450.0 / 1920.0, 210.0 / 1080.0);
            System.Windows.Forms.IDataObject previous = null;
            try
            {
                try { previous = System.Windows.Forms.Clipboard.GetDataObject(); }
                catch (Exception) { }
                try { System.Windows.Forms.Clipboard.Clear(); }
                catch (Exception) { }
                keybd_event(VkControl, 0, 0, UIntPtr.Zero);
                PressKey(VkA);
                PressKey(VkC);
                keybd_event(VkControl, 0, 2, UIntPtr.Zero);
                Thread.Sleep(50);
                try
                {
                    return System.Windows.Forms.Clipboard.ContainsText() ? System.Windows.Forms.Clipboard.GetText() : string.Empty;
                }
                catch (Exception) { return string.Empty; }
            }
            finally
            {
                try
                {
                    if (previous == null) System.Windows.Forms.Clipboard.Clear();
                    else System.Windows.Forms.Clipboard.SetDataObject(previous, true);
                }
                catch (Exception) { }
            }
        }

        public void SelectLobbyIndex(int index, int roomCount)
        {
            if (index < 0 || index >= 40) throw new ArgumentOutOfRangeException("index");
            if (roomCount <= index) throw new ArgumentOutOfRangeException("roomCount");
            Activate();
            MoveCursorNormalized(1450.0 / 1920.0, 450.0 / 1080.0);
            for (int step = 0; step < 40; step++)
            {
                mouse_event(MouseWheel, 0, 0, 120, UIntPtr.Zero);
                Thread.Sleep(10);
            }
            Thread.Sleep(50);

            int maxScrollOffset = Math.Max(0, roomCount - 14);
            int scrollOffset = index <= 13 ? 0 : Math.Min(index - 7, maxScrollOffset);
            if (scrollOffset > 0)
            {
                double fraction = (double)scrollOffset / maxScrollOffset;
                DragNormalized(1510.0 / 1920.0, 285.0 / 1080.0, 1510.0 / 1920.0, (285.0 + 320.0 * fraction) / 1080.0);
                Thread.Sleep(80);
            }
            int visibleIndex = index - scrollOffset;
            int remaining = 700 - (int)(DateTime.UtcNow - lastLobbyRowClickUtc).TotalMilliseconds;
            if (remaining > 0) Thread.Sleep(remaining);
            double rowY = Math.Min(610.0, 260.0 + visibleIndex * 27.3);
            ClickNormalized(1345.0 / 1920.0, rowY / 1080.0);
            lastLobbyRowClickUtc = DateTime.UtcNow;
            Thread.Sleep(50);
        }

        public void ClickJoin()
        {
            Activate();
            ClickNormalized(1470.0 / 1920.0, 675.0 / 1080.0);
        }

        public void DismissJoinFailure()
        {
            Activate();
            PressKey(VkEscape);
            Thread.Sleep(200);
        }

        public void LeaveGame()
        {
            Activate();
            PressKey(VkEscape);
            Thread.Sleep(100);
            ClickNormalized(960.0 / 1920.0, 480.0 / 1080.0);
            Thread.Sleep(50);
            ClickNormalized(960.0 / 1920.0, 480.0 / 1080.0);
        }

        private void Activate()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                ShowWindow(window, SwRestore);
                ShowWindow(window, SwShow);
                BringWindowToTop(window);
                SetForegroundWindow(window);
                Thread.Sleep(50);
                if (GetForegroundWindow() == window) return;
            }
            throw new InvalidOperationException("D2R 窗口无法置前，已取消本次鼠标操作。");
        }

        private void EnsureUsableWindow()
        {
            Rect client;
            if (!GetClientRect(window, out client)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 D2R 客户区。");
            if (client.Right - client.Left >= 800) return;

            Rect outer;
            if (!GetWindowRect(window, out outer)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 D2R 窗口尺寸。");
            IntPtr monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
            var info = new MonitorInfo { Size = Marshal.SizeOf(typeof(MonitorInfo)) };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 D2R 所在显示器。");

            int borderWidth = (outer.Right - outer.Left) - (client.Right - client.Left);
            int borderHeight = (outer.Bottom - outer.Top) - (client.Bottom - client.Top);
            int workWidth = info.WorkArea.Right - info.WorkArea.Left;
            int workHeight = info.WorkArea.Bottom - info.WorkArea.Top;
            int clientWidth = Math.Min(960, workWidth - borderWidth);
            int clientHeight = Math.Min((int)Math.Round(clientWidth * 9.0 / 16.0), workHeight - borderHeight);
            int outerWidth = clientWidth + borderWidth;
            int outerHeight = clientHeight + borderHeight;
            int x = info.WorkArea.Left + Math.Max(0, (workWidth - outerWidth) / 2);
            int y = info.WorkArea.Top + Math.Max(0, (workHeight - outerHeight) / 2);
            if (!SetWindowPos(window, IntPtr.Zero, x, y, outerWidth, outerHeight, SwpNoZOrder))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法调整 D2R 到可用窗口尺寸。");
            Thread.Sleep(500);
        }

        private void ClickNormalized(double normalizedX, double normalizedY)
        {
            MoveCursorNormalized(normalizedX, normalizedY);
            if (GetForegroundWindow() != window)
            {
                Activate();
                MoveCursorNormalized(normalizedX, normalizedY);
            }
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(10);
        }

        private void DragNormalized(double fromX, double fromY, double toX, double toY)
        {
            MoveCursorNormalized(fromX, fromY);
            mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(20);
            MoveCursorNormalized(toX, toY);
            Thread.Sleep(20);
            mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
        }

        private void MoveCursorNormalized(double normalizedX, double normalizedY)
        {
            Rect rect;
            if (!GetClientRect(window, out rect)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法读取 D2R 客户区。");
            var point = new Point
            {
                X = (int)Math.Round((rect.Right - rect.Left) * normalizedX),
                Y = (int)Math.Round((rect.Bottom - rect.Top) * normalizedY)
            };
            if (!ClientToScreen(window, ref point)) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法换算 D2R 点击坐标。");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (SetCursorPos(point.X, point.Y)) return;
                Thread.Sleep(40);
            }
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法移动鼠标到 D2R 窗口。");
        }

        private static void PressKey(byte virtualKey)
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
            keybd_event(virtualKey, 0, 2, UIntPtr.Zero);
            Thread.Sleep(3);
        }

        private static void TypeCharacter(char character)
        {
            short key = VkKeyScan(character);
            if (key == -1 || character > 0x7F) throw new InvalidOperationException("字符必须通过 Unicode 粘贴输入: " + character);
            byte virtualKey = (byte)(key & 0xFF);
            byte modifiers = (byte)((key >> 8) & 0xFF);
            if ((modifiers & 1) != 0) keybd_event(0x10, 0, 0, UIntPtr.Zero);
            PressKey(virtualKey);
            if ((modifiers & 1) != 0) keybd_event(0x10, 0, 2, UIntPtr.Zero);
        }

        private static bool RequiresUnicodePaste(string text)
        {
            foreach (char character in text)
                if (character > 0x7F || VkKeyScan(character) == -1) return true;
            return false;
        }

        private static void PasteTextPreservingClipboard(string text)
        {
            System.Windows.Forms.IDataObject previous = null;
            try
            {
                previous = System.Windows.Forms.Clipboard.GetDataObject();
                System.Windows.Forms.Clipboard.SetText(text, System.Windows.Forms.TextDataFormat.UnicodeText);
                keybd_event(VkControl, 0, 0, UIntPtr.Zero);
                PressKey(VkV);
                keybd_event(VkControl, 0, 2, UIntPtr.Zero);
                Thread.Sleep(100);
            }
            finally
            {
                if (previous == null) System.Windows.Forms.Clipboard.Clear();
                else System.Windows.Forms.Clipboard.SetDataObject(previous, true);
            }
        }
    }
}
