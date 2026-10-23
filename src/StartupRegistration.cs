using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace RaskladWindows {
    sealed class StartupRegistration {
        private const string ApprovalKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";
        private readonly string shortcutPath, executablePath;
        internal StartupRegistration(string shortcutPath, string executablePath) {
            this.shortcutPath = shortcutPath; this.executablePath = Path.GetFullPath(executablePath);
        }
        private object Shortcut(out object shell) {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            return shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
        }
        internal bool Enabled {
            get {
                if (!File.Exists(shortcutPath)) return false;
                object shell = null, link = null;
                try {
                    link = Shortcut(out shell);
                    string target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                    string arguments = (string)link.GetType().InvokeMember("Arguments", BindingFlags.GetProperty, null, link, null);
                    if (!String.Equals(Path.GetFullPath(target), executablePath, StringComparison.OrdinalIgnoreCase) ||
                        !String.IsNullOrWhiteSpace(arguments)) return false;
                    // Respect a disable made through Windows Startup Apps / Task Manager.
                    using (RegistryKey approval = Registry.CurrentUser.OpenSubKey(ApprovalKey)) {
                        byte[] state = approval == null ? null : approval.GetValue(Path.GetFileName(shortcutPath)) as byte[];
                        return state == null || state.Length == 0 || (state[0] != 3 && state[0] != 7);
                    }
                } finally {
                    if (link != null) Marshal.FinalReleaseComObject(link);
                    if (shell != null) Marshal.FinalReleaseComObject(shell);
                }
            }
        }
        internal void SetEnabled(bool enabled) {
            if (!enabled) { File.Delete(shortcutPath); return; }
            object shell = null, link = null;
            try {
                Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
                link = Shortcut(out shell);
                Type type = link.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { executablePath });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { Path.GetDirectoryName(executablePath) });
                type.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { "" });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "Rasklad" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                using (RegistryKey approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, true))
                    if (approval != null) approval.DeleteValue(Path.GetFileName(shortcutPath), false);
                if (!Enabled) throw new IOException("The startup shortcut could not be enabled.");
            } finally {
                if (link != null) Marshal.FinalReleaseComObject(link);
                if (shell != null) Marshal.FinalReleaseComObject(shell);
            }
        }
        internal static int Check() {
            string path = Path.Combine(Path.GetTempPath(), "Rasklad-startup-test-" + Guid.NewGuid().ToString("N") + ".lnk");
            try {
                StartupRegistration registration = new StartupRegistration(path, System.Windows.Forms.Application.ExecutablePath);
                if (registration.Enabled) throw new Exception("Unexpected initial startup entry");
                registration.SetEnabled(true);
                if (!File.Exists(path) || !registration.Enabled) throw new Exception("Startup enable failed");
                if (new StartupRegistration(path, Path.Combine(Path.GetTempPath(), "other.exe")).Enabled) throw new Exception("Wrong executable accepted");
                using (RegistryKey approval = Registry.CurrentUser.CreateSubKey(ApprovalKey)) {
                    approval.SetValue(Path.GetFileName(path), new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
                    if (registration.Enabled) throw new Exception("Windows-disabled startup accepted");
                }
                registration.SetEnabled(true);
                if (!registration.Enabled) throw new Exception("Startup re-enable failed");
                registration.SetEnabled(false);
                if (File.Exists(path) || registration.Enabled) throw new Exception("Startup disable failed");
                registration.SetEnabled(false);
                Console.WriteLine("startup shortcut: enable, target validation, Windows-disabled state, re-enable, disable PASS");
                return 0;
            } finally {
                File.Delete(path);
                using (RegistryKey approval = Registry.CurrentUser.OpenSubKey(ApprovalKey, true))
                    if (approval != null) approval.DeleteValue(Path.GetFileName(path), false);
            }
        }
    }
}
