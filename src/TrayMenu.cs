using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RaskladWindows {
    sealed class TrayMenu : ContextMenuStrip {
        private readonly Font menuFont;
        internal bool? ForceDarkForTest;
        internal bool DarkForTest { get; private set; }
        internal TrayMenu() {
            menuFont = new Font(SystemFonts.MenuFont.FontFamily, 10F, SystemFonts.MenuFont.Style, GraphicsUnit.Point);
            Font = menuFont;
            ShowImageMargin = false; ShowCheckMargin = true;
        }
        protected override void OnOpening(CancelEventArgs e) {
            DarkForTest = (Program.TestMode && ForceDarkForTest.HasValue ? ForceDarkForTest.Value : HintTheme.IsDark()) && !SystemInformation.HighContrast;
            UiPalette palette = new UiPalette(DarkForTest);
            BackColor = palette.Back; ForeColor = palette.Fore;
            Renderer = new TrayMenuRenderer(palette);
            foreach (ToolStripItem item in Items) {
                item.Font = menuFont;
                item.Margin = Padding.Empty;
                if (item is ToolStripMenuItem) item.Padding = Padding.Empty;
            }
            base.OnOpening(e);
            PerformLayout();
            UpdateShape();
        }
        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); UpdateShape(); }
        private void UpdateShape() {
            if (Width < 2 || Height < 2) return;
            Region old = Region;
            using (GraphicsPath path = HintSurfaceRenderer.Shape(new RectangleF(0, 0, Width, Height), SystemInformation.HighContrast ? 0 : 10))
                Region = new Region(path);
            if (old != null) old.Dispose();
        }
        protected override void Dispose(bool disposing) {
            base.Dispose(disposing);
            if (disposing) menuFont.Dispose();
        }
    }

    sealed class TrayMenuRenderer : ToolStripProfessionalRenderer {
        private readonly UiPalette colors;
        internal TrayMenuRenderer(UiPalette colors) { this.colors = colors; RoundedEdges = false; }
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = HintSurfaceRenderer.Shape(new RectangleF(0.5F, 0.5F, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1),
                SystemInformation.HighContrast ? 0 : 10))
            using (Brush fill = new SolidBrush(colors.Back))
            using (Pen outline = new Pen(colors.Border)) { e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(outline, path); }
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e) {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = HintSurfaceRenderer.Shape(new RectangleF(1, 0, e.Item.Width - 2, e.Item.Height),
                SystemInformation.HighContrast ? 0 : 4))
            using (Brush fill = new SolidBrush(colors.Hover)) e.Graphics.FillPath(fill, path);
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e) {
            e.TextColor = !e.Item.Enabled ? colors.Muted : e.Item.Selected && SystemInformation.HighContrast ? SystemColors.HighlightText : colors.Fore;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) {
            Rectangle bounds = e.ImageRectangle;
            float x = bounds.Left + bounds.Width / 2F, y = bounds.Top + bounds.Height / 2F;
            Color color = !e.Item.Enabled ? colors.Muted : SystemInformation.HighContrast && e.Item.Selected ? SystemColors.HighlightText : colors.Accent;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (Pen pen = new Pen(color, 2F)) {
                pen.StartCap = pen.EndCap = LineCap.Round;
                e.Graphics.DrawLines(pen, new[] { new PointF(x - 5, y), new PointF(x - 1, y + 4), new PointF(x + 6, y - 4) });
            }
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e) {
            using (Pen pen = new Pen(colors.Border)) e.Graphics.DrawLine(pen, 8, e.Item.Height / 2, e.Item.Width - 8, e.Item.Height / 2);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e) {
            e.ArrowColor = e.Item.Enabled ? colors.Fore : colors.Muted;
            base.OnRenderArrow(e);
        }
    }

    static class TrayMenuTests {
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        internal static int Run() {
            Program.TestMode = true;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp();
            Form form = new Form { Text = "Проверка меню", Width = 600, Height = 200 };
            int result = 0;
            form.Shown += async delegate {
                try {
                    await Task.Delay(200);
                    Native.ShowWindow(form.Handle, 1);
                    TrayMenu menu = app.MenuForTest;
                    Require(app.BrandingForTest, "tray name/icon mismatch");
                    Require(menu.Items.Count == 10 && menu.Items[9].Text == "Выход", "menu commands changed");
                    Require(menu.Font.SizeInPoints == 10F && menu.Font.FontFamily.Equals(SystemFonts.MenuFont.FontFamily), "menu font must be 10pt with system family");
                    foreach (bool dark in new[] { false, true }) {
                        menu.ForceDarkForTest = dark;
                        menu.Show(form, new Point(20, 20));
                        await Task.Delay(100);
                        Require(menu.Visible && Native.IsWindowVisible(menu.Handle), "menu hidden");
                        Require(menu.DarkForTest == (dark && !SystemInformation.HighContrast), "wrong theme");
                        if (!SystemInformation.HighContrast) Require(!menu.Region.IsVisible(0, 0) && menu.Region.IsVisible(10, 0), "menu corners not rounded");
                        foreach (ToolStripItem item in menu.Items) {
                            Require(menu.ClientRectangle.Contains(item.Bounds), "clipped row: " + item.Text + " menu=" + menu.ClientRectangle + " item=" + item.Bounds);
                            if (!(item is ToolStripSeparator)) Require(TextRenderer.MeasureText(item.Text, menu.Font).Width <= menu.Width - 40, "clipped label: " + item.Text);
                        }
                        for (int i = 0; i < 4; i++) {
                            ToolStripMenuItem item = (ToolStripMenuItem)menu.Items[i];
                            bool before = item.Checked; item.PerformClick();
                            Require(item.Checked != before, "toggle stopped working");
                            item.PerformClick(); Require(item.Checked == before, "toggle restore failed");
                        }
                        menu.Show(form, new Point(20, 20));
                        menu.Items[0].Select();
                        using (Bitmap bitmap = new Bitmap(menu.Width, menu.Height)) {
                            menu.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "menu-" + (dark ? "Dark" : "Light") + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                        menu.Close();
                        Console.WriteLine((dark ? "Dark" : "Light") + ": visible, labels, commands, check states PASS");
                    }
                    menu.ForceDarkForTest = null;
                    menu.Show(form, new Point(20, 20));
                    Require(menu.DarkForTest == (HintTheme.IsDark() && !SystemInformation.HighContrast), "automatic theme failed");
                    menu.Close(); Console.WriteLine("automatic Windows theme PASS");
                } catch (Exception ex) { Console.Error.WriteLine(ex); result = 1; }
                finally { form.Close(); app.ExitThread(); }
            };
            form.Show(); Application.Run(app); form.Dispose(); return result;
        }
    }
}
