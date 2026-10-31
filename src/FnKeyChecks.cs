using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    static class FnKeyChecks {
        [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
        private static void Event(TrayApp app, uint vk, uint scan, bool up, bool extended = true) {
            Native.KbdData key = new Native.KbdData { vkCode = vk, scanCode = scan, flags = (extended ? 1u : 0u) | (up ? 128u : 0u) };
            IntPtr data = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.KbdData)));
            try {
                Marshal.StructureToPtr(key, data, false);
                typeof(TrayApp).GetMethod("KeyboardHook", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(app,
                    new object[] { 0, (IntPtr)(up ? Native.WM_KEYUP : Native.WM_KEYDOWN), data });
            } finally { Marshal.FreeHGlobal(data); }
        }
        private static void Enable(TrayApp app, bool enabled) {
            FieldInfo field = typeof(TrayApp).GetField("fnEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null) field.SetValue(app, enabled);
        }
        private static int Language(Form form) { return unchecked((ushort)Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(form.Handle, IntPtr.Zero)).ToInt64()); }
        private static void Check(bool value, string name) { Console.WriteLine(name + ": " + value); if (!value) throw new Exception(name); }
        private static async Task Stress(TrayApp app, Form form, TextBox box) {
            foreach (int interval in new[] { 5, 40, 120 }) {
                Queue<double> sent = new Queue<double>();
                List<double> latency = new List<double>();
                Stopwatch clock = Stopwatch.StartNew();
                double lastReceived = 0;
                int requests = interval == 5 ? 400 : 40;
                int direct = app.FnDirectForTest, fallback = app.FnFallbackForTest;
                InputLanguageChangedEventHandler changed = delegate {
                    if (sent.Count > 0) latency.Add(clock.Elapsed.TotalMilliseconds - sent.Dequeue());
                    lastReceived = clock.Elapsed.TotalMilliseconds;
                };
                form.InputLanguageChanged += changed;
                try {
                    for (int i = 0; i < requests; i++) {
                        if (Native.GetForegroundWindow() != form.Handle) {
                            uint pid; Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out pid);
                            Console.WriteLine("stress focus lost at=" + i + " direct=" + (app.FnDirectForTest - direct) +
                                " fallback=" + (app.FnFallbackForTest - fallback) + " foregroundProcess=" + Process.GetProcessById((int)pid).ProcessName);
                            throw new Exception("Stress fixture lost foreground");
                        }
                        sent.Enqueue(clock.Elapsed.TotalMilliseconds);
                        Event(app, 255, 99, false); Event(app, 255, 99, true);
                        await Task.Delay(interval);
                    }
                    double finished = clock.Elapsed.TotalMilliseconds;
                    for (int i = 0; i < 60 && sent.Count > 0; i++) await Task.Delay(50);
                    latency.Sort();
                    double p95 = latency.Count == 0 ? -1 : latency[(latency.Count - 1) * 95 / 100];
                    double tail = Math.Max(0, lastReceived - finished);
                    Console.WriteLine("Fn stress interval=" + interval + "ms requests=" + requests + " received=" + latency.Count +
                        " direct=" + (app.FnDirectForTest - direct) + " fallback=" + (app.FnFallbackForTest - fallback) +
                        " p95=" + p95.ToString("F1") + "ms tail=" + tail.ToString("F1") + "ms");
                    Check(latency.Count == requests && p95 <= 120 && tail <= 100, "Fn burst stays responsive");
                    Check(box.Text == "unchanged" && box.SelectionLength == 9, "stress text and selection unchanged");
                } finally { form.InputLanguageChanged -= changed; }
            }
        }
        internal static int Run(bool stress = false) {
            Program.TestMode = true; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp(); Enable(app, true); int result = 0;
            using (Form form = new Form { Text = "Rasklad Fn test", Size = new Size(500, 200) })
            using (TextBox box = new TextBox { Dock = DockStyle.Top, Text = "unchanged" })
            using (TextBox other = new TextBox { Dock = DockStyle.Bottom }) {
                form.Controls.Add(box); form.Controls.Add(other);
                form.Shown += async delegate {
                    try {
                        await Task.Delay(750); // Let the launcher/approval window finish activating.
                        uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                        bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
                        Native.ShowWindow(form.Handle, 1); Native.SetForegroundWindow(form.Handle);
                        form.ActiveControl = box; box.Select(); box.Focus(); box.SelectAll();
                        if (attached) Native.AttachThreadInput(current, previous, false);
                        await Task.Delay(150); Check(box.Focused && Native.GetForegroundWindow() == form.Handle, "focus");
                        IntPtr english = (IntPtr)typeof(TrayApp).GetField("englishLayout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        IntPtr ignored; Native.SendMessageTimeout(form.Handle, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english, 2, 150, out ignored);
                        box.SelectAll(); // Select after initial language/font setup, before testing Fn.
                        Check(box.Text == "unchanged" && box.SelectionLength == 9, "initial text and selection after language setup");
                        if (stress) { await Stress(app, form, box); return; }
                        for (int tap = 0; tap < 6; tap++) {
                            Event(app, 255, 99, false); Event(app, 255, 99, false); await Task.Delay(40);
                            Check(Language(form) == (tap % 2 == 0 ? 0x409 : 0x419), "no change before release " + tap);
                            Event(app, 255, 99, true); await Task.Delay(350);
                            Check(Language(form) == (tap % 2 == 0 ? 0x419 : 0x409), "Fn toggle " + tap);
                            Check(box.Text == "unchanged" && box.SelectionLength == 9, "text/selection unchanged " + tap);
                        }
                        int before = Language(form);
                        Event(app, 255, 99, false); Event(app, 112, 59, false, false); Event(app, 112, 59, true, false); Event(app, 255, 99, true);
                        await Task.Delay(120); Check(Language(form) == before, "Fn combination unchanged");
                        Event(app, 255, 98, false); Event(app, 255, 98, true); await Task.Delay(120);
                        Check(Language(form) == before, "other scan code ignored");
                        Enable(app, false); Event(app, 255, 99, false); Event(app, 255, 99, true); await Task.Delay(120);
                        Check(Language(form) == before, "disabled Fn unchanged");
                        Enable(app, true); Event(app, 255, 99, false);
                        form.ActiveControl = other; other.Select(); other.Focus();
                        Check(other.Focused, "fixture moved focus to second field");
                        before = Language(form);
                        Event(app, 255, 99, true); await Task.Delay(120);
                        Check(Language(form) != before, "Fn works after focus changes within window");
                        before = Language(form); form.ActiveControl = null; SetFocus(form.Handle);
                        Check(!box.Focused && !other.Focused, "no text input focused");
                        Event(app, 255, 99, false); Event(app, 255, 99, true); await Task.Delay(350);
                        Check(Language(form) != before, "Fn works without text input");
                        Check(box.Text == "unchanged" && other.Text == "", "Fn sends no text");
                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
