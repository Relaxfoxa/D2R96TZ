using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Collections.Generic;

namespace D2R96TZ
{
    public sealed class ProcessMemoryReader : IDisposable
    {
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmRead = 0x0010;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, byte[] buffer, int size, out IntPtr bytesRead);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr VirtualQueryEx(IntPtr process, IntPtr address, out MemoryBasicInformation buffer, UIntPtr length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private Process process;
        private IntPtr handle;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public uint Alignment1;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
            public uint Alignment2;
        }

        public sealed class MemoryRegion
        {
            public long Address { get; set; }
            public long Size { get; set; }
        }

        public IntPtr ModuleBase { get; private set; }
        public int ModuleSize { get; private set; }
        public int ProcessId { get { return process == null ? 0 : process.Id; } }
        public IntPtr MainWindowHandle { get { return process == null ? IntPtr.Zero : process.MainWindowHandle; } }
        public string FileVersion { get; private set; }
        public bool Is64BitProcess { get; private set; }

        public void Open(string processName)
        {
            process = Process.GetProcessesByName(processName).OrderByDescending(p => p.StartTime).FirstOrDefault();
            if (process == null) throw new InvalidOperationException("未找到进程 " + processName + ". 请先启动 D2R 并停留在 Join Game 页面。");

            ProcessModule module = null;
            try
            {
                module = process.Modules.Cast<ProcessModule>().FirstOrDefault(m => string.Equals(m.ModuleName, "D2R.exe", StringComparison.OrdinalIgnoreCase));
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException("无法枚举 D2R 模块；请尝试以相同权限运行验证器。", ex);
            }
            if (module == null) throw new InvalidOperationException("D2R.exe 模块不存在。");

            handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, process.Id);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess 只读权限申请失败。");
            ModuleBase = module.BaseAddress;
            ModuleSize = module.ModuleMemorySize;
            FileVersion = module.FileVersionInfo.FileVersion;
            ushort processMachine;
            ushort nativeMachine;
            Is64BitProcess = IsWow64Process2(handle, out processMachine, out nativeMachine) && processMachine == 0 && nativeMachine == 0x8664;
        }

        public byte[] Read(IntPtr address, int size)
        {
            if (handle == IntPtr.Zero) throw new InvalidOperationException("进程尚未打开。");
            var buffer = new byte[size];
            IntPtr bytesRead;
            if (!ReadProcessMemory(handle, address, buffer, size, out bytesRead) || bytesRead.ToInt64() != size)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ReadProcessMemory 读取失败，地址 0x" + address.ToInt64().ToString("X"));
            return buffer;
        }

        public bool TryRead(IntPtr address, int size, out byte[] buffer)
        {
            buffer = new byte[size];
            IntPtr bytesRead;
            return ReadProcessMemory(handle, address, buffer, size, out bytesRead) && bytesRead.ToInt64() == size;
        }

        public IEnumerable<MemoryRegion> EnumerateReadableModuleRegions()
        {
            const uint memCommit = 0x1000;
            const uint pageNoAccess = 0x01;
            const uint pageGuard = 0x100;
            long current = ModuleBase.ToInt64();
            long end = checked(current + ModuleSize);
            UIntPtr infoSize = new UIntPtr((uint)Marshal.SizeOf(typeof(MemoryBasicInformation)));

            while (current < end)
            {
                MemoryBasicInformation info;
                if (VirtualQueryEx(handle, new IntPtr(current), out info, infoSize) == UIntPtr.Zero) yield break;
                long regionAddress = info.BaseAddress.ToInt64();
                long regionSize = checked((long)info.RegionSize.ToUInt64());
                if (regionSize <= 0) yield break;
                long regionEnd = Math.Min(end, checked(regionAddress + regionSize));
                long clippedAddress = Math.Max(current, regionAddress);
                if (info.State == memCommit && (info.Protect & (pageNoAccess | pageGuard)) == 0 && regionEnd > clippedAddress)
                {
                    yield return new MemoryRegion { Address = clippedAddress, Size = regionEnd - clippedAddress };
                }
                current = regionEnd > current ? regionEnd : checked(current + 0x1000);
            }
        }

        public void Dispose()
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
            handle = IntPtr.Zero;
            if (process != null) process.Dispose();
            process = null;
        }
    }
}
