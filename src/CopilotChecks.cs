using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    static class CopilotChecks {
        private delegate void WinEventProc(IntPtr hook, uint evt, IntPtr window, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        private static int startMenuActivations;
        private static readonly WinEventProc callback = delegate(IntPtr hook, uint evt, IntPtr window, int obj, int child, uint thread, uint time) {
            try {
                uint pid; Native.GetWindowThreadProcessId(window, out pid);
                string name = Process.GetProcessById((int)pid).ProcessName;
                if (name.Equals("StartMenuExperienceHost", StringComparison.OrdinalIgnoreCase)) startMenuActivations++;
            } catch { }
        };
        private static void Send(params int[] codes) {
            Native.Input[] keys = new Native.Input[codes.Length];
            for (int i = 0; i < codes.Length; i++) {
                keys[i].type = 1; keys[i].data.key.vk = (ushort)Math.Abs(codes[i]);
                keys[i].data.key.extraInfo = new UIntPtr(0x123);
                if (codes[i] < 0) keys[i].data.key.flags = Native.KEYEVENTF_KEYUP;
            }
            if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) != keys.Length) throw new Exception("Input injection failed");
        }
        private static void Set(TrayApp app, string name, bool value) {
            typeof(TrayApp).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(app, value);
        }
        private static async Task Chord(string mode) {
            if (mode == "fast") Send(91, 160, 134, -134, -160, -91);
            else if (mode == "delayed") { Send(91, 160, 134, -134); await Task.Delay(80); Send(-160, -91); }
            else if (mode == "win-first") Send(91, 160, 134, -134, -91, -160);
            else if (mode == "repeat") Send(91, 160, 134, 134, -134, -160, -91);
            else Send(91, 160, 134, -160, -91, -134);
        }
        private static async Task Focus(Form form, TextBox box) {
            uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
            bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
            Native.ShowWindow(form.Handle, 1); Native.SetForegroundWindow(form.Handle);
            form.ActiveControl = box; box.Select(); box.Focus();
            if (attached) Native.AttachThreadInput(current, previous, false);
            await Task.Delay(150);
            if (!box.Focused || Native.GetForegroundWindow() != form.Handle) throw new Exception("Fixture focus failed");
        }
        internal static int Run() {
            Program.TestMode = true;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp();
            Set(app, "copilotEnabled", true); Set(app, "autoEnabled", false);
            int result = 0;
            IntPtr events = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0);
            if (events == IntPtr.Zero) throw new Exception("Foreground event monitoring failed");
            using (Form form = new Form { Text = "Rasklad Copilot test", Size = new Size(500, 160) })
            using (TextBox box = new TextBox { Dock = DockStyle.Fill }) {
                form.Controls.Add(box);
                form.Shown += async delegate {
                    try {
                        foreach (string mode in new[] { "fast", "delayed", "win-first", "repeat", "f23-last" }) {
                            await Focus(form, box); app.ResetContextForTest();
                            box.Text = "ghbdtn"; box.SelectAll(); startMenuActivations = 0;
                            await Chord(mode);
                            await Task.Delay(800);
                            bool text = box.Text == "привет", focus = Native.GetForegroundWindow() == form.Handle && box.Focused;
                            Console.WriteLine(mode + ": text=" + text + " focus=" + focus + " startMenu=" + startMenuActivations);
                            if (!text || !focus || startMenuActivations != 0) result = 1;
                            await Chord(mode); await Task.Delay(800);
                            bool undo = box.Text == "ghbdtn" && Native.GetForegroundWindow() == form.Handle && box.Focused;
                            Console.WriteLine(mode + ": undo=" + undo + " startMenu=" + startMenuActivations);
                            if (!undo || startMenuActivations != 0) result = 1;
                        }
                        await Focus(form, box); app.ResetContextForTest(); box.UseSystemPasswordChar = true;
                        box.Text = "ghbdtn"; box.SelectAll(); startMenuActivations = 0;
                        await Chord("fast"); await Task.Delay(800);
                        bool password = box.Text == "ghbdtn" && Native.GetForegroundWindow() == form.Handle && box.Focused && startMenuActivations == 0;
                        Console.WriteLine("password protected=" + password + " startMenu=" + startMenuActivations);
                        if (!password) result = 1;
                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { Send(-134, -160, -91); UnhookWinEvent(events); form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
