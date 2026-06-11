using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32.SafeHandles;

namespace WaveTools.Depend
{
    public sealed class LockingProcessInfo
    {
        public int ProcessId { get; init; }
        public string ProcessName { get; init; }
        public string MainWindowTitle { get; init; }
        public string ExecutablePath { get; init; }

        public string DisplayName
        {
            get
            {
                string name = string.IsNullOrWhiteSpace(ProcessName) ? "未知进程" : ProcessName;
                string title = string.IsNullOrWhiteSpace(MainWindowTitle) ? "" : $" - {MainWindowTitle}";
                return $"{name}{title} (PID: {ProcessId})";
            }
        }
    }

    public sealed class BlockedPathInfo
    {
        public string Path { get; init; }
        public string Reason { get; init; }

        public string DisplayName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Reason))
                {
                    return Path;
                }

                return $"{Path}\n{Reason}";
            }
        }
    }

    public static class DataRootLockDetector
    {
        private const int RmRebootReasonNone = 0;
        private const int ErrorMoreData = 234;
        private const int CchRmMaxAppName = 255;
        private const int CchRmMaxSvcName = 63;

        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint OpenExisting = 3;
        private const int ErrorSharingViolation = 32;
        private const int ErrorLockViolation = 33;
        private const int ErrorAccessDenied = 5;

        private enum RM_APP_TYPE
        {
            RmUnknownApp = 0,
            RmMainWindow = 1,
            RmOtherWindow = 2,
            RmService = 3,
            RmExplorer = 4,
            RmConsole = 5,
            RmCritical = 1000
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RM_UNIQUE_PROCESS
        {
            public int dwProcessId;
            public FILETIME ProcessStartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxAppName + 1)]
            public string strAppName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = CchRmMaxSvcName + 1)]
            public string strServiceShortName;

            public RM_APP_TYPE ApplicationType;
            public uint AppStatus;
            public uint TSSessionId;

            [MarshalAs(UnmanagedType.Bool)]
            public bool bRestartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
        private static extern int RmRegisterResources(
            uint pSessionHandle,
            uint nFiles,
            string[] rgsFilenames,
            uint nApplications,
            [In] RM_UNIQUE_PROCESS[] rgApplications,
            uint nServices,
            string[] rgsServiceNames);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmGetList(
            uint dwSessionHandle,
            out uint pnProcInfoNeeded,
            ref uint pnProcInfo,
            [In, Out] RM_PROCESS_INFO[] rgAffectedApps,
            ref uint lpdwRebootReasons);

        [DllImport("rstrtmgr.dll")]
        private static extern int RmEndSession(uint pSessionHandle);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        public static List<LockingProcessInfo> FindLockingProcesses(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return new List<LockingProcessInfo>();
            }

            List<string> resources = CollectResources(path);
            if (resources.Count == 0)
            {
                return new List<LockingProcessInfo>();
            }

            Dictionary<int, LockingProcessInfo> result = new Dictionary<int, LockingProcessInfo>();

            foreach (List<string> batch in Split(resources, 64))
            {
                foreach (LockingProcessInfo item in FindLockingProcessesForBatch(batch))
                {
                    result[item.ProcessId] = item;
                }
            }

            return result.Values
                .OrderBy(item => item.ProcessName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.ProcessId)
                .ToList();
        }

        public static List<BlockedPathInfo> FindBlockedDirectoryPaths(string path)
        {
            List<BlockedPathInfo> result = new List<BlockedPathInfo>();

            if (string.IsNullOrWhiteSpace(path))
            {
                return result;
            }

            string fullPath;

            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch
            {
                return result;
            }

            if (!Directory.Exists(fullPath))
            {
                return result;
            }

            foreach (string directory in SafeEnumerateDirectoriesIncludingRoot(fullPath))
            {
                if (IsDirectoryHandleBlocked(directory, out string reason))
                {
                    result.Add(new BlockedPathInfo
                    {
                        Path = directory,
                        Reason = reason
                    });
                }
            }

            return result
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsDirectoryHandleBlocked(string directoryPath, out string reason)
        {
            reason = "";

            try
            {
                using SafeFileHandle handle = CreateFileW(
                    directoryPath,
                    0,
                    0,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics,
                    IntPtr.Zero);

                if (!handle.IsInvalid)
                {
                    return false;
                }

                int error = Marshal.GetLastWin32Error();

                if (error == ErrorSharingViolation || error == ErrorLockViolation)
                {
                    reason = "目录正在被其他进程占用。常见原因：CMD / PowerShell 当前目录在这里，或 Explorer / 其他程序打开了该目录。";
                    return true;
                }

                if (error == ErrorAccessDenied)
                {
                    // Access Denied 不一定是占用，也可能是权限问题。为了避免误报成占用，这里只返回说明。
                    reason = "无法独占检查该目录，可能是权限不足或目录正在被系统占用。";
                    return true;
                }

                reason = $"目录独占检查失败，Win32Error={error}";
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        private static List<string> CollectResources(string path)
        {
            HashSet<string> resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                string fullPath = Path.GetFullPath(path);

                if (File.Exists(fullPath))
                {
                    resources.Add(fullPath);
                    return resources.ToList();
                }

                if (!Directory.Exists(fullPath))
                {
                    return resources.ToList();
                }

                foreach (string directory in SafeEnumerateDirectoriesIncludingRoot(fullPath))
                {
                    resources.Add(directory);
                }

                foreach (string file in SafeEnumerateFiles(fullPath))
                {
                    resources.Add(file);
                }
            }
            catch{ }

            return resources.ToList();
        }

        private static IEnumerable<string> SafeEnumerateDirectoriesIncludingRoot(string root)
        {
            yield return root;

            Stack<string> directories = new Stack<string>();
            directories.Push(root);

            while (directories.Count > 0)
            {
                string current = directories.Pop();

                string[] subDirectories = Array.Empty<string>();
                try
                {
                    subDirectories = Directory.GetDirectories(current);
                }
                catch{ }

                foreach (string subDirectory in subDirectories)
                {
                    yield return subDirectory;
                    directories.Push(subDirectory);
                }
            }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string root)
        {
            Stack<string> directories = new Stack<string>();
            directories.Push(root);

            while (directories.Count > 0)
            {
                string current = directories.Pop();

                string[] files = Array.Empty<string>();
                try
                {
                    files = Directory.GetFiles(current);
                }
                catch{ }

                foreach (string file in files)
                {
                    yield return file;
                }

                string[] subDirectories = Array.Empty<string>();
                try
                {
                    subDirectories = Directory.GetDirectories(current);
                }
                catch{ }

                foreach (string subDirectory in subDirectories)
                {
                    directories.Push(subDirectory);
                }
            }
        }

        private static IEnumerable<List<T>> Split<T>(IReadOnlyList<T> source, int size)
        {
            for (int i = 0; i < source.Count; i += size)
            {
                List<T> batch = new List<T>();

                for (int j = i; j < source.Count && j < i + size; j++)
                {
                    batch.Add(source[j]);
                }

                yield return batch;
            }
        }

        private static List<LockingProcessInfo> FindLockingProcessesForBatch(List<string> resources)
        {
            List<LockingProcessInfo> result = new List<LockingProcessInfo>();

            uint sessionHandle = 0;
            string sessionKey = Guid.NewGuid().ToString("N");

            int startResult = RmStartSession(out sessionHandle, 0, sessionKey);
            if (startResult != 0)
            {
                return result;
            }

            try
            {
                int registerResult = RmRegisterResources(
                    sessionHandle,
                    (uint)resources.Count,
                    resources.ToArray(),
                    0,
                    null,
                    0,
                    null);

                if (registerResult != 0)
                {
                    return result;
                }

                uint procInfoNeeded;
                uint procInfo = 0;
                uint rebootReasons = RmRebootReasonNone;

                int listResult = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, null, ref rebootReasons);
                if (listResult != ErrorMoreData || procInfoNeeded == 0)
                {
                    return result;
                }

                procInfo = procInfoNeeded;
                RM_PROCESS_INFO[] processInfo = new RM_PROCESS_INFO[procInfoNeeded];

                listResult = RmGetList(sessionHandle, out procInfoNeeded, ref procInfo, processInfo, ref rebootReasons);
                if (listResult != 0)
                {
                    return result;
                }

                for (int i = 0; i < procInfo; i++)
                {
                    int processId = processInfo[i].Process.dwProcessId;
                    result.Add(CreateProcessInfo(processId, processInfo[i].strAppName));
                }
            }
            finally
            {
                RmEndSession(sessionHandle);
            }

            return result;
        }

        private static LockingProcessInfo CreateProcessInfo(int processId, string fallbackName)
        {
            string processName = fallbackName;
            string mainWindowTitle = "";
            string executablePath = "";

            try
            {
                using Process process = Process.GetProcessById(processId);

                if (!string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    processName = process.ProcessName;
                }

                mainWindowTitle = process.MainWindowTitle;

                try
                {
                    executablePath = process.MainModule?.FileName ?? "";
                }
                catch
                {
                    executablePath = "";
                }
            }
            catch
            {
                if (string.IsNullOrWhiteSpace(processName))
                {
                    processName = "未知进程";
                }
            }

            return new LockingProcessInfo
            {
                ProcessId = processId,
                ProcessName = processName,
                MainWindowTitle = mainWindowTitle,
                ExecutablePath = executablePath
            };
        }
    }
}
