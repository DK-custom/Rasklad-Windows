using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RaskladWindows {
    // Only successful undo operations are observed. Counts deliberately last for this session.
    sealed class UndoLearning {
        private readonly HashSet<string> once = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal void Clear() { once.Clear(); }
        internal bool Observe(string source, string target) {
            string key = source + "\t" + target;
            if (once.Remove(key)) return true;
            if (once.Count >= 1024) once.Clear();
            once.Add(key);
            return false;
        }
    }

    static class DiagnosticLog {
        internal static readonly string PathName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rasklad-diagnostics.log");
        private static readonly object gate = new object();
        private static readonly object fileGate = new object();
        private static readonly Dictionary<string, DateTime> last = new Dictionary<string, DateTime>();
        internal static void Write(string reason) {
            if (Program.TestMode) return;
            // Callers pass fixed reason codes only; never text, selection, window titles or clipboard data.
            lock (gate) {
                DateTime now = DateTime.UtcNow, previous;
                if (last.TryGetValue(reason, out previous) && (now - previous).TotalSeconds < 10) return;
                last[reason] = now;
            }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate {
                try {
                    lock (fileGate) {
                        if (File.Exists(PathName) && new FileInfo(PathName).Length > 1024 * 1024) {
                            File.Copy(PathName, PathName + ".previous", true);
                            File.WriteAllText(PathName, "");
                        }
                        File.AppendAllText(PathName, DateTime.UtcNow.ToString("o") + " " + reason + Environment.NewLine);
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
            });
        }
    }

    static class InputAccess {
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
        [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(IntPtr token, int kind, IntPtr buffer, int length, out int required);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthorityCount(IntPtr sid);
        [DllImport("advapi32.dll")] static extern IntPtr GetSidSubAuthority(IntPtr sid, uint index);
        private static readonly int selfLevel = ReadLevel((uint)Process.GetCurrentProcess().Id);
        private static uint cachedPid;
        private static int cachedLevel = -1;
        private static DateTime checkedAt;
        internal static bool LevelsAllow(int source, int target) { return source >= 0 && target >= 0 && source >= target; }
        internal static int ReadLevel(uint pid) {
            IntPtr process = OpenProcess(0x1000, false, pid), token = IntPtr.Zero, buffer = IntPtr.Zero;
            if (process == IntPtr.Zero) return -1;
            try {
                if (!OpenProcessToken(process, 8, out token)) return -1;
                int length;
                GetTokenInformation(token, 25, IntPtr.Zero, 0, out length); // TokenIntegrityLevel
                if (length <= 0 || length > 65536) return -1;
                buffer = Marshal.AllocHGlobal(length);
                if (!GetTokenInformation(token, 25, buffer, length, out length)) return -1;
                IntPtr sid = Marshal.ReadIntPtr(buffer);
                byte count = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
                return count == 0 ? -1 : Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(count - 1)));
            } finally {
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (token != IntPtr.Zero) CloseHandle(token);
                CloseHandle(process);
            }
        }
        internal static bool Allows(IntPtr window) {
            uint pid;
            if (window == IntPtr.Zero || Native.GetWindowThreadProcessId(window, out pid) == 0 || pid == 0) {
                DiagnosticLog.Write("skip.window_unavailable"); return false;
            }
            // No window-title or control cache: only a short-lived process integrity query.
            if (pid != cachedPid || (DateTime.UtcNow - checkedAt).TotalSeconds >= 1) {
                cachedPid = pid; cachedLevel = ReadLevel(pid); checkedAt = DateTime.UtcNow;
            }
            if (LevelsAllow(selfLevel, cachedLevel)) return true;
            DiagnosticLog.Write(cachedLevel < 0 || selfLevel < 0 ? "skip.integrity_unknown" : "skip.higher_integrity");
            return false;
        }
    }

    static class HintPosition {
        [DllImport("oleacc.dll")] static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId,
            ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object result);
        internal static bool Visible(Rectangle rectangle) {
            if (rectangle.Height <= 0 || rectangle.Height > 600 || rectangle.Width < 0) return false;
            foreach (Screen screen in Screen.AllScreens) if (screen.Bounds.IntersectsWith(rectangle)) return true;
            return false;
        }
        // Called on the existing bounded background context query, never in the keyboard hook.
        internal static Point? MsaaCaret(IntPtr focus) {
            object value = null;
            try {
                Guid iid = new Guid("618736e0-3c3d-11cf-810c-00aa00389b71");
                if (AccessibleObjectFromWindow(focus, unchecked((uint)-8), ref iid, out value) != 0 || value == null) return null;
                int x, y, width, height;
                ((Accessibility.IAccessible)value).accLocation(out x, out y, out width, out height, 0);
                Rectangle rectangle = new Rectangle(x, y, Math.Max(1, width), height);
                return Visible(rectangle) ? new Point(rectangle.Right, rectangle.Top) : (Point?)null;
            } catch (COMException) { return null; } catch (InvalidCastException) { return null; }
            finally { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        }
    }
}

