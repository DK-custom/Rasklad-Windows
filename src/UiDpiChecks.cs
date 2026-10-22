using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace RaskladWindows {
    static class UiDpiChecks {
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
        internal static int Run() {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (TrayMenu menu = new TrayMenu())
            using (Form form = new Form()) {
                uint before = GetDpiForWindow(form.Handle);
                Size initialIcon = AppBranding.TrayIcon.Size;
                using (Graphics graphics = form.CreateGraphics()) {
                    Console.WriteLine("before UIA: windowDpi=" + before + " graphicsDpi=" + graphics.DpiY +
                        " menu=" + menu.Font.Name + " " + menu.Font.SizeInPoints + "pt icon=" + initialIcon);
                    AutomationElement root = AutomationElement.RootElement;
                    uint after = GetDpiForWindow(form.Handle);
                    using (Graphics refreshed = form.CreateGraphics()) {
                        Console.WriteLine("after UIA: windowDpi=" + after + " graphicsDpi=" + refreshed.DpiY +
                            " systemMenu=" + SystemFonts.MenuFont.SizeInPoints + "pt");
                        if (root == null || before != after || Math.Abs(refreshed.DpiY - after) > 0.1 ||
                            menu.Font.SizeInPoints != 10F || !menu.Font.FontFamily.Equals(SystemFonts.MenuFont.FontFamily) ||
                            initialIcon.Width != GetSystemMetricsForDpi(49, after) ||
                            initialIcon.Height != GetSystemMetricsForDpi(50, after)) {
                            Console.Error.WriteLine("FAIL: late DPI change, stale font/icon, or bitmap scaling");
                            return 1;
                        }
                    }
                }
                Console.WriteLine("PASS: stable startup DPI, native graphics scale, 10pt menu font and tray icon size");
                return 0;
            }
        }
    }
}
