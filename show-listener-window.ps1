param(
    [Parameter(Mandatory = $true)]
    [int]$TargetProcessId
)

Add-Type @'
using System;
using System.Runtime.InteropServices;

public static class ListenerWindowActivator
{
    public delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
}
'@

$found = $false
$callback = [ListenerWindowActivator+EnumProc]{
    param($handle, $unused)
    [uint32]$ownerProcessId = 0
    [void][ListenerWindowActivator]::GetWindowThreadProcessId($handle, [ref]$ownerProcessId)
    if ($ownerProcessId -eq $TargetProcessId) {
        $rect = New-Object ListenerWindowActivator+Rect
        if ([ListenerWindowActivator]::GetWindowRect($handle, [ref]$rect) -and
            ($rect.Right - $rect.Left) -ge 300 -and ($rect.Bottom - $rect.Top) -ge 100) {
            $script:found = $true
            [void][ListenerWindowActivator]::ShowWindow($handle, 5)
            [void][ListenerWindowActivator]::SetForegroundWindow($handle)
        }
    }
    return $true
}

[void][ListenerWindowActivator]::EnumWindows($callback, [IntPtr]::Zero)
if ($found) { exit 0 }
exit 1
