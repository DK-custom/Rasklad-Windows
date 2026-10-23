using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    static class FnKeyChecks {
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
        internal static int Run() {
            Program.TestMode = true; Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp(); Enable(app, true); int result = 0;
            using (Form form = new Form { Text = "Rasklad Fn test", Size = new Size(500, 200) })
            using (TextBox box = new TextBox { Dock = DockStyle.Top, Text = "unchanged" })
            using (TextBox other = new TextBox { Dock = DockStyle.Bottom }) {
                form.Controls.Add(box); form.Controls.Add(other);
                form.Shown += async delegate {
                    try {
                        uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                        bool attached = previous != current && previous != 0 && Native.AttachThreadInput(current, previous, true);
                        Native.SetForegroundWindow(form.Handle); box.Focus(); box.SelectAll();
                        if (attached) Native.AttachThreadInput(current, previous, false);
                        await Task.Delay(150); Check(box.Focused, "focus");
                        IntPtr english = (IntPtr)typeof(TrayApp).GetField("englishLayout", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(app);
                        IntPtr ignored; Native.SendMessageTimeout(form.Handle, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, english, 2, 150, out ignored);
                        for (int tap = 0; tap < 6; tap++) {
                            Event(app, 255, 99, false); Event(app, 255, 99, false); await Task.Delay(40);
                            Check(Language(form) == (tap % 2 == 0 ? 0x409 : 0x419), "no change before release " + tap);
                            Event(app, 255, 99, true); await Task.Delay(120);
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
                        Enable(app, true); Event(app, 255, 99, false); other.Focus(); Event(app, 255, 99, true); await Task.Delay(120);
                        Check(Language(form) == before, "focus change cancels Fn");
                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
