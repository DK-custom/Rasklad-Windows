using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    sealed class UiPalette {
        internal readonly Color Back, Fore, Muted, Accent, Border, Hover;
        internal UiPalette(bool dark) {
            dark = dark && !SystemInformation.HighContrast;
            Back = dark ? Color.FromArgb(32, 34, 38) : SystemColors.Window;
            Fore = dark ? Color.FromArgb(244, 245, 247) : SystemColors.WindowText;
            Muted = dark ? Color.FromArgb(168, 175, 187) : SystemColors.GrayText;
            Accent = dark ? Color.FromArgb(122, 180, 255) : SystemColors.Highlight;
            Border = dark ? Color.FromArgb(65, 69, 76) : SystemColors.ControlDark;
            Hover = SystemInformation.HighContrast ? SystemColors.Highlight :
                dark ? Color.FromArgb(49, 54, 62) : Color.FromArgb(235, 241, 249);
        }
    }

    static class HintTheme {
        internal static bool IsDark() {
            try {
                object value = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                return value is int && (int)value == 0;
            } catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }
    }

    sealed class HintSurfaceRenderer : ToolStripRenderer {
        private readonly Color background, border;
        private readonly int radius;
        internal HintSurfaceRenderer(Color background, Color border, int radius) {
            this.background = background; this.border = border; this.radius = radius;
        }
        internal static GraphicsPath Shape(RectangleF bounds, int radius) {
            GraphicsPath path = new GraphicsPath();
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            if (diameter <= 0) { path.AddRectangle(bounds); return path; }
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Shape(new RectangleF(0.5F, 0.5F,
                e.ToolStrip.Width - 1, e.ToolStrip.Height - 1), radius))
            using (Brush brush = new SolidBrush(background))
            using (Pen pen = new Pen(border)) { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    static class HintDesignTests {
        private static void Require(bool value, string message) {
            if (!value) throw new Exception(message);
        }
        internal static int Run() {
            Program.TestMode = true;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp();
            int result = 0;
            using (Form form = new Form { Text = "Проверка подсказок", Width = 640, Height = 200 })
            using (TextBox input = new TextBox { Dock = DockStyle.Fill })
            using (SuggestionPopup hint = new SuggestionPopup()) {
                form.Controls.Add(input);
                form.Shown += async delegate {
                    try {
                        await Task.Delay(200);
                        uint previous = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), IntPtr.Zero), current = Native.GetCurrentThreadId();
                        bool attached = previous != 0 && previous != current && Native.AttachThreadInput(current, previous, true);
                        Native.ShowWindow(form.Handle, 1); Native.SetForegroundWindow(form.Handle);
                        form.ActiveControl = input; input.Select(); input.Focus();
                        if (attached) Native.AttachThreadInput(current, previous, false);
                        Require(input.Focused && Native.GetForegroundWindow() == form.Handle, "test focus");
                        Rectangle field = input.RectangleToScreen(input.ClientRectangle);
                        Point? discovered = TrayApp.PopupAnchor();
                        Require(discovered.HasValue && field.Contains(discovered.Value), "native text caret discovery");
                        Point caret = new Point(field.Left + field.Width / 2, field.Top + 80);
                        foreach (bool dark in new[] { false, true }) {
                            string style = dark ? "Dark" : "Light";
                            hint.ForceDarkForTest = dark;
                            string[] texts = { "b → и", "ghbdtn → привет", "↩ hello\nотмена замены",
                                new String('я', 100) + " → " + new String('a', 100) };
                            foreach (string text in texts) {
                                hint.ShowAt(caret, field, text, 5000);
                                Require(hint.FitsForTest, "clipped text: " + style);
                                Require(hint.InkCenteredForTest, "visible glyph margins not symmetric: " + style);
                                if (!SystemInformation.HighContrast) Require(hint.RoundedForTest, "hint corners not rounded");
                                Require(hint.ActuallyVisibleForTest && hint.NoTaskbarButtonForTest && input.Focused && Native.GetForegroundWindow() == form.Handle, "visibility/activation/taskbar: " + style);
                                Require(hint.BoundsForTest.Bottom <= caret.Y - 8, "not above caret: " + style);
                                Rectangle workingArea = Screen.FromPoint(caret).WorkingArea;
                                int expectedLeft = Math.Max(workingArea.Left, Math.Min(caret.X - hint.BoundsForTest.Width / 2, workingArea.Right - hint.BoundsForTest.Width));
                                Require(hint.BoundsForTest.Left == expectedLeft, "not centered/clamped: " + style + " bounds=" + hint.BoundsForTest + " caret=" + caret);
                                Require(Screen.FromRectangle(hint.BoundsForTest).WorkingArea.Contains(hint.BoundsForTest), "screen bounds: " + style);
                            }
                            Rectangle area = Screen.FromPoint(caret).WorkingArea;
                            Point topCaret = new Point(area.Left + 100, area.Top + 2);
                            hint.ShowAt(topCaret, null, "ghbdtn → привет", 5000);
                            Require(hint.BoundsForTest.Top >= topCaret.Y + 20 && area.Contains(hint.BoundsForTest), "top edge fallback: " + style);
                            hint.ShowAt(caret, null, "ghbdtn → привет", 5000);
                            Require(hint.BoundsForTest.Height < 33, "not smaller than old compact: " + style);
                            Require(hint.DarkForTest == (dark && !SystemInformation.HighContrast), "theme override: " + style);
                            hint.SavePreview(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hint-" + style + ".png"));
                            Console.WriteLine(style + ": layout, focus, taskbar, centered above caret, smaller size, screen edge PASS");
                        }
                        hint.ForceDarkForTest = null;
                        hint.ShowAt(caret, null, "ghbdtn → привет", 5000);
                        Require(hint.DarkForTest == (HintTheme.IsDark() && !SystemInformation.HighContrast), "automatic Windows theme");
                        Console.WriteLine("automatic Windows theme PASS; dark=" + hint.DarkForTest);
                        hint.Dismiss();
                        string prefix = "prefix prefix prefix ";
                        input.Text = prefix + "abcdefghij"; input.SelectionStart = input.TextLength;
                        Point? before = TrayApp.PopupAnchor();
                        app.ReplaceAndNotifyForTest("abcdefghij", "и");
                        await Task.Delay(600);
                        Point? after = TrayApp.PopupAnchor();
                        Require(input.Text == prefix + "и" && before.HasValue && after.HasValue && before.Value.X != after.Value.X,
                            "test caret did not move after replacement");
                        Require(app.PopupVisibleForTest && app.PopupFitsForTest, "replacement hint missing");
                        Require(Math.Abs(app.PopupBoundsForTest.Left + app.PopupBoundsForTest.Width / 2 - after.Value.X) <= 1,
                            "hint centered on old caret, not the replacement caret");
                        Console.WriteLine("real replacement: refreshed caret, centered hint PASS");
                        app.ReplaceAndNotifyForTest("и", "hello");
                        await Task.Delay(10);
                        Native.Input down = new Native.Input { type = 1 }; down.data.key.vk = 88;
                        Native.Input up = down; up.data.key.flags = Native.KEYEVENTF_KEYUP;
                        Native.SendInput(2, new[] { down, up }, System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.Input)));
                        await Task.Delay(500);
                        Require(!app.PopupVisibleForTest, "late hint reappeared after continued typing");
                        Console.WriteLine("continued typing cancels pending hint PASS");
                    } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                    finally { form.Close(); app.ExitThread(); }
                };
                form.Show(); Application.Run(app);
            }
            return result;
        }
    }
}
