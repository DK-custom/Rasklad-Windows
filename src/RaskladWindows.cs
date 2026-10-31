using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Mutex = System.Threading.Mutex;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using System.Windows.Forms;

namespace RaskladWindows {
    [ComImport, Guid("00000101-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumStringNative {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.LPWStr)] out string value, out uint fetched);
        [PreserveSig] int Skip(uint count);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IEnumStringNative clone);
    }

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerFactoryNative {
        [PreserveSig] int get_SupportedLanguages(out IEnumStringNative languages);
        [PreserveSig] int IsSupported([MarshalAs(UnmanagedType.LPWStr)] string tag, out int supported);
        [PreserveSig] int CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string tag, out ISpellCheckerNative checker);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellCheckerNative {
        [PreserveSig] int get_LanguageTag([MarshalAs(UnmanagedType.LPWStr)] out string value);
        [PreserveSig] int Check([MarshalAs(UnmanagedType.LPWStr)] string text, out IEnumSpellingErrorNative errors);
        [PreserveSig] int Suggest([MarshalAs(UnmanagedType.LPWStr)] string word, out IEnumStringNative suggestions);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IEnumSpellingErrorNative {
        [PreserveSig] int Next(out ISpellingErrorNative error);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISpellingErrorNative {
        [PreserveSig] int get_StartIndex(out uint value);
        [PreserveSig] int get_Length(out uint value);
        [PreserveSig] int get_CorrectiveAction(out int value);
        [PreserveSig] int get_Replacement([MarshalAs(UnmanagedType.LPWStr)] out string value);
    }

    sealed class WindowsSpeller : IDisposable {
        private ISpellCheckerFactoryNative factory;
        private ISpellCheckerNative russian;
        private ISpellCheckerNative english;

        public WindowsSpeller() {
            Type type = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));
            factory = (ISpellCheckerFactoryNative)Activator.CreateInstance(type);
            int hr = factory.CreateSpellChecker("ru-RU", out russian);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
            hr = factory.CreateSpellChecker("en-US", out english);
            if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        }

        private ISpellCheckerNative ForLanguage(bool russianLanguage) { return russianLanguage ? russian : english; }

        public bool IsCorrect(string word, bool russianLanguage) {
            IEnumSpellingErrorNative errors = null;
            try {
                int hr = ForLanguage(russianLanguage).Check(word, out errors);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                ISpellingErrorNative error;
                hr = errors.Next(out error);
                if (error != null) Marshal.ReleaseComObject(error);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                return error == null;
            } finally {
                if (errors != null) Marshal.ReleaseComObject(errors);
            }
        }

        public List<string> Suggestions(string word, bool russianLanguage, int limit) {
            List<string> result = new List<string>();
            IEnumStringNative suggestions = null;
            try {
                int hr = ForLanguage(russianLanguage).Suggest(word, out suggestions);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                string value;
                uint fetched;
                while (result.Count < limit) {
                    hr = suggestions.Next(1, out value, out fetched);
                    if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                    if (fetched == 0) break;
                    if (!String.IsNullOrWhiteSpace(value) && !value.Equals(word, StringComparison.OrdinalIgnoreCase) &&
                        !result.Contains(value)) result.Add(value);
                }
                return result;
            } finally {
                if (suggestions != null) Marshal.ReleaseComObject(suggestions);
            }
        }

        public string FirstSuggestion(string word, bool russianLanguage) {
            List<string> values = Suggestions(word, russianLanguage, 1);
            return values.Count == 0 ? null : values[0];
        }

        public List<string> TypoSuggestions(string word, bool russianLanguage, int limit) {
            if (String.IsNullOrEmpty(word)) return new List<string>();
            List<string> values = Suggestions(word, russianLanguage, limit);
            if (word.IndexOf('-') < 0) return values;
            // Windows can suggest only the misspelled component of a hyphenated word.
            IEnumSpellingErrorNative errors = null;
            ISpellingErrorNative error = null;
            try {
                int hr = ForLanguage(russianLanguage).Check(word, out errors);
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                hr = errors.Next(out error);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
                if (error == null) return values;
                uint start, length;
                hr = error.get_StartIndex(out start); if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                hr = error.get_Length(out length); if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                if (length == 0 || (ulong)start + length > (ulong)word.Length) return values;
                string prefix = word.Substring(0, (int)start), suffix = word.Substring((int)(start + length));
                List<string> whole = new List<string>();
                foreach (string candidate in values) {
                    string replacement = candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                        candidate.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ? candidate : prefix + candidate + suffix;
                    if (!whole.Contains(replacement)) whole.Add(replacement);
                }
                return whole;
            } finally {
                if (error != null) Marshal.ReleaseComObject(error);
                if (errors != null) Marshal.ReleaseComObject(errors);
            }
        }

        public void Dispose() {
            if (english != null) Marshal.ReleaseComObject(english);
            if (russian != null) Marshal.ReleaseComObject(russian);
            if (factory != null) Marshal.ReleaseComObject(factory);
        }
    }

    static class Native {
        public const int WH_KEYBOARD_LL = 13, WH_MOUSE_LL = 14;
        public const int WM_KEYDOWN = 0x100, WM_KEYUP = 0x101, WM_SYSKEYDOWN = 0x104, WM_SYSKEYUP = 0x105;
        public const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_MBUTTONDOWN = 0x207;
        public const int WM_INPUTLANGCHANGEREQUEST = 0x50;
        public const uint LLKHF_INJECTED = 0x10, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
        public static readonly UIntPtr OurInputMarker = new UIntPtr(0x505753);
        public static readonly UIntPtr ReplayInputMarker = new UIntPtr(0x505754);
        public const int VK_BACK = 8, VK_TAB = 9, VK_RETURN = 13, VK_SHIFT = 16, VK_CONTROL = 17,
            VK_MENU = 18, VK_PAUSE = 19, VK_ESCAPE = 27, VK_SPACE = 32, VK_LWIN = 91, VK_RWIN = 92, VK_F23 = 134;
        public const int GWL_STYLE = -16, ES_PASSWORD = 0x20;

        public delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)] public struct KbdData {
            public uint vkCode, scanCode, flags, time;
            public UIntPtr extraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] public struct KeyInput {
            public ushort vk, scan;
            public uint flags, time;
            public UIntPtr extraInfo;
        }
        [StructLayout(LayoutKind.Sequential)] public struct MouseInput {
            public int dx, dy;
            public uint mouseData, flags, time;
            public UIntPtr extraInfo;
        }
        [StructLayout(LayoutKind.Explicit)] public struct InputUnion {
            [FieldOffset(0)] public KeyInput key;
            [FieldOffset(0)] public MouseInput mouse;
        }
        [StructLayout(LayoutKind.Sequential)] public struct Input {
            public uint type;
            public InputUnion data;
        }
        [StructLayout(LayoutKind.Sequential)] public struct GuiThreadInfo {
            public uint cbSize, flags;
            public IntPtr active, focus, capture, menuOwner, moveSize, caret;
            public Rect caretRect;
        }
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int left, top, right, bottom; }

        [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int hookId, HookProc callback, IntPtr module, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool AttachThreadInput(uint attach, uint attachTo, bool value);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr processId);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] public static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] public static extern int GetKeyboardLayoutList(int count, [Out] IntPtr[] layouts);
        [DllImport("user32.dll")] public static extern IntPtr ActivateKeyboardLayout(IntPtr layout, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr LoadKeyboardLayout(string id, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetKeyboardState(byte[] state);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, StringBuilder output, int capacity, uint flags, IntPtr layout);
        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, [In] Input[] inputs, int size);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern short GetKeyState(int key);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ClientToScreen(IntPtr window, ref Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern short VkKeyScanEx(char character, IntPtr layout);
    }

    sealed class KeyStroke {
        public uint vk, scan;
        public int language;
        public byte[] keyboardState;
        public char character;
    }

    sealed class PendingWord {
        public string original, converted, sourceCheck, targetCheck, suffix, wholeTarget;
        public bool sourceRussian, mixedLayout;
        public int delimiterVk;
        public char delimiter;
        public IntPtr foreground, focus, originalLayout;
    }

    sealed class UndoRecord {
        public string original, converted;
        public int delimiterVk;
        public char delimiter;
        public IntPtr foreground, focus, sourceLayout;
        public bool typo, sourceRussian, punctuation;
        public Rectangle? fieldBounds;
        public Point? caretPoint;
        public int selectionStart = -1;
    }

    sealed class InputContext {
        public bool Protected = true;
        public Rectangle? FieldBounds;
        public Point? CaretPoint;
        public string SkipReason = "skip.context_unavailable";
        public bool CodeField;
        public bool CaretCode, CaretCodeBlock;
        public string BeforeCaret;
        public string SelectionText;
        public int NativeSelectionStart = -1;
        public TextPatternRange SelectionRange;
    }

    sealed class PopupTextItem : ToolStripItem {
        public override bool CanSelect { get { return false; } }
        internal int HorizontalPadding = 3, VerticalPadding = 3;
        internal Color MutedColor, AccentColor;
        private Size desiredSize = new Size(170, 48);
        public void SetContent(string content, Size size) { Text = content; desiredSize = size; }
        public override Size GetPreferredSize(Size constrainingSize) { return desiredSize; }
        protected override void OnPaint(PaintEventArgs e) {
            // GDI advance widths include bearings/rounding; center the actual painted glyphs.
            using (Bitmap layer = new Bitmap(Width, Height)) {
                layer.SetResolution(e.Graphics.DpiX, e.Graphics.DpiY);
                using (Graphics ownerGraphics = Owner.CreateGraphics())
                using (Font pixelFont = new Font(Font.FontFamily, Font.SizeInPoints * ownerGraphics.DpiY / 72F, Font.Style, GraphicsUnit.Pixel))
                using (Graphics graphics = Graphics.FromImage(layer)) {
                    graphics.Clear(BackColor);
                    PaintText(graphics, new Rectangle(HorizontalPadding, VerticalPadding,
                        Width - 2 * HorizontalPadding, Height - 2 * VerticalPadding), pixelFont);
                }
                Rectangle ink = InkBounds(layer, BackColor);
                if (ink.IsEmpty) return;
                int x = (Width - ink.Width) / 2, y = (Height - ink.Height) / 2;
                e.Graphics.DrawImage(layer, new Rectangle(x, y, ink.Width, ink.Height), ink, GraphicsUnit.Pixel);
            }
        }
        internal static Rectangle InkBounds(Bitmap bitmap, Color background) {
            int left = bitmap.Width, right = -1, top = bitmap.Height, bottom = -1;
            int color = background.ToArgb();
            for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++) {
                if (bitmap.GetPixel(x, y).ToArgb() == color) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                top = Math.Min(top, y); bottom = Math.Max(bottom, y);
            }
            return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }
        private void PaintText(Graphics graphics, Rectangle textBounds, Font font) {
            int arrow = Text.IndexOf(" → ", StringComparison.Ordinal);
            if (arrow > 0 && Text.IndexOf('\n') < 0 &&
                TextRenderer.MeasureText(graphics, Text, font).Width <= textBounds.Width) {
                string[] parts = { Text.Substring(0, arrow), " → ", Text.Substring(arrow + 3) };
                Color[] colors = { MutedColor, AccentColor, ForeColor };
                int totalWidth = 0, textHeight = 0;
                foreach (string part in parts) {
                    Size partSize = TextRenderer.MeasureText(graphics, part, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    totalWidth += partSize.Width; textHeight = Math.Max(textHeight, partSize.Height);
                }
                int x = textBounds.Left + (textBounds.Width - totalWidth) / 2;
                int y = textBounds.Top + (textBounds.Height - textHeight) / 2;
                for (int i = 0; i < parts.Length; i++) {
                    Size measured = TextRenderer.MeasureText(graphics, parts[i], font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    TextRenderer.DrawText(graphics, parts[i], font, new Point(x, y), colors[i],
                        TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    x += measured.Width;
                }
            } else {
                Size measured = TextRenderer.MeasureText(graphics, Text, font, textBounds.Size,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                textBounds.Y += Math.Max(0, (textBounds.Height - measured.Height) / 2);
                TextRenderer.DrawText(graphics, Text, font, textBounds, ForeColor,
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter);
            }
        }
    }

    sealed class HintDropDown : ToolStripDropDown {
        protected override CreateParams CreateParams {
            get {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle = (parameters.ExStyle | 0x00000080 | 0x08000000) & ~0x00040000;
                return parameters;
            }
        }
    }

    sealed class SuggestionPopup : IDisposable {
        private readonly HintDropDown popup = new HintDropDown();
        private readonly PopupTextItem message = new PopupTextItem();
        private readonly Timer hideTimer = new Timer();
        private int popupDuration = 2500;
        internal int Generation { get; private set; }
        private Font hintFont;
        private int cornerRadius = 8;
        private bool currentDark, currentHighContrast;
        internal bool? ForceDarkForTest;
        internal bool DarkForTest { get { return currentDark; } }
        private void ApplyTheme() {
            bool dark = (Program.TestMode && ForceDarkForTest.HasValue ? ForceDarkForTest.Value : HintTheme.IsDark()) && !SystemInformation.HighContrast;
            if (hintFont != null && dark == currentDark && currentHighContrast == SystemInformation.HighContrast) return;
            currentDark = dark; currentHighContrast = SystemInformation.HighContrast;
            Font previous = hintFont;
            hintFont = new Font(SystemFonts.MessageBoxFont.FontFamily, Math.Max(9F, SystemFonts.MessageBoxFont.Size));
            message.Font = hintFont;
            if (previous != null) previous.Dispose();
            UiPalette palette = new UiPalette(dark);
            popup.BackColor = palette.Back;
            message.BackColor = popup.BackColor;
            message.ForeColor = palette.Fore;
            message.MutedColor = palette.Muted;
            message.AccentColor = palette.Accent;
            cornerRadius = SystemInformation.HighContrast ? 0 : 8;
            popup.Renderer = new HintSurfaceRenderer(popup.BackColor, palette.Border, cornerRadius);
        }
        private void UpdateShape() {
            Region previous = popup.Region;
            using (System.Drawing.Drawing2D.GraphicsPath path = HintSurfaceRenderer.Shape(
                new RectangleF(0, 0, popup.Width, popup.Height), cornerRadius)) popup.Region = new Region(path);
            if (previous != null) previous.Dispose();
        }
        internal void SavePreview(string path) {
            using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height)) {
                popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        public bool Visible { get { return popup.Visible; } }
        internal bool ActuallyVisibleForTest { get { return Native.IsWindowVisible(popup.Handle); } }
        internal bool NoTaskbarButtonForTest {
            get {
                long style = Native.GetWindowLongPtr(popup.Handle, -20).ToInt64();
                return (style & 0x00000080) != 0 && (style & 0x00040000) == 0;
            }
        }
        internal Rectangle BoundsForTest { get { return popup.Bounds; } }
        internal bool RoundedForTest { get { return popup.Region != null && !popup.Region.IsVisible(0, 0) && popup.Region.IsVisible(8, 0); } }
        internal bool InkCenteredForTest {
            get {
                using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height)) {
                    popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    int left = bitmap.Width, right = -1;
                    Color muted = message.MutedColor, accent = message.AccentColor;
                    for (int y = 4; y < bitmap.Height - 4; y++) for (int x = 4; x < bitmap.Width - 4; x++) {
                        int color = bitmap.GetPixel(x, y).ToArgb();
                        if (color == message.ForeColor.ToArgb() || color == muted.ToArgb() || color == accent.ToArgb()) {
                            left = Math.Min(left, x); right = Math.Max(right, x);
                        }
                    }
                    if (Program.TestMode) Console.WriteLine("hint ink margins=" + left + "/" + (bitmap.Width - 1 - right));
                    return right >= left && Math.Abs(left - (bitmap.Width - 1 - right)) <= 2;
                }
            }
        }
        internal bool FitsForTest {
            get {
                if (!Visible) return false;
                Size textSize = TextRenderer.MeasureText(message.Text, message.Font,
                    new Size(message.Bounds.Width - 2 * message.HorizontalPadding - 2, 10000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int verticalSlack = message.Bounds.Height - 2 * message.VerticalPadding - 2 - textSize.Height;
                bool fits = verticalSlack >= 0 && verticalSlack <= 8 &&
                    message.Bounds.Right <= popup.ClientSize.Width && message.Bounds.Bottom <= popup.ClientSize.Height;
                fits = fits && Math.Abs(message.Bounds.Left - (popup.ClientSize.Width - message.Bounds.Right)) <= 1 &&
                    Math.Abs(message.Bounds.Top - (popup.ClientSize.Height - message.Bounds.Bottom)) <= 1;
                if (Program.TestMode && !fits) Console.WriteLine("popup size=" + popup.ClientSize + " item=" + message.Bounds + " text=" + textSize);
                return fits;
            }
        }

        public SuggestionPopup() {
            popup.AutoClose = false;
            popup.AccessibleRole = AccessibleRole.ToolTip;
            popup.AutoSize = false;
            popup.Padding = new Padding(2);
            message.AutoSize = true;
            message.Margin = Padding.Empty;
            message.Padding = Padding.Empty;
            popup.Items.Add(message);
            ApplyTheme();
            popup.MouseEnter += delegate { hideTimer.Stop(); };
            popup.MouseLeave += delegate { hideTimer.Stop(); hideTimer.Interval = popupDuration; hideTimer.Start(); };
            hideTimer.Tick += delegate { Dismiss(); };
        }

        public void ShowAt(Point anchor, Rectangle? fieldBounds, string content, int durationMs) {
            popupDuration = durationMs;
            ApplyTheme();
            Rectangle area = Screen.FromPoint(anchor).WorkingArea;
            int maxTextWidth = Math.Max(80, Math.Min(440, area.Width - 2 * message.HorizontalPadding - 8));
            Size measured = TextRenderer.MeasureText(content, message.Font, new Size(maxTextWidth, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int width = Math.Min(area.Width, Math.Max(70, measured.Width + 2 * message.HorizontalPadding + 8));
            Size wrapped = TextRenderer.MeasureText(content, message.Font,
                new Size(width - 2 * message.HorizontalPadding - 2, 10000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            int height = Math.Min(area.Height, Math.Max(20, wrapped.Height + 2 * message.VerticalPadding + 2));
            int windowWidth = Math.Min(area.Width, width + 4);
            int windowHeight = Math.Min(area.Height, height + 4);
            message.SetContent(content, new Size(windowWidth - 4, windowHeight - 4));
            popup.Size = new Size(windowWidth, windowHeight);
            popup.PerformLayout();
            UpdateShape();
            int x = Math.Max(area.Left, Math.Min(anchor.X - windowWidth / 2, area.Right - windowWidth));
            int y = anchor.Y - windowHeight - 8;
            if (y < area.Top) y = anchor.Y + 20;
            if (Visible) popup.Close();
            popup.Show(new Point(x, Math.Max(area.Top, Math.Min(y, area.Bottom - windowHeight))));
            Native.ShowWindow(popup.Handle, 4); // SW_SHOWNOACTIVATE, including the first hint after a hidden tray launch.
            hideTimer.Stop();
            hideTimer.Interval = durationMs;
            hideTimer.Start();
        }

        public void Dismiss() { Generation++; hideTimer.Stop(); if (Visible) popup.Close(); }
        public void Dispose() { Dismiss(); hideTimer.Dispose(); popup.Dispose(); if (hintFont != null) hintFont.Dispose(); }
    }

    sealed class TrayApp : ApplicationContext {
        private readonly WindowsSpeller speller;
        private readonly ITDictionary itDictionary = ITDictionary.Load();
        private ITDictionaryWindow dictionaryWindow;
        private readonly NotifyIcon tray;
        private readonly TrayMenu trayMenu;
        internal TrayMenu MenuForTest { get { return trayMenu; } }
        private readonly SuggestionPopup suggestionPopup = new SuggestionPopup();
        private readonly ToolStripMenuItem autoItem, typoItem, spellingItem, codeItem, copilotItem, fnItem, startupItem, excludeItem, customWordItem;
        private readonly StartupRegistration startup = new StartupRegistration(
            Program.TestMode ? Path.Combine(Path.GetTempPath(), "Rasklad-menu-test-" + Guid.NewGuid().ToString("N") + ".lnk") :
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Rasklad.lnk"), Application.ExecutablePath);
        private readonly Native.HookProc keyboardCallback, mouseCallback;
        private readonly Control dispatcher;
        private readonly List<KeyStroke> word = new List<KeyStroke>();
        private readonly StringBuilder recentTyped = new StringBuilder();
        private readonly List<Native.Input> queuedKeys = new List<Native.Input>();
        private readonly HashSet<string> excludedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> customWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly ReplacementRules replacementRules = new ReplacementRules();
        private readonly string replacementRulesPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rasklad-replacements.txt");
        private readonly UndoLearning undoLearning = new UndoLearning();
        private readonly string exclusionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rasklad-exclusions.txt");
        private readonly string customWordsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rasklad-words.txt");
        private readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rasklad-settings.txt");
        private readonly string selfAppName = Process.GetCurrentProcess().ProcessName;
        private PendingWord pending;
        private PendingWord recent, manual;
        private PendingWord ambiguousPrevious;
        private bool? phraseRussian;
        private UndoRecord undo;
        private IntPtr keyboardHook, mouseHook, foreground, russianLayout, englishLayout;
        private string activeApp, lastApp;
        private string lastUnknownWord;
        private bool lastUnknownRussian;
        private int wordLanguage;
        private int suppressedKeyUp = -1;
        private bool capsActive, capsKeyDown;
        private int editingRevision;
        private bool autoEnabled = true, typoEnabled = true, spellingEnabled = true, codeProtectionEnabled = true, undoPending;
        private bool copilotEnabled, copilotChord;
        private int correctionKey = Native.VK_PAUSE;
        private bool correctionKeyDown;
        private bool modifierTapDown, modifierTapArmed;
        private IntPtr modifierTapWindow, modifierTapFocus;
        private int modifierTapGeneration;
        private uint altGrControlTime;
        private bool altGrControlCandidate;
        private static readonly UIntPtr CopilotPauseMarker = new UIntPtr(0x52434F50);
        private bool fnEnabled, fnKeyDown, fnArmed;
        private IntPtr fnWindow, fnFocus;
        internal int FnDirectForTest, FnFallbackForTest;
        internal bool PopupVisibleForTest { get { return suggestionPopup.Visible; } }
        internal bool PopupFitsForTest { get { return suggestionPopup.FitsForTest; } }
        internal bool PopupNoTaskbarForTest { get { return suggestionPopup.NoTaskbarButtonForTest; } }
        internal Rectangle PopupBoundsForTest { get { return suggestionPopup.BoundsForTest; } }
        internal bool ForceAutomationSelectionForTest;
        internal bool BrandingForTest { get { return tray.Text == AppBranding.Name && tray.Icon == AppBranding.TrayIcon; } }

        public TrayApp() {
            speller = new WindowsSpeller();
            capsActive = (Native.GetKeyState(20) & 1) != 0;
            russianLayout = Native.LoadKeyboardLayout("00000419", 0);
            englishLayout = Native.LoadKeyboardLayout("00000409", 0);
            if (russianLayout == IntPtr.Zero || englishLayout == IntPtr.Zero)
                throw new InvalidOperationException(UiText.Get("Не удалось загрузить русскую и английскую раскладки.", "Could not load the Russian and English keyboard layouts."));
            if (!Program.TestMode && File.Exists(exclusionsPath))
                foreach (string line in File.ReadAllLines(exclusionsPath)) {
                    string name = line.Trim();
                    if (name.Length > 0 && !name.StartsWith("#")) excludedApps.Add(name);
                }
            if (!Program.TestMode) customWords.UnionWith(LoadCustomWords(customWordsPath));
            if (!Program.TestMode) replacementRules.Load(replacementRulesPath);
            if (!Program.TestMode) LoadSettings();

            TrayMenu menu = new TrayMenu(); trayMenu = menu;
            autoItem = new ToolStripMenuItem(UiText.Get("Автопереключение раскладки", "Automatic layout switching"), null, delegate {
                autoEnabled = !autoEnabled; autoItem.Checked = autoEnabled; word.Clear(); SaveSettings(); });
            autoItem.Checked = autoEnabled;
            typoItem = new ToolStripMenuItem(UiText.Get("Исправлять опечатки", "Correct typos"), null, delegate {
                typoEnabled = !typoEnabled; typoItem.Checked = typoEnabled; word.Clear(); SaveSettings(); });
            typoItem.Checked = typoEnabled;
            spellingItem = new ToolStripMenuItem(UiText.Get("Всплывающие подсказки", "Pop-up hints"), null, delegate {
                spellingEnabled = !spellingEnabled; spellingItem.Checked = spellingEnabled;
                if (!spellingEnabled) suggestionPopup.Dismiss(); SaveSettings(); });
            spellingItem.Checked = spellingEnabled;
            codeItem = new ToolStripMenuItem(UiText.Get("Не править код и терминалы", "Leave code and terminals unchanged"), null, delegate {
                codeProtectionEnabled = !codeProtectionEnabled; codeItem.Checked = codeProtectionEnabled; SaveSettings(); });
            codeItem.Checked = codeProtectionEnabled;
            copilotItem = new ToolStripMenuItem(UiText.Get("Использовать клавишу Copilot", "Use the Copilot key"), null, delegate {
                copilotEnabled = !copilotEnabled; copilotItem.Checked = copilotEnabled; copilotChord = false; SaveSettings(); });
            copilotItem.Checked = copilotEnabled;
            fnItem = new ToolStripMenuItem(UiText.Get("Fn — переключать RU / EN", "Fn — switch RU / EN"), null, delegate {
                fnEnabled = !fnEnabled; fnItem.Checked = fnEnabled; fnArmed = fnKeyDown = false; SaveSettings(); });
            fnItem.Checked = fnEnabled;
            excludeItem = new ToolStripMenuItem(UiText.Get("Исключить последнее приложение ввода", "Exclude the last typing application"), null, delegate { ToggleExcludedApp(); });
            customWordItem = new ToolStripMenuItem(UiText.Get("Добавить последнее незнакомое слово", "Add the last unrecognized word"));
            customWordItem.DropDown = new TrayMenu();
            menu.AddHeader(UiText.Get("Правка", "Corrections"));
            menu.Items.Add(autoItem);
            menu.Items.Add(typoItem);
            menu.Items.Add(spellingItem);
            menu.Items.Add(codeItem);
            menu.Items.Add(excludeItem);
            menu.AddHeader(UiText.Get("Клавиши", "Keys"));
            menu.Items.Add(copilotItem);
            menu.Items.Add(fnItem);
            ToolStripMenuItem correctionItem = new ToolStripMenuItem();
            correctionItem.DropDown = new TrayMenu();
            Action refreshCorrectionItem = delegate {
                correctionItem.Text = UiText.Get("Клавиша исправления: ", "Correction key: ") + CorrectionKeyName(correctionKey);
                foreach (ToolStripMenuItem option in correctionItem.DropDownItems) option.Checked = (int)option.Tag == correctionKey;
            };
            foreach (int code in new[] { 19, 164, 165, 162, 163 }) {
                ToolStripMenuItem option = new ToolStripMenuItem(CorrectionKeyName(code)) { Tag = code };
                option.Click += delegate(object sender, EventArgs e) {
                    correctionKey = (int)((ToolStripMenuItem)sender).Tag;
                    correctionKeyDown = modifierTapDown = modifierTapArmed = false; refreshCorrectionItem(); SaveSettings();
                };
                correctionItem.DropDownItems.Add(option);
            }
            refreshCorrectionItem(); menu.Items.Add(correctionItem);
            menu.AddHeader(UiText.Get("Слова", "Words"));
            menu.Items.Add(customWordItem);
            menu.Items.Add(new ToolStripMenuItem(UiText.Get("IT-словарь: найти термин…", "IT dictionary: find a term…"), null, delegate {
                if (dictionaryWindow == null || dictionaryWindow.IsDisposed) dictionaryWindow = new ITDictionaryWindow(itDictionary);
                dictionaryWindow.Show(); dictionaryWindow.Activate();
                dictionaryWindow.SearchFor(lastUnknownWord);
            }));
            menu.Opening += delegate { excludeItem.Enabled = !String.IsNullOrEmpty(lastApp);
                excludeItem.Text = lastApp == null ? UiText.Get("Исключить последнее приложение ввода", "Exclude the last typing application") : UiText.Get("Не править в «", "Do not correct in “") + lastApp + UiText.Get("»", "”");
                excludeItem.Checked = lastApp != null && excludedApps.Contains(lastApp);
                RefreshCustomWordMenu(); };
            menu.Items.Add(new ToolStripSeparator());
            startupItem = new ToolStripMenuItem(UiText.Get("Запускать при входе в Windows", "Start when signing in to Windows"), null, delegate {
                try { startup.SetEnabled(!startup.Enabled); }
                catch (Exception ex) {
                    DiagnosticLog.Write("error.startup_registration");
                    MessageBox.Show(UiText.Get("Не удалось изменить автозагрузку: ", "Could not change startup settings: ") + ex.Message, AppBranding.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                RefreshStartupItem();
            });
            menu.Items.Add(startupItem);
            menu.Opening += delegate { RefreshStartupItem(); };
            menu.Items.Add(new ToolStripMenuItem(UiText.Get("Журнал диагностики", "Diagnostic log"), null, delegate {
                try {
                    if (!File.Exists(DiagnosticLog.PathName)) File.WriteAllText(DiagnosticLog.PathName, "");
                    Process.Start("notepad.exe", "\"" + DiagnosticLog.PathName + "\"");
                } catch (Exception) { tray.Text = UiText.Get("Не удалось открыть журнал диагностики", "Could not open the diagnostic log"); }
            }));
            menu.Items.Add(new ToolStripMenuItem(AppBranding.Name + " " + AppBranding.Version) { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem(UiText.Get("Выход", "Exit"), null, delegate { ExitThread(); }));
            tray = new NotifyIcon();
            tray.Icon = AppBranding.TrayIcon;
            tray.Text = AppBranding.Name;
            tray.ContextMenuStrip = menu;
            tray.Visible = true;
            dispatcher = new Control();
            IntPtr unused = dispatcher.Handle;

            keyboardCallback = KeyboardHook;
            mouseCallback = MouseHook;
            IntPtr module = Native.GetModuleHandle(null);
            keyboardHook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, keyboardCallback, module, 0);
            mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, mouseCallback, module, 0);
            if (keyboardHook == IntPtr.Zero || mouseHook == IntPtr.Zero)
                throw new InvalidOperationException(UiText.Get("Не удалось установить системный обработчик клавиатуры и мыши.", "Could not install the keyboard and mouse hooks."));
            DiagnosticLog.Write("runtime.started");
        }

        private static string ApplicationName(IntPtr window) {
            uint processId;
            if (Native.GetWindowThreadProcessId(window, out processId) == 0 || processId == 0) return null;
            try { return Process.GetProcessById((int)processId).ProcessName; }
            catch (ArgumentException) { return null; }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }
        private void RefreshStartupItem() {
            try { startupItem.Checked = startup.Enabled; startupItem.Enabled = true; }
            catch (Exception) { startupItem.Checked = false; startupItem.Enabled = false; DiagnosticLog.Write("error.startup_status"); }
        }

        private static readonly HashSet<string> codeApps = new HashSet<string>(new[] {
            "Code", "Code - Insiders", "Cursor", "Windsurf", "sublime_text", "notepad++", "devenv",
            "rider64", "pycharm64", "idea64", "webstorm64", "studio64", "WindowsTerminal",
            "OpenConsole", "wt", "powershell", "pwsh", "cmd", "mintty", "wezterm-gui",
            "alacritty", "kitty", "Tabby", "Hyper"
        }, StringComparer.OrdinalIgnoreCase);

        private static bool IsCodeApp(string name) { return name != null && codeApps.Contains(name); }
        internal static bool IsCodeAppForTest(string name) { return IsCodeApp(name); }

        private void ToggleExcludedApp() {
            if (String.IsNullOrEmpty(lastApp)) return;
            bool wasExcluded = excludedApps.Contains(lastApp);
            if (wasExcluded) excludedApps.Remove(lastApp); else excludedApps.Add(lastApp);
            try {
                if (!Program.TestMode) {
                    string[] names = new string[excludedApps.Count];
                    excludedApps.CopyTo(names);
                    Array.Sort(names, StringComparer.OrdinalIgnoreCase);
                    File.WriteAllLines(exclusionsPath, names, Encoding.UTF8);
                }
                word.Clear(); undo = null;
                excludeItem.Checked = !wasExcluded;
            } catch (Exception ex) {
                if (wasExcluded) excludedApps.Add(lastApp); else excludedApps.Remove(lastApp);
                tray.Text = UiText.Get("Ошибка сохранения исключения: ", "Exclusion save error: ") + ex.GetType().Name;
            }
        }

        private static string CustomKey(string wordText, bool russianLanguage) {
            return (russianLanguage ? "ru\t" : "en\t") + wordText.ToLowerInvariant();
        }

        private static bool IsWordJoiner(char c) { return c == '-' || c == '\'' || c == '\u2019'; }

        private static bool ValidCustomWord(string wordText, bool russianLanguage) {
            if (String.IsNullOrEmpty(wordText) || wordText.Length < 2 || wordText.Length > 64) return false;
            for (int i = 0; i < wordText.Length; i++) {
                char c = wordText[i];
                if (russianLanguage ? IsRussian(c) : IsEnglish(c)) continue;
                if (!IsWordJoiner(c) || i == 0 || i == wordText.Length - 1 ||
                    !Char.IsLetter(wordText[i - 1]) || !Char.IsLetter(wordText[i + 1])) return false;
            }
            return true;
        }

        private static HashSet<string> LoadCustomWords(string path) {
            HashSet<string> words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return words;
            foreach (string line in File.ReadAllLines(path, Encoding.UTF8)) {
                string[] parts = line.Trim().Split('\t');
                if (parts.Length == 2 && (parts[0] == "ru" || parts[0] == "en") &&
                    ValidCustomWord(parts[1], parts[0] == "ru"))
                    words.Add(CustomKey(parts[1], parts[0] == "ru"));
            }
            return words;
        }

        private static void SaveCustomWords(string path, HashSet<string> words) {
            string[] lines = new string[words.Count];
            words.CopyTo(lines);
            Array.Sort(lines, StringComparer.OrdinalIgnoreCase);
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }

        internal static bool ValidCorrectionKey(int value) { return value == 19 || value >= 162 && value <= 165; }
        private static string CorrectionKeyName(int value) {
            switch (value) {
                case 164: return UiText.Get("Левый Alt", "Left Alt");
                case 165: return UiText.Get("Правый Alt", "Right Alt");
                case 162: return UiText.Get("Левый Ctrl", "Left Ctrl");
                case 163: return UiText.Get("Правый Ctrl", "Right Ctrl");
                default: return "Copilot / Break";
            }
        }

        private void LoadSettings() {
            if (!File.Exists(settingsPath)) return;
            foreach (string line in File.ReadAllLines(settingsPath, Encoding.UTF8)) {
                if (line == "auto=0") autoEnabled = false;
                if (line == "typos=0") typoEnabled = false;
                if (line == "hints=0") spellingEnabled = false;
                if (line == "protectCode=0") codeProtectionEnabled = false;
                if (line == "copilot=1") copilotEnabled = true;
                if (line == "fn=1") fnEnabled = true;
                if (line.StartsWith("correctionKey=", StringComparison.Ordinal)) {
                    int value;
                    if (Int32.TryParse(line.Substring(14), out value) && ValidCorrectionKey(value)) correctionKey = value;
                }
            }
        }

        private void SaveSettings() {
            if (Program.TestMode) return;
            try {
                File.WriteAllLines(settingsPath, new[] {
                    "auto=" + (autoEnabled ? "1" : "0"),
                    "typos=" + (typoEnabled ? "1" : "0"),
                    "hints=" + (spellingEnabled ? "1" : "0"),
                    "protectCode=" + (codeProtectionEnabled ? "1" : "0"),
                    "copilot=" + (copilotEnabled ? "1" : "0"),
                    "fn=" + (fnEnabled ? "1" : "0"),
                    "correctionKey=" + correctionKey.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }, Encoding.UTF8);
            } catch (Exception ex) {
                tray.Text = UiText.Get("Ошибка сохранения настроек: ", "Could not save settings: ") + ex.GetType().Name;
            }
        }

        private void RefreshCustomWordMenu() {
            // Dispose old entries as this menu is rebuilt for every opening.
            while (customWordItem.DropDownItems.Count > 0) customWordItem.DropDownItems[0].Dispose();
            string original = lastUnknownWord;
            bool russian = lastUnknownRussian;
            customWordItem.Enabled = ValidCustomWord(original, russian);
            customWordItem.Text = !customWordItem.Enabled ? UiText.Get("Добавить последнее незнакомое слово", "Add the last unrecognized word") :
                UiText.Get("Считать правильным: ", "Accept spelling: ") + original;
            if (!customWordItem.Enabled) return;
            HashSet<string> offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string, bool> add = delegate(string text, bool language) {
                if (!ValidCustomWord(text, language) || !offered.Add(CustomKey(text, language))) return;
                ToolStripMenuItem option = new ToolStripMenuItem(text + (language ? " (RU)" : " (EN)"));
                option.Checked = customWords.Contains(CustomKey(text, language));
                option.Click += delegate { ToggleCustomWord(text, language, option); };
                customWordItem.DropDownItems.Add(option);
            };
            bool convertedRussian;
            string converted = ConvertSelectedText(original, russian, out convertedRussian);
            add(original, russian);
            add(converted, convertedRussian);
            int directCount = customWordItem.DropDownItems.Count;
            try {
                List<string> suggestions = speller.TypoSuggestions(original, russian, 3);
                add(SelectTypoCorrection(original, russian, delegate(string candidate) { return IsKnown(candidate, russian); }, suggestions), russian);
                foreach (string suggestion in suggestions) add(suggestion, russian);
                if (ValidCustomWord(converted, convertedRussian))
                    foreach (string suggestion in speller.TypoSuggestions(converted, convertedRussian, 3)) add(suggestion, convertedRussian);
            } catch (Exception) { DiagnosticLog.Write("error.dictionary_menu_suggestions"); }
            if (customWordItem.DropDownItems.Count > directCount)
                customWordItem.DropDownItems.Insert(directCount, new ToolStripSeparator());
        }

        private void ToggleCustomWord(string text, bool russianLanguage, ToolStripMenuItem option) {
            if (!ValidCustomWord(text, russianLanguage)) return;
            string key = CustomKey(text, russianLanguage);
            bool wasAdded = customWords.Contains(key);
            if (wasAdded) customWords.Remove(key); else customWords.Add(key);
            try {
                if (!Program.TestMode) SaveCustomWords(customWordsPath, customWords);
                option.Checked = !wasAdded;
            } catch (Exception ex) {
                if (wasAdded) customWords.Add(key); else customWords.Remove(key);
                tray.Text = UiText.Get("Ошибка сохранения слова: ", "Could not save the word: ") + ex.GetType().Name;
            }
        }

        private bool IsKnown(string wordText, bool russianLanguage) {
            return (!russianLanguage && wordText.IndexOf('\'') >= 0 && commonEnglishShort.Contains(wordText)) ||
                customWords.Contains(CustomKey(wordText, russianLanguage)) ||
                itDictionary.IsWord(wordText, russianLanguage) || speller.IsCorrect(wordText, russianLanguage);
        }

        internal void SetCurrentAppExcludedForTest(bool excluded) {
            if (excluded) excludedApps.Add(selfAppName); else excludedApps.Remove(selfAppName);
        }
        internal void SetAutoEnabledForTest(bool enabled) { autoEnabled = enabled; }
        internal void ResetContextForTest() {
            ResetTypingContext(); word.Clear(); recent = null; undo = null; manual = null; suggestionPopup.Dismiss();
        }
        private void ResetPhrase() { phraseRussian = null; ambiguousPrevious = null; }
        private void ResetTypingContext() { ResetPhrase(); recentTyped.Clear(); }

        internal static bool InsideMarkdownCodeForTest(string text) {
            int blocks = 0, lastBlockEnd = 0;
            for (int i = 0; i + 2 < text.Length; i++)
                if (text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`') {
                    blocks++; lastBlockEnd = i + 3; i += 2;
                }
            if ((blocks & 1) != 0) return true;
            int start = Math.Max(text.LastIndexOf('\n') + 1, lastBlockEnd), ticks = 0;
            for (int i = start; i < text.Length; i++) if (text[i] == '`') ticks++;
            return (ticks & 1) != 0;
        }

        internal static bool InsideMarkdownCodeForTest(string field, string typed) {
            if (field == null) return InsideMarkdownCodeForTest(typed);
            // Rich editors may remove or temporarily expose only one delimiter.
            // A completed inline pair in the current typing stream closes that span.
            if (ClosedInlineCodeForTest(typed) && !OpenFenceForTest(field)) return false;
            if (InsideMarkdownCodeForTest(field)) return true;
            if (!InsideMarkdownCodeForTest(typed)) return false;
            int mark = typed.LastIndexOf('`');
            if (mark < 0) return false;
            string after = System.Text.RegularExpressions.Regex.Replace(typed.Substring(mark + 1), @"\s", "");
            string actual = System.Text.RegularExpressions.Regex.Replace(field, @"\s", "");
            return actual.Length != 0 && actual.EndsWith(after, StringComparison.Ordinal);
        }

        private static bool OpenFenceForTest(string text) {
            int count = 0;
            for (int i = 0; i + 2 < text.Length; i++)
                if (text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`') { count++; i += 2; }
            return (count & 1) != 0;
        }

        internal static bool ClosedInlineCodeForTest(string typed) {
            if (InsideMarkdownCodeForTest(typed)) return false;
            int last = typed.LastIndexOf('`'), line = typed.LastIndexOf('\n');
            if (last <= line || last > 0 && typed[last - 1] == '`') return false;
            int previous = typed.LastIndexOf('`', last - 1);
            return previous > line && (previous == 0 || typed[previous - 1] != '`');
        }

        internal static bool CodeMarkerForTest(string marker, bool wholeField) {
            marker = (marker ?? "").ToLowerInvariant();
            return marker.Contains("monaco") || marker.Contains("codemirror") || marker.Contains("cm-content") ||
                marker.Contains("cm-editor") || marker.Contains("ace_text") || marker.Contains("ace_editor") ||
                !wholeField && (marker.Contains("code") || marker == "pre" || marker == "код");
        }

        private static bool CaretInCode(AutomationElement node, AutomationElement field, out bool block) {
            bool code = false, foundBlock = false; block = false;
            for (int depth = 0; node != null && depth < 16; depth++) {
                if (Automation.Compare(node, field)) { block = foundBlock; return code; } // Exclude the field itself.
                if (node.Current.IsPassword || node.Current.ControlType == ControlType.Window) return false;
                string marker = (node.Current.ClassName + " " + node.Current.AutomationId).Trim().ToLowerInvariant();
                foundBlock |= marker.Contains("code-block") || marker.Contains("codeblock") || marker.Contains("code_block") || marker == "pre";
                code |= CodeMarkerForTest(marker, false) ||
                    CodeMarkerForTest(node.Current.LocalizedControlType, false);
                node = TreeWalker.RawViewWalker.GetParent(node);
            }
            return false;
        }

        private static void ReadCodeContext(AutomationElement focused, InputContext context) {
            AutomationElement ancestor = focused;
            for (int depth = 0; ancestor != null && depth < 8; depth++) {
                if (ancestor.Current.IsPassword) { context.Protected = true; return; }
                context.CodeField |= CodeMarkerForTest(ancestor.Current.ClassName + " " + ancestor.Current.AutomationId, true);
                if (ancestor.Current.ControlType == ControlType.Window) break;
                ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
            }
            object value = null;
            AutomationElement textField = focused;
            for (int depth = 0; textField != null && depth < 6; depth++) {
                if (textField.Current.IsPassword) { context.Protected = true; return; }
                if (textField.TryGetCurrentPattern(TextPattern.Pattern, out value)) break;
                if (textField.Current.ControlType == ControlType.Window) return;
                textField = TreeWalker.RawViewWalker.GetParent(textField);
            }
            if (value == null || textField == null) return;
            TextPattern pattern = (TextPattern)value;
            TextPatternRange[] selected = pattern.GetSelection();
            if (selected.Length != 1) return;
            TextPatternRange caret = selected[0].Clone();
            caret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            TextPatternRange fieldRange = Automation.Compare(textField, focused) ? pattern.DocumentRange : pattern.RangeFromChild(focused);
            if (caret.CompareEndpoints(TextPatternRangeEndpoint.Start, fieldRange, TextPatternRangeEndpoint.Start) >= 0 &&
                caret.CompareEndpoints(TextPatternRangeEndpoint.End, fieldRange, TextPatternRangeEndpoint.End) <= 0) {
                TextPatternRange before = caret.Clone();
                before.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -4000);
                // Chromium can move a range beyond its editable container into the chat transcript.
                if (before.CompareEndpoints(TextPatternRangeEndpoint.Start, fieldRange, TextPatternRangeEndpoint.Start) < 0)
                    before.MoveEndpointByRange(TextPatternRangeEndpoint.Start, fieldRange, TextPatternRangeEndpoint.Start);
                context.BeforeCaret = before.GetText(4000);
            }
            // Browser carets often have no Win32 caret. Resolve the UIA caret before hit-testing code.
            System.Windows.Rect[] caretBounds = caret.GetBoundingRectangles();
            if (caretBounds.Length > 0) {
                System.Windows.Rect bounds = caretBounds[0];
                if (!bounds.IsEmpty && !Double.IsNaN(bounds.Left) && !Double.IsInfinity(bounds.Left) && bounds.Height > 0)
                    context.CaretPoint = new Point((int)bounds.Left, (int)bounds.Top);
            }
            bool block;
            context.CaretCode |= CaretInCode(caret.GetEnclosingElement(), textField, out block);
            context.CaretCodeBlock |= block;
            if (!context.CaretCode) {
                context.CaretCode |= CodeCaret.AtElement(caret.GetEnclosingElement(), focused, out block);
                context.CaretCodeBlock |= block;
            }
            if (!context.CaretCode && context.CaretPoint.HasValue) {
                Point point = context.CaretPoint.Value;
                context.CaretCode |= CaretInCode(AutomationElement.FromPoint(new System.Windows.Point(point.X, point.Y + 2)), textField, out block);
                context.CaretCodeBlock |= block;
                if (!context.CaretCode) {
                    context.CaretCode |= CodeCaret.AtPoint(new Point(point.X + 1, point.Y + 2), focused, out block);
                    context.CaretCodeBlock |= block;
                }
            }
        }

        private void RememberTyped(char c) {
            if (c == '\0') return;
            recentTyped.Append(c);
            if (recentTyped.Length > 4000) recentTyped.Remove(0, recentTyped.Length - 4000);
        }
        internal static bool SpaceBeforePunctuationForTest(string text, char mark) {
            return !String.IsNullOrEmpty(text) && text.Length >= 2 && text[text.Length - 1] == ' ' &&
                Char.IsLetterOrDigit(text[text.Length - 2]) && ",.!?:;)»”".IndexOf(mark) >= 0;
        }
        internal void AddCustomWordForTest(string wordText, bool russianLanguage) {
            customWords.Add(CustomKey(wordText, russianLanguage));
        }

        internal bool HasCustomWordForTest(string text, bool russianLanguage) { return customWords.Contains(CustomKey(text, russianLanguage)); }
        internal void ResetLearningForTest() { undoLearning.Clear(); RemoveCustomWordForTest("helllo", false); }
        internal void RemoveCustomWordForTest(string wordText, bool russianLanguage) {
            customWords.Remove(CustomKey(wordText, russianLanguage));
        }
        internal static bool CustomWordsRoundTripForTest() {
            string path = Path.GetTempFileName();
            try {
                HashSet<string> sample = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                sample.Add(CustomKey("ghbdtn", false));
                sample.Add(CustomKey("приветик", true));
                sample.Add(CustomKey("кто-нибудь", true));
                sample.Add(CustomKey("well-known", false));
                sample.Add(CustomKey("can't", false));
                sample.Add(CustomKey("can’t", false));
                SaveCustomWords(path, sample);
                HashSet<string> loaded = LoadCustomWords(path);
                return loaded.SetEquals(sample) && !ValidCustomWord("-hello", false) && !ValidCustomWord("hello-", false) &&
                    !ValidCustomWord("foo--bar", false) && !ValidCustomWord("can''t", false) && !ValidCustomWord("hello\tworld", false);
            } finally { File.Delete(path); }
        }

        private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam) {
            if (code >= 0) {
                int message = wParam.ToInt32();
                if (message == Native.WM_LBUTTONDOWN || message == Native.WM_RBUTTONDOWN || message == Native.WM_MBUTTONDOWN) {
                    if (Program.TestMode && undo != null) Console.WriteLine("undo cleared by mouse");
                    editingRevision++;
                    word.Clear(); recent = null; undo = null; ResetTypingContext(); suggestionPopup.Dismiss();
                }
            }
            return Native.CallNextHookEx(mouseHook, code, wParam, lParam);
        }

        private static Native.Input ReplayKey(Native.KbdData key, bool up) {
            Native.Input input = new Native.Input(); input.type = 1;
            input.data.key.vk = (ushort)key.vkCode;
            input.data.key.scan = (ushort)key.scanCode;
            input.data.key.flags = (up ? Native.KEYEVENTF_KEYUP : 0) | ((key.flags & 1) != 0 ? 1u : 0u);
            input.data.key.extraInfo = key.extraInfo == CopilotPauseMarker ? CopilotPauseMarker : Native.ReplayInputMarker;
            return input;
        }

        private static bool SendKeys(List<Native.Input> keys) {
            bool sent = Native.SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(Native.Input))) == keys.Count;
            if (!sent) DiagnosticLog.Write("input.send_incomplete");
            return sent;
        }

        private static bool AtMostOneEdit(string first, string second) {
            if (first == null || second == null || Math.Abs(first.Length - second.Length) > 1) return false;
            int i = 0, j = 0, edits = 0;
            while (i < first.Length && j < second.Length) {
                if (first[i] == second[j]) { i++; j++; continue; }
                if (++edits > 1) return false;
                if (first.Length >= second.Length) i++;
                if (second.Length >= first.Length) j++;
            }
            return edits + (first.Length - i) + (second.Length - j) <= 1;
        }

        private static bool AtMostOneTypo(string first, string second) {
            if (AtMostOneEdit(first, second)) return true;
            if (first == null || second == null || first.Length != second.Length) return false;
            int mismatch = 0;
            while (mismatch < first.Length && first[mismatch] == second[mismatch]) mismatch++;
            return mismatch + 1 < first.Length && first[mismatch] == second[mismatch + 1] &&
                first[mismatch + 1] == second[mismatch] &&
                first.Substring(mismatch + 2) == second.Substring(mismatch + 2);
        }

        private static int EditDistance(string first, string second) {
            int[,] distance = new int[first.Length + 1, second.Length + 1];
            for (int i = 0; i <= first.Length; i++) distance[i, 0] = i;
            for (int j = 0; j <= second.Length; j++) distance[0, j] = j;
            for (int i = 1; i <= first.Length; i++)
                for (int j = 1; j <= second.Length; j++) {
                    distance[i, j] = Math.Min(Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
                    if (i > 1 && j > 1 && first[i - 1] == second[j - 2] && first[i - 2] == second[j - 1])
                        distance[i, j] = Math.Min(distance[i, j], distance[i - 2, j - 2] + 1);
                }
            return distance[first.Length, second.Length];
        }

        internal static string TypoCore(string text) {
            if (String.IsNullOrEmpty(text)) return "";
            int first = 0, last = text.Length - 1;
            while (first <= last && !Char.IsLetter(text[first])) first++;
            while (last >= first && !Char.IsLetter(text[last])) last--;
            return first <= last ? text.Substring(first, last - first + 1) : "";
        }

        private static bool ValidTypoCore(string text) {
            for (int i = 0; i < text.Length; i++)
                if (!Char.IsLetter(text[i]) && (!IsWordJoiner(text[i]) || i == 0 || i == text.Length - 1 ||
                    !Char.IsLetter(text[i - 1]) || !Char.IsLetter(text[i + 1]))) return false;
            return text.Length > 0;
        }

        internal static string TypoCorrection(string original, string suggestion) {
            string core = TypoCore(original), fixedCore = TypoCore(suggestion);
            if (core.Length < 4 || fixedCore.Length == 0 || core == core.ToUpperInvariant() ||
                !ValidTypoCore(core) || !ValidTypoCore(fixedCore)) return null;
            if (core.IndexOf('\u2019') >= 0 && core.IndexOf('\'') < 0) fixedCore = fixedCore.Replace('\'', '\u2019');
            int limit = core.Length >= 7 ? 2 : 1;
            if (core.Equals(fixedCore, StringComparison.OrdinalIgnoreCase) || Math.Abs(core.Length - fixedCore.Length) > limit ||
                EditDistance(core.ToLowerInvariant(), fixedCore.ToLowerInvariant()) > limit) return null;
            int start = original.IndexOf(core, StringComparison.Ordinal);
            return original.Substring(0, start) + MatchCase(core, fixedCore) + original.Substring(start + core.Length);
        }

        internal static string SelectTypoCorrection(string original, bool russian, Func<string, bool> isKnown, IList<string> suggestions) {
            string core = TypoCore(original), lower = core.ToLowerInvariant();
            if (core.Length < 4 || !ValidTypoCore(core) || isKnown(lower)) return null;
            // This bound suffix is not suggested as a standalone word by Windows.
            if (russian && lower.EndsWith("-нибуть", StringComparison.Ordinal)) {
                string candidate = lower.Substring(0, lower.Length - 6) + "нибудь";
                if (isKnown(candidate)) return TypoCorrection(original, candidate);
            }
            // Windows often ranks a different inflection/root before the intended -чок.
            // Only try this ending for an unknown source and a dictionary-confirmed target.
            if (russian && ValidCustomWord(core, true) && lower.EndsWith("чек", StringComparison.Ordinal)) {
                string candidate = lower.Substring(0, lower.Length - 2) + "ок";
                if (isKnown(candidate)) {
                    string preferred = TypoCorrection(original, candidate);
                    if (preferred != null) return preferred;
                }
            }
            if (suggestions != null)
                foreach (string candidate in suggestions) {
                    if (String.IsNullOrEmpty(candidate) || !isKnown(TypoCore(candidate).ToLowerInvariant())) continue;
                    string corrected = TypoCorrection(original, candidate);
                    if (corrected != null) return corrected;
                }
            return null;
        }

        internal static string CaseCorrectionForTest(string original, Func<string, bool> isKnown) {
            if (String.IsNullOrEmpty(original) || original.Length < 3 ||
                original.EndsWith("OS", StringComparison.Ordinal) ||
                Array.IndexOf(new[] { "mRNA", "tRNA", "eID", "eSIM", "iMAC", "kWT" }, original) >= 0) return null;
            int firstUpper = -1;
            for (int i = 0; i < original.Length; i++) {
                if (!Char.IsLetter(original[i])) return null;
                if (Char.IsUpper(original[i])) { firstUpper = i; break; }
            }
            if (firstUpper < 1 || original.Length - firstUpper < 2) return null;
            for (int i = 0; i < firstUpper; i++) if (!Char.IsLower(original[i])) return null;
            for (int i = firstUpper; i < original.Length; i++) if (!Char.IsUpper(original[i])) return null;
            string corrected = firstUpper == 1
                ? Char.ToUpperInvariant(original[0]) + original.Substring(1).ToLowerInvariant()
                : original.ToLowerInvariant();
            return isKnown(corrected) ? corrected : null;
        }

        private static string MatchCase(string original, string suggestion) {
            if (original == original.ToUpperInvariant()) return suggestion.ToUpperInvariant();
            if (Char.IsUpper(original[0])) return Char.ToUpperInvariant(suggestion[0]) + suggestion.Substring(1);
            return suggestion;
        }

        private static void SendDelimiter(int vk, char delimiter) {
            List<Native.Input> keys = new List<Native.Input>();
            if (vk == Native.VK_SPACE || vk == Native.VK_RETURN || vk == Native.VK_TAB) {
                keys.Add(VirtualKey((ushort)vk, false)); keys.Add(VirtualKey((ushort)vk, true));
            } else { keys.Add(UnicodeKey(delimiter, false)); keys.Add(UnicodeKey(delimiter, true)); }
            SendKeys(keys);
        }

        private void TurnOffCapsLock() {
            if (capsActive && SendKeys(new List<Native.Input> { VirtualKey(20, false), VirtualKey(20, true) }))
                capsActive = false;
        }

        private static async Task<InputContext> GetInputContextAsync(bool readSelection = false) {
            IntPtr queryWindow = Native.GetForegroundWindow();
            IntPtr queryFocus = FocusedWindow(queryWindow);
            Point nativeCaret;
            Point? initialCaret = TryCaretPoint(queryWindow, out nativeCaret) ? nativeCaret : (Point?)null;
            Task<InputContext> check = Task.Run(() => {
                InputContext context = new InputContext { CaretPoint = initialCaret };
                try {
                    InputContext native;
                    if (TryNativeSelection(queryFocus, out native)) {
                        if (native.Protected) return native;
                        context.BeforeCaret = native.BeforeCaret;
                    }
                    AutomationElement focused = AutomationElement.FocusedElement;
                    if (focused == null) return context;
                    if (focused.Current.IsPassword) { context.SkipReason = "skip.password"; return context; }
                    context.Protected = false;
                    try { ReadCodeContext(focused, context); }
                    catch (Exception) { DiagnosticLog.Write("context.code_unavailable"); }
                    if (context.Protected) return context;
                    string className = focused.Current.ClassName ?? "";
                    string automationId = focused.Current.AutomationId ?? "";
                    string marker = className + " " + automationId;
                    context.CodeField |= marker.IndexOf("monaco", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        marker.IndexOf("codemirror", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        marker.IndexOf("ace_text", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        marker.IndexOf("ace_editor", StringComparison.OrdinalIgnoreCase) >= 0;
                    ControlType type = focused.Current.ControlType;
                    if (type == ControlType.Edit || type == ControlType.Document || type == ControlType.ComboBox) {
                        System.Windows.Rect bounds = focused.Current.BoundingRectangle;
                        if (!bounds.IsEmpty && bounds.Width >= 20 && bounds.Height >= 10 && bounds.Height <= 600 &&
                            !Double.IsNaN(bounds.Left) && !Double.IsNaN(bounds.Top))
                            context.FieldBounds = Rectangle.FromLTRB((int)bounds.Left, (int)bounds.Top,
                                (int)bounds.Right, (int)bounds.Bottom);
                    }
                    if (readSelection) {
                        AutomationElement element = focused;
                        for (int depth = 0; element != null && depth < 4; depth++) {
                            if (element.Current.IsPassword) return new InputContext();
                            object pattern;
                            if (element.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
                                TextPatternRange[] ranges = ((TextPattern)pattern).GetSelection();
                                if (ranges.Length > 1) return new InputContext();
                                if (ranges.Length == 1) {
                                    if (context.SelectionRange == null) context.SelectionRange = ranges[0].Clone();
                                    string selected = ranges[0].GetText(16385);
                                    if (selected.Length > 0) {
                                        if (selected.Length > 16384) return new InputContext();
                                        context.SelectionText = selected;
                                        break;
                                    }
                                    break;
                                }
                            }
                            if (element.Current.ControlType == ControlType.Window) break;
                            element = TreeWalker.ControlViewWalker.GetParent(element);
                        }
                    }
                    if (!context.CaretPoint.HasValue) {
                        try {
                            object pattern;
                            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out pattern)) {
                                TextPatternRange[] ranges = ((TextPattern)pattern).GetSelection();
                                if (ranges.Length == 1) {
                                    TextPatternRange range = ranges[0].Clone();
                                    if (range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) == 0) {
                                        if (range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -1) == 0)
                                            range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 1);
                                    }
                                    System.Windows.Rect[] rectangles = range.GetBoundingRectangles();
                                    if (rectangles.Length > 0) {
                                        System.Windows.Rect last = rectangles[rectangles.Length - 1];
                                        if (!last.IsEmpty && !Double.IsInfinity(last.Right) && !Double.IsNaN(last.Right)) {
                                            Rectangle bounds = Rectangle.FromLTRB((int)last.Left, (int)last.Top, (int)last.Right, (int)last.Bottom);
                                            if (HintPosition.Visible(bounds)) context.CaretPoint = new Point(bounds.Right, bounds.Top);
                                        }
                                    }
                                }
                            }
                        } catch (Exception) { DiagnosticLog.Write("hint.uia_caret_unavailable"); }
                        if (!context.CaretPoint.HasValue) context.CaretPoint = HintPosition.MsaaCaret(queryFocus);
                    }
                } catch (Exception) { return new InputContext(); }
                return context;
            });
            if (await Task.WhenAny(check, Task.Delay(readSelection ? 750 : 250)) != check)
                return new InputContext { SkipReason = "skip.context_timeout" };
            if (Native.GetForegroundWindow() != queryWindow || FocusedWindow(queryWindow) != queryFocus)
                return new InputContext { SkipReason = "skip.focus_changed" };
            return await check;
        }

        private bool IsProtectedCode(IntPtr window, InputContext context) {
            return codeProtectionEnabled && (IsCodeApp(ApplicationName(window)) || context.CodeField ||
                context.CaretCode ||
                InsideMarkdownCodeForTest(context.BeforeCaret, recentTyped.ToString()));
        }

        private async void ProcessPunctuation(IntPtr window, IntPtr focus, int revision, char mark, IntPtr layout) {
            bool delivered = false;
            try {
                InputContext context = await GetInputContextAsync(true);
                if (revision != editingRevision || Native.GetForegroundWindow() != window || FocusedWindow(window) != focus ||
                    !InputAccess.Allows(window)) return;
                bool allowed = typoEnabled && !context.Protected && !IsProtectedCode(window, context) &&
                    !excludedApps.Contains(ApplicationName(window) ?? "") && String.IsNullOrEmpty(context.SelectionText);
                int start = -1;
                InputContext native;
                if (allowed && TryNativeSelection(focus, out native)) {
                    string actual = native.Protected ? null : ReadNativeText(focus);
                    start = native.NativeSelectionStart;
                    allowed = !native.Protected && String.IsNullOrEmpty(native.SelectionText) && actual != null &&
                        start >= 2 && start <= actual.Length && SpaceBeforePunctuationForTest(actual.Substring(0, start), mark);
                }
                string replacement = mark.ToString();
                bool replaced = allowed && (start >= 0
                    ? ReplaceNativeRange(focus, start - 1, " ", replacement)
                    : ReplaceTypedWord(1, replacement, 0, '\0'));
                if (replaced) {
                    delivered = true;
                    if (recentTyped.Length >= 2 && recentTyped[recentTyped.Length - 2] == ' ')
                        recentTyped.Remove(recentTyped.Length - 2, 1);
                    undo = new UndoRecord { original = " " + mark, converted = replacement, punctuation = true,
                        foreground = window, focus = focus, sourceLayout = layout, selectionStart = start >= 0 ? start - 1 : -1,
                        fieldBounds = context.FieldBounds, caretPoint = context.CaretPoint };
                    DiagnosticLog.Write("punctuation.space_removed");
                    NotifyChange(" " + mark, replacement, true, context.FieldBounds, context.CaretPoint);
                } else delivered = ReplaceTypedWord(0, replacement, 0, '\0');
            } catch (Exception ex) {
                if (Program.TestMode) Console.Error.WriteLine(ex);
                DiagnosticLog.Write("error.punctuation");
                if (!delivered && revision == editingRevision && Native.GetForegroundWindow() == window && FocusedWindow(window) == focus)
                    ReplaceTypedWord(0, mark.ToString(), 0, '\0');
            } finally {
                word.Clear(); recent = null; ResetPhrase();
                List<Native.Input> replay = new List<Native.Input>(queuedKeys);
                queuedKeys.Clear(); undoPending = false;
                if (replay.Count > 0) {
                    foreach (Native.Input input in replay) if (ReplayEditsInput(input)) undo = null;
                    SendKeys(replay);
                }
            }
        }

        private async void ProcessPending() {
            PendingWord item = pending;
            if (item == null) return;
            bool delimiterSent = false;
            try {
                InputContext context = await GetInputContextAsync();
                if (Program.TestMode && context.Protected) Console.WriteLine("pending context: " + context.SkipReason);
                if (context.Protected) DiagnosticLog.Write(context.SkipReason);
                if (IsProtectedCode(item.foreground, context)) DiagnosticLog.Write("skip.code");
                if (Native.GetForegroundWindow() != item.foreground ||
                    (item.focus != IntPtr.Zero && FocusedWindow(item.foreground) != item.focus)) DiagnosticLog.Write("skip.focus_changed");
                if (!context.Protected && InputAccess.Allows(item.foreground) && !IsProtectedCode(item.foreground, context) &&
                    Native.GetForegroundWindow() == item.foreground &&
                    (item.focus == IntPtr.Zero || FocusedWindow(item.foreground) == item.focus)) {
                    ResolveDotEnding(item);
                    string prescribed;
                    if (typoEnabled && !customWords.Contains(CustomKey(item.sourceCheck, item.sourceRussian)) &&
                        replacementRules.TryGet(item.sourceCheck, item.sourceRussian, out prescribed)) {
                        string replacement = MatchCase(item.sourceCheck, prescribed) + item.suffix;
                        delimiterSent = ReplaceTypedWord(item.original.Length, replacement, item.delimiterVk, item.delimiter);
                        if (delimiterSent && item.delimiterVk != Native.VK_RETURN && item.delimiterVk != Native.VK_TAB) {
                            undo = new UndoRecord { original = item.original, converted = replacement,
                                delimiterVk = item.delimiterVk, delimiter = item.delimiter,
                                foreground = item.foreground, focus = item.focus, sourceLayout = item.originalLayout,
                                typo = true, sourceRussian = item.sourceRussian, fieldBounds = context.FieldBounds };
                            NotifyChange(item.original, replacement, true, context.FieldBounds, context.CaretPoint);
                        }
                        return;
                    }
                    PendingWord previous = ambiguousPrevious;
                    ambiguousPrevious = null;
                    bool shortLayoutWord = autoEnabled && IsShortLayoutWord(item) &&
                        !customWords.Contains(CustomKey(item.original, item.sourceRussian));
                    if (Program.TestMode) Console.WriteLine("capture=" + item.original + " check=" + item.sourceCheck +
                        " target=" + item.targetCheck + " suffix=" + item.suffix + " mixed=" + item.mixedLayout);
                    bool sourceCorrect = !shortLayoutWord && !item.mixedLayout &&
                        IsKnown(item.sourceCheck.ToLowerInvariant(), item.sourceRussian);
                    bool ambiguousWord = autoEnabled && sourceCorrect && item.targetCheck != null &&
                        item.sourceCheck.Length >= 3 && item.suffix.Length == 0 &&
                        IsKnown(item.targetCheck.ToLowerInvariant(), !item.sourceRussian);
                    if (ambiguousWord && phraseRussian.HasValue && phraseRussian.Value != item.sourceRussian)
                        sourceCorrect = false;
                    if (Program.TestMode) Console.WriteLine("sourceCorrect=" + sourceCorrect);
                    if (sourceCorrect && typoEnabled && !item.mixedLayout) {
                        string correctedCase = CaseCorrectionForTest(item.sourceCheck,
                            delegate(string candidate) { return IsKnown(candidate, item.sourceRussian); });
                        if (correctedCase != null) {
                            string replacement = correctedCase + item.suffix;
                            delimiterSent = ReplaceTypedWord(item.original.Length, replacement, item.delimiterVk, item.delimiter);
                            if (delimiterSent && item.delimiterVk != Native.VK_TAB && item.delimiterVk != Native.VK_RETURN)
                                undo = new UndoRecord { original = item.original, converted = replacement,
                                    delimiterVk = item.delimiterVk, delimiter = item.delimiter,
                                    foreground = item.foreground, focus = item.focus,
                                    sourceLayout = item.originalLayout, fieldBounds = context.FieldBounds, caretPoint = context.CaretPoint };
                            if (delimiterSent && undo != null) NotifyChange(item.original, replacement, true, context.FieldBounds, context.CaretPoint);
                            if (delimiterSent) TurnOffCapsLock();
                        }
                    }
                    if (sourceCorrect) {
                        phraseRussian = item.sourceRussian;
                        if (ambiguousWord && item.delimiterVk == Native.VK_SPACE) ambiguousPrevious = item;
                    }
                    if (!sourceCorrect) {
                        string unknown = TypoCore(item.sourceCheck);
                        if (!ambiguousWord && ValidCustomWord(unknown, item.sourceRussian)) {
                            lastUnknownWord = unknown;
                            lastUnknownRussian = item.sourceRussian;
                        }
                        bool targetCorrect = autoEnabled && item.converted != null &&
                            (shortLayoutWord || IsKnown(item.targetCheck.ToLowerInvariant(), !item.sourceRussian));
                        if (Program.TestMode) Console.WriteLine("targetCorrect=" + targetCorrect);
                        bool targetClose = false;
                        string targetSuggestion = null;
                        string sourceSuggestion = null;
                        if (autoEnabled && !targetCorrect && item.converted != null && item.targetCheck.Length >= 5) {
                            targetSuggestion = speller.FirstSuggestion(item.targetCheck.ToLowerInvariant(), !item.sourceRussian);
                            if (AtMostOneTypo(item.targetCheck.ToLowerInvariant(), targetSuggestion)) {
                                sourceSuggestion = item.mixedLayout ? null :
                                    speller.FirstSuggestion(item.sourceCheck.ToLowerInvariant(), item.sourceRussian);
                                targetClose = sourceSuggestion == null;
                            }
                        }
                        if (Program.TestMode) Console.WriteLine("targetClose=" + targetClose);
                        if (targetCorrect || targetClose) {
                            bool includePrevious = targetCorrect && previous != null &&
                                previous.foreground == item.foreground && previous.focus == item.focus &&
                                previous.sourceRussian == item.sourceRussian && previous.converted != null &&
                                previous.delimiterVk == Native.VK_SPACE && item.delimiterVk == Native.VK_SPACE;
                            string before = includePrevious ? previous.original + " " + item.original : item.original;
                            string targetWord = targetClose ? MatchCase(item.targetCheck, targetSuggestion) : item.targetCheck;
                            string correctedTargetCase = CaseCorrectionForTest(targetWord,
                                delegate(string candidate) { return IsKnown(candidate, !item.sourceRussian); });
                            if (correctedTargetCase != null) targetWord = correctedTargetCase;
                            string replacement = targetWord + item.suffix;
                            if (includePrevious) replacement = previous.converted + " " + replacement;
                            IntPtr ignored;
                            IntPtr targetLayout = item.sourceRussian ? englishLayout : russianLayout;
                            if (Native.SendMessageTimeout(item.foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero,
                                targetLayout, 2, 150, out ignored) == IntPtr.Zero)
                                Native.PostMessage(item.foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, targetLayout);
                            delimiterSent = ReplaceTypedWord(before.Length, replacement, item.delimiterVk, item.delimiter);
                            if (delimiterSent && item.delimiterVk != Native.VK_TAB && item.delimiterVk != Native.VK_RETURN)
                                undo = new UndoRecord { original = before, converted = replacement,
                                    delimiterVk = item.delimiterVk, delimiter = item.delimiter,
                                    foreground = item.foreground, focus = item.focus,
                                    sourceLayout = item.originalLayout, fieldBounds = context.FieldBounds, caretPoint = context.CaretPoint };
                            if (delimiterSent && undo != null) NotifyChange(before, replacement, true, context.FieldBounds, context.CaretPoint);
                            if (delimiterSent) phraseRussian = !item.sourceRussian;
                            if (delimiterSent && correctedTargetCase != null) TurnOffCapsLock();
                        } else {
                            if (typoEnabled && !item.mixedLayout && item.sourceCheck.Length >= 4) {
                                string corrected = SelectTypoCorrection(item.sourceCheck, item.sourceRussian,
                                    delegate(string candidate) { return IsKnown(candidate, item.sourceRussian); },
                                    speller.TypoSuggestions(TypoCore(item.sourceCheck).ToLowerInvariant(), item.sourceRussian, 8));
                                if (corrected != null) {
                                    string correctedCase = CaseCorrectionForTest(corrected,
                                        delegate(string candidate) { return IsKnown(candidate, item.sourceRussian); });
                                    string replacement = (correctedCase ?? corrected) + item.suffix;
                                    delimiterSent = ReplaceTypedWord(item.original.Length, replacement, item.delimiterVk, item.delimiter);
                                    if (delimiterSent && item.delimiterVk != Native.VK_TAB && item.delimiterVk != Native.VK_RETURN)
                                        undo = new UndoRecord { original = item.original, converted = replacement,
                                            delimiterVk = item.delimiterVk, delimiter = item.delimiter,
                                            foreground = item.foreground, focus = item.focus,
                                            sourceLayout = item.originalLayout, typo = true,
                                            sourceRussian = item.sourceRussian, fieldBounds = context.FieldBounds, caretPoint = context.CaretPoint };
                                    if (delimiterSent && undo != null) NotifyChange(item.original, replacement, true, context.FieldBounds, context.CaretPoint);
                                    if (delimiterSent) phraseRussian = item.sourceRussian;
                                    if (delimiterSent && correctedCase != null) TurnOffCapsLock();
                                }
                            }
                            if (!delimiterSent) phraseRussian = null;
                        }
                    }
                }
            } catch (Exception ex) {
                if (Program.TestMode) Console.WriteLine(ex);
                DiagnosticLog.Write("error.dictionary");
                tray.Text = UiText.Get("Ошибка словаря: ", "Dictionary error: ") + ex.GetType().Name;
            } finally {
                if (!delimiterSent && Native.GetForegroundWindow() == item.foreground &&
                    (item.focus == IntPtr.Zero || FocusedWindow(item.foreground) == item.focus))
                    SendDelimiter(item.delimiterVk, item.delimiter);
                if (!delimiterSent && item.delimiterVk != Native.VK_RETURN && item.delimiterVk != Native.VK_TAB &&
                    Native.GetForegroundWindow() == item.foreground &&
                    (item.focus == IntPtr.Zero || FocusedWindow(item.foreground) == item.focus)) recent = item;
                List<Native.Input> replay = new List<Native.Input>(queuedKeys);
                queuedKeys.Clear();
                pending = null;
                if (replay.Count > 0) {
                    foreach (Native.Input input in replay)
                        if (ReplayEditsInput(input)) { recent = null; undo = null; }
                    SendKeys(replay);
                }
                if (item.delimiterVk != Native.VK_SPACE) ResetPhrase();
                if (item.delimiterVk == Native.VK_TAB) recentTyped.Clear();
            }
        }

        private static UndoRecord ReverseCorrection(UndoRecord item, IntPtr layout) {
            return new UndoRecord { original = item.converted, converted = item.original,
                delimiterVk = item.delimiterVk, delimiter = item.delimiter,
                foreground = item.foreground, focus = item.focus, sourceLayout = layout,
                sourceRussian = unchecked((ushort)layout.ToInt64()) == 0x0419,
                selectionStart = item.selectionStart, fieldBounds = item.fieldBounds, caretPoint = item.caretPoint };
        }

        private void ProcessUndo() {
            UndoRecord item = undo;
            undo = null;
            IntPtr previousLayout = item == null ? IntPtr.Zero : Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(item.foreground, IntPtr.Zero));
            try {
                if (item != null && Native.GetForegroundWindow() == item.foreground &&
                    InputAccess.Allows(item.foreground) &&
                    (item.focus == IntPtr.Zero || FocusedWindow(item.foreground) == item.focus) &&
                    (item.selectionStart >= 0
                        ? ReplaceNativeRange(item.focus, item.selectionStart, item.converted, item.original)
                        : ReplaceTypedWord(item.converted.Length + (item.delimiterVk == 0 ? 0 : 1), item.original, item.delimiterVk, item.delimiter))) {
                    IntPtr ignored;
                    if (Native.SendMessageTimeout(item.foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero,
                        item.sourceLayout, 2, 150, out ignored) == IntPtr.Zero)
                        Native.PostMessage(item.foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, item.sourceLayout);
                    phraseRussian = item.sourceRussian;
                    ambiguousPrevious = null;
                    bool added = false;
                    if (item.typo && ValidCustomWord(item.original, item.sourceRussian) &&
                        undoLearning.Observe(CustomKey(item.original, item.sourceRussian), item.converted)) {
                        string key = CustomKey(item.original, item.sourceRussian);
                        added = customWords.Add(key);
                        if (added) {
                            try { if (!Program.TestMode) SaveCustomWords(customWordsPath, customWords); }
                            catch (Exception ex) { customWords.Remove(key); added = false;
                                tray.Text = UiText.Get("Ошибка сохранения слова: ", "Could not save the word: ") + ex.GetType().Name; }
                        }
                    }
                    DiagnosticLog.Write(added ? "undo.learned_after_repeat" : "undo.restored");
                    if (!item.punctuation) undo = ReverseCorrection(item, previousLayout);
                    else {
                        word.Clear(); recent = null; ResetPhrase();
                        if (recentTyped.Length > 0 && recentTyped[recentTyped.Length - 1] == item.converted[0])
                            recentTyped.Insert(recentTyped.Length - 1, ' ');
                    }
                    NotifyChange(item.original, added ? UiText.Get("добавлено в исключения", "added to exceptions") : UiText.Get("отмена замены", "correction undone"), false, item.fieldBounds, item.caretPoint);
                }
            } finally {
                List<Native.Input> replay = new List<Native.Input>(queuedKeys);
                queuedKeys.Clear();
                undoPending = false;
                if (replay.Count > 0) SendKeys(replay);
            }
        }

        private void ProcessManual(InputContext context) {
            PendingWord item = manual;
            manual = null;
            try {
                if (!context.Protected && item != null) ResolveDotEnding(item);
                if (Program.TestMode) Console.WriteLine("manual context protected=" + context.Protected + " reason=" + context.SkipReason + " item=" + (item != null));
                if (context.Protected) DiagnosticLog.Write(context.SkipReason);
                if (!context.Protected && item != null && InputAccess.Allows(item.foreground) && Native.GetForegroundWindow() == item.foreground &&
                    (item.focus == IntPtr.Zero || FocusedWindow(item.foreground) == item.focus) &&
                    ReplaceTypedWord(item.original.Length + (item.delimiterVk == 0 ? 0 : 1), item.converted, item.delimiterVk, item.delimiter)) {
                    IntPtr targetLayout = item.sourceRussian ? englishLayout : russianLayout;
                    IntPtr ignored;
                    if (Native.SendMessageTimeout(item.foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero,
                        targetLayout, 2, 150, out ignored) == IntPtr.Zero)
                        Native.PostMessage(item.foreground, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, targetLayout);
                    undo = new UndoRecord { original = item.original, converted = item.converted,
                        delimiterVk = item.delimiterVk, delimiter = item.delimiter,
                        foreground = item.foreground, focus = item.focus,
                        sourceLayout = item.originalLayout, sourceRussian = item.sourceRussian,
                        fieldBounds = context.FieldBounds, caretPoint = context.CaretPoint };
                    DiagnosticLog.Write("correction.manual_done");
                } else DiagnosticLog.Write("correction.manual_skipped");
            } catch (Exception ex) {
                if (Program.TestMode) Console.WriteLine(ex);
                DiagnosticLog.Write("error.manual_correction");
                tray.Text = UiText.Get("Ошибка переключения: ", "Layout conversion error: ") + ex.GetType().Name;
            } finally {
                List<Native.Input> replay = new List<Native.Input>(queuedKeys);
                queuedKeys.Clear();
                undoPending = false;
                if (replay.Count > 0) {
                    foreach (Native.Input input in replay) if (ReplayEditsInput(input)) undo = null;
                    SendKeys(replay);
                }
            }
        }

        private string ConvertSelectedText(string text, bool currentRussian, out bool targetRussian) {
            int russian = 0, english = 0;
            foreach (char c in text) { if (IsRussian(c)) russian++; if (IsEnglish(c)) english++; }
            bool sourceRussian = russian == english ? currentRussian : russian > english;
            targetRussian = !sourceRussian;
            if (russian + english == 0) return null;
            IntPtr source = sourceRussian ? russianLayout : englishLayout;
            IntPtr target = sourceRussian ? englishLayout : russianLayout;
            StringBuilder converted = new StringBuilder(text.Length);
            foreach (char c in text) {
                short key = Native.VkKeyScanEx(c, source);
                if (key == -1 || (key & 0x600) != 0 || Char.IsWhiteSpace(c)) { converted.Append(c); continue; }
                byte[] state = new byte[256];
                state[Native.VK_SHIFT] = (byte)((key & 0x100) != 0 ? 0x80 : 0);
                char mapped = Translate((uint)(key & 0xff), 0, state, target);
                converted.Append(mapped == '\0' ? c : mapped);
            }
            return converted.ToString();
        }

        private static string ReadNativeText(IntPtr focus) {
            IntPtr length;
            if (Native.SendMessageTimeout(focus, 0xE, IntPtr.Zero, IntPtr.Zero, 2, 150, out length) == IntPtr.Zero ||
                length.ToInt64() < 0 || length.ToInt64() > 1048576) return null;
            IntPtr buffer = Marshal.AllocHGlobal(((int)length + 1) * 2);
            try {
                IntPtr count;
                if (Native.SendMessageTimeout(focus, 0xD, (IntPtr)((int)length + 1), buffer, 2, 150, out count) == IntPtr.Zero) return null;
                if (count.ToInt64() < 0 || count.ToInt64() > length.ToInt64()) return null;
                return Marshal.PtrToStringUni(buffer, (int)count);
            } finally { Marshal.FreeHGlobal(buffer); }
        }

        private static bool TryNativeSelection(IntPtr focus, out InputContext context) {
            context = new InputContext();
            StringBuilder className = new StringBuilder(128);
            Native.GetClassName(focus, className, className.Capacity);
            string name = className.ToString();
            if (!name.Equals("Edit", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("WindowsForms10.EDIT", StringComparison.OrdinalIgnoreCase)) return false;
            if ((Native.GetWindowLongPtr(focus, Native.GWL_STYLE).ToInt64() & Native.ES_PASSWORD) != 0) return true;
            IntPtr start = Marshal.AllocHGlobal(4), end = Marshal.AllocHGlobal(4);
            try {
                IntPtr ignored;
                if (Native.SendMessageTimeout(focus, 0xB0, start, end, 2, 150, out ignored) == IntPtr.Zero) return true;
                int first = Marshal.ReadInt32(start), last = Marshal.ReadInt32(end);
                string text = ReadNativeText(focus);
                if (text == null || first < 0 || last < first || last > text.Length || last - first > 16384) return true;
                context.Protected = false;
                context.NativeSelectionStart = first;
                context.BeforeCaret = text.Substring(Math.Max(0, first - 4000), Math.Min(first, 4000));
                if (last > first) context.SelectionText = text.Substring(first, last - first);
                return true;
            } finally { Marshal.FreeHGlobal(start); Marshal.FreeHGlobal(end); }
        }

        private static bool ReplaceNativeRange(IntPtr focus, int start, string expected, string replacement) {
            IntPtr active = Native.GetForegroundWindow();
            if (FocusedWindow(active) != focus || !InputAccess.Allows(active)) return false;
            string text = ReadNativeText(focus);
            if (text == null || start < 0 || start + expected.Length > text.Length ||
                text.Substring(start, expected.Length) != expected) return false;
            IntPtr ignored;
            if (Native.SendMessageTimeout(focus, 0xB1, (IntPtr)start, (IntPtr)(start + expected.Length), 2, 150, out ignored) == IntPtr.Zero) return false;
            IntPtr value = Marshal.StringToHGlobalUni(replacement);
            try { return Native.SendMessageTimeout(focus, 0xC2, (IntPtr)1, value, 2, 150, out ignored) != IntPtr.Zero; }
            finally { Marshal.FreeHGlobal(value); }
        }

        private async void ProcessCorrection(IntPtr window, IntPtr focus, int revision) {
            bool delegated = false;
            try {
                DiagnosticLog.Write("correction.started");
                if (!InputAccess.Allows(window)) return;
                InputContext context;
                if ((Program.TestMode && ForceAutomationSelectionForTest) || !TryNativeSelection(focus, out context)) context = await GetInputContextAsync(true);
                if (Program.TestMode) Console.WriteLine("selection context protected=" + context.Protected + " chars=" + (context.SelectionText == null ? 0 : context.SelectionText.Length) + " native=" + context.NativeSelectionStart + " undo=" + (undo == null ? -9 : undo.selectionStart));
                if (context.Protected) { DiagnosticLog.Write(context.SkipReason); return; }
                if (revision != editingRevision || Native.GetForegroundWindow() != window ||
                    (focus != IntPtr.Zero && FocusedWindow(window) != focus)) { DiagnosticLog.Write("skip.focus_changed"); return; }
                if (!String.IsNullOrEmpty(context.SelectionText)) {
                    uint thread = Native.GetWindowThreadProcessId(window, IntPtr.Zero);
                    IntPtr originalLayout = Native.GetKeyboardLayout(thread);
                    bool targetRussian;
                    string replacement = ConvertSelectedText(context.SelectionText,
                        unchecked((ushort)originalLayout.ToInt64()) == 0x0419, out targetRussian);
                    if (replacement == null || replacement == context.SelectionText) { DiagnosticLog.Write("correction.selection_unchanged"); return; }
                    bool replaced = context.NativeSelectionStart >= 0
                        ? ReplaceNativeRange(focus, context.NativeSelectionStart, context.SelectionText, replacement)
                        : ReplaceTypedWord(0, replacement, 0, '\0');
                    if (replaced) {
                        IntPtr ignored;
                        IntPtr target = targetRussian ? russianLayout : englishLayout;
                        Native.SendMessageTimeout(window, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target, 2, 150, out ignored);
                        undo = new UndoRecord { original = context.SelectionText, converted = replacement,
                            foreground = window, focus = focus, sourceLayout = originalLayout,
                            fieldBounds = context.FieldBounds, caretPoint = context.CaretPoint, selectionStart = context.NativeSelectionStart >= 0 ? context.NativeSelectionStart : -2 };
                        manual = null; word.Clear(); recent = null; ResetTypingContext();
                        NotifyChange(context.SelectionText, replacement, true, context.FieldBounds, context.CaretPoint);
                        DiagnosticLog.Write("correction.selection_done");
                    } else DiagnosticLog.Write("correction.selection_failed");
                } else if (undo != null && undo.foreground == window &&
                    (undo.focus == IntPtr.Zero || undo.focus == focus)) {
                    DiagnosticLog.Write("correction.undo_requested");
                    if (undo.selectionStart == -2) {
                        UndoRecord record = undo; undo = null;
                        IntPtr previousLayout = Native.GetKeyboardLayout(Native.GetWindowThreadProcessId(window, IntPtr.Zero));
                        System.Threading.CancellationTokenSource selectionCancellation = new System.Threading.CancellationTokenSource();
                        Task<bool> select = Task.Run(() => {
                            try {
                                if (context.SelectionRange == null) return false;
                                string expected = record.converted.Replace("\r\n", "\n").Replace('\r', '\n');
                                int[] counts = { StringInfo.ParseCombiningCharacters(expected).Length, record.converted.Length };
                                foreach (int count in counts) {
                                    TextPatternRange range = context.SelectionRange.Clone();
                                    range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -count);
                                    string actual = range.GetText(16385).Replace("\r\n", "\n").Replace('\r', '\n');
                                    if (actual != expected) continue;
                                    if (selectionCancellation.IsCancellationRequested || revision != editingRevision || Native.GetForegroundWindow() != window ||
                                        (focus != IntPtr.Zero && FocusedWindow(window) != focus)) return false;
                                    range.Select(); return true;
                                }
                                return false;
                            } catch (Exception) { return false; }
                        });
                        bool completed = await Task.WhenAny(select, Task.Delay(750)) == select;
                        if (!completed) selectionCancellation.Cancel();
                        if (completed && await select &&
                            revision == editingRevision && Native.GetForegroundWindow() == window &&
                            (focus == IntPtr.Zero || FocusedWindow(window) == focus) &&
                            ReplaceTypedWord(0, record.original, 0, '\0')) {
                            IntPtr ignored;
                            Native.SendMessageTimeout(window, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, record.sourceLayout, 2, 150, out ignored);
                            undo = ReverseCorrection(record, previousLayout);
                            NotifyChange(record.original, UiText.Get("отмена замены", "correction undone"), false, record.fieldBounds, record.caretPoint);
                        } else DiagnosticLog.Write("correction.undo_range_unavailable");
                    } else { delegated = true; ProcessUndo(); }
                } else if (manual != null) {
                    DiagnosticLog.Write("correction.manual_requested");
                    delegated = true; ProcessManual(context);
                } else DiagnosticLog.Write("correction.no_word_or_selection");
            } catch (Exception ex) {
                if (Program.TestMode) Console.WriteLine(ex);
                DiagnosticLog.Write("error.selection");
                tray.Text = UiText.Get("Ошибка выделения: ", "Selection error: ") + ex.GetType().Name;
            } finally {
                if (!delegated) {
                    manual = null;
                    List<Native.Input> replay = new List<Native.Input>(queuedKeys);
                    queuedKeys.Clear(); undoPending = false;
                    if (replay.Count > 0) {
                        foreach (Native.Input input in replay)
                            if (ReplayEditsInput(input)) undo = null;
                        SendKeys(replay);
                    }
                }
            }
        }

        private static bool Down(int vk) { return
            (Native.GetAsyncKeyState(Native.VK_CONTROL) & 0x8000) != 0 ||
            (Native.GetAsyncKeyState(Native.VK_MENU) & 0x8000) != 0 ||
            (Native.GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 ||
            (Native.GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0; }

        private static bool Pressed(int vk) { return (Native.GetAsyncKeyState(vk) & 0x8000) != 0; }

        /// Copilot uses the correction path independently of the user-selected key.
        private static void SendCopilotPause() {
            Native.Input down = new Native.Input(); down.type = 1; down.data.key.vk = Native.VK_PAUSE;
            down.data.key.extraInfo = CopilotPauseMarker;
            Native.Input up = down; up.data.key.flags = Native.KEYEVENTF_KEYUP;
            Native.Input[] keys = { down, up };
            if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) != keys.Length)
                DiagnosticLog.Write("copilot.send_failed");
            else DiagnosticLog.Write("copilot.pause_sent");
        }

        private static bool WinDown() { return
            (Native.GetAsyncKeyState(Native.VK_LWIN) & 0x8000) != 0 ||
            (Native.GetAsyncKeyState(Native.VK_RWIN) & 0x8000) != 0; }

        private static bool IsModifierKey(int vk) {
            return vk == Native.VK_SHIFT || vk == Native.VK_CONTROL || vk == Native.VK_MENU ||
                vk == 20 ||
                vk == Native.VK_LWIN || vk == Native.VK_RWIN ||
                vk == 160 || vk == 161 || vk == 162 || vk == 163 || vk == 164 || vk == 165;
        }

        private bool ReplayEditsInput(Native.Input input) {
            int vk = input.data.key.vk;
            return (input.data.key.flags & Native.KEYEVENTF_KEYUP) == 0 &&
                !IsModifierKey(vk) && vk != Native.VK_F23 && vk != Native.VK_PAUSE && vk != correctionKey;
        }

        private static char Translate(uint vk, uint scan, byte[] state, IntPtr layout) {
            StringBuilder output = new StringBuilder(8);
            int count = Native.ToUnicodeEx(vk, scan, state, output, output.Capacity, 0, layout);
            return count == 1 ? output[0] : '\0';
        }

        private static bool IsRussian(char c) { return c >= '\u0410' && c <= '\u044F' || c == '\u0401' || c == '\u0451'; }
        private static bool IsEnglish(char c) { return c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'; }

        private static IntPtr FocusedWindow(IntPtr active) {
            uint threadId = Native.GetWindowThreadProcessId(active, IntPtr.Zero);
            Native.GuiThreadInfo info = new Native.GuiThreadInfo();
            info.cbSize = (uint)Marshal.SizeOf(typeof(Native.GuiThreadInfo));
            return Native.GetGUIThreadInfo(threadId, ref info) ? info.focus : IntPtr.Zero;
        }

        private bool PasswordField(uint threadId) {
            Native.GuiThreadInfo info = new Native.GuiThreadInfo();
            info.cbSize = (uint)Marshal.SizeOf(typeof(Native.GuiThreadInfo));
            if (!Native.GetGUIThreadInfo(threadId, ref info) || info.focus == IntPtr.Zero) return false;
            StringBuilder name = new StringBuilder(64);
            Native.GetClassName(info.focus, name, name.Capacity);
            if (name.ToString().IndexOf("Edit", StringComparison.OrdinalIgnoreCase) < 0) return false;
            long style = Native.GetWindowLongPtr(info.focus, Native.GWL_STYLE).ToInt64();
            return (style & Native.ES_PASSWORD) != 0;
        }

        private async void SwitchFnLayout(IntPtr window, IntPtr focus) {
            for (int i = 0; i < 15 && (pending != null || undoPending); i++) await Task.Delay(50);
            if (!fnEnabled || pending != null || undoPending || Native.GetForegroundWindow() != window) return;
            // Avoid reopening the Windows input switcher for every RU/EN tap. Keep its
            // full input-method ordering as the fallback for other configurations.
            if (Native.GetKeyboardLayoutList(0, null) == 2) {
                IntPtr[] layouts = new IntPtr[2];
                if (Native.GetKeyboardLayoutList(2, layouts) == 2 &&
                    Array.IndexOf(layouts, russianLayout) >= 0 && Array.IndexOf(layouts, englishLayout) >= 0) {
                    uint thread = Native.GetWindowThreadProcessId(window, IntPtr.Zero);
                    IntPtr current = Native.GetKeyboardLayout(thread);
                    if (current == russianLayout || current == englishLayout) {
                        IntPtr target = current == russianLayout ? englishLayout : russianLayout, ignored;
                        if (Native.SendMessageTimeout(window, Native.WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero,
                            target, 2, 50, out ignored) != IntPtr.Zero && Native.GetKeyboardLayout(thread) == target) {
                            DiagnosticLog.Write("layout.fn_direct");
                            if (Program.TestMode) FnDirectForTest++;
                            return;
                        }
                    }
                }
            }
            // Use Windows' input switcher: it also works on the desktop and when no edit has focus.
            // This sends no text and does not inspect the target application's input field.
            Native.Input[] keys = { VirtualKey(Native.VK_LWIN, false), VirtualKey(Native.VK_SPACE, false),
                VirtualKey(Native.VK_SPACE, true), VirtualKey(Native.VK_LWIN, true) };
            if (Program.TestMode) FnFallbackForTest++;
            if (Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) != keys.Length)
                DiagnosticLog.Write("layout.fn_failed");
            DiagnosticLog.Write("layout.fn_requested");
        }

        private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam) {
            if (code < 0) return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            Native.KbdData key = (Native.KbdData)Marshal.PtrToStructure(lParam, typeof(Native.KbdData));
            int message = wParam.ToInt32();
            int vk = (int)key.vkCode;
            bool keyDownEvent = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
            // Modifier taps pass through to Windows; any chord cancels correction.
            // Act only after release, so replayed text never inherits Alt/Ctrl.
            bool externalKey = key.extraInfo != Native.OurInputMarker && key.extraInfo != CopilotPauseMarker &&
                key.extraInfo != Native.ReplayInputMarker &&
                ((key.flags & Native.LLKHF_INJECTED) == 0 || Program.TestMode && key.extraInfo == UIntPtr.Zero);
            if (externalKey && correctionKey >= 162 && correctionKey <= 165) {
                bool pairedAltGr = vk == 165 && keyDownEvent && altGrControlCandidate && key.time == altGrControlTime;
                if (keyDownEvent) {
                    altGrControlCandidate = vk == 162 && !Pressed(162) && !Pressed(163);
                    if (altGrControlCandidate) altGrControlTime = key.time;
                }
                if (keyDownEvent && vk != correctionKey) { modifierTapArmed = false; modifierTapGeneration++; }
                if (vk == correctionKey) {
                    if (keyDownEvent && !modifierTapDown) {
                        modifierTapDown = true; modifierTapArmed = true;
                        foreach (int modifierCode in new[] { 160, 161, 162, 163, 164, 165, 91, 92 })
                            if (modifierCode != vk && !(pairedAltGr && modifierCode == 162) && Pressed(modifierCode)) modifierTapArmed = false;
                        modifierTapWindow = Native.GetForegroundWindow(); modifierTapFocus = FocusedWindow(modifierTapWindow);
                    } else if (message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) {
                        bool activate = modifierTapDown && modifierTapArmed;
                        modifierTapDown = modifierTapArmed = false;
                        if (activate) {
                            if (vk == 164 || vk == 165) {
                                Native.Input[] mask = { VirtualKey(0xE8, false), VirtualKey(0xE8, true) };
                                Native.SendInput((uint)mask.Length, mask, Marshal.SizeOf(typeof(Native.Input)));
                            }
                            IntPtr window = modifierTapWindow, focus = modifierTapFocus; int generation = modifierTapGeneration;
                            dispatcher.BeginInvoke((Action)delegate {
                                if (generation == modifierTapGeneration && Native.GetForegroundWindow() == window &&
                                    FocusedWindow(window) == focus && !Down(0) && !Pressed(Native.VK_SHIFT)) SendCopilotPause();
                            });
                        }
                    }
                }
            }
            // Standalone Fn observed on this ThinkPad: VK_FF, extended scan E0 63.
            // Firmware sends no such event for the tested Fn+F1 combination.
            if (vk == 255 && key.scanCode == 99 && (key.flags & 1) != 0 &&
                ((key.flags & Native.LLKHF_INJECTED) == 0 || Program.TestMode && key.extraInfo == UIntPtr.Zero)) {
                if (keyDownEvent && !fnKeyDown) {
                    fnKeyDown = true; fnWindow = Native.GetForegroundWindow(); fnFocus = FocusedWindow(fnWindow);
                    fnArmed = fnEnabled && !Down(vk) && !Pressed(Native.VK_SHIFT);
                } else if (message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) {
                    bool activate = fnKeyDown && fnArmed; fnKeyDown = fnArmed = false;
                    IntPtr window = fnWindow, focus = fnFocus;
                    if (activate) dispatcher.BeginInvoke((Action)delegate { SwitchFnLayout(window, focus); });
                }
                return fnEnabled ? (IntPtr)1 : Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if (keyDownEvent && key.extraInfo != Native.OurInputMarker) fnArmed = false;
            if (vk == 20 && key.extraInfo != Native.OurInputMarker) {
                if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN) {
                    if (!capsKeyDown) { capsActive = !capsActive; capsKeyDown = true; }
                } else if (message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) capsKeyDown = false;
            }
            if (copilotEnabled && key.extraInfo != Native.OurInputMarker) {
                // Клавиша Copilot: левые Win+Shift+F23. F23 не пропускаем — иначе Windows запустит Copilot;
                // vkE8 при зажатом Win не даёт открыться «Пуску»; Pause — только после отпускания Win и Shift,
                // иначе перенабранный текст превратится в сочетания Win+Shift+буква.
                bool keyDown = message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN;
                if (vk == Native.VK_F23 && (copilotChord || keyDown && Pressed(Native.VK_LWIN) && Pressed(160))) {
                    if (keyDown && !copilotChord) {
                        copilotChord = true;
                        DiagnosticLog.Write("copilot.captured");
                        Native.Input[] mask = { VirtualKey(0xE8, false), VirtualKey(0xE8, true) };
                        Native.SendInput((uint)mask.Length, mask, Marshal.SizeOf(typeof(Native.Input)));
                    }
                    return (IntPtr)1;
                }
                if (copilotChord && !keyDown && (vk == Native.VK_LWIN || vk == 160) &&
                    !Pressed(vk == Native.VK_LWIN ? 160 : Native.VK_LWIN)) {
                    copilotChord = false;
                    dispatcher.BeginInvoke((Action)SendCopilotPause);
                }
            }
            if ((vk == correctionKey && correctionKey == Native.VK_PAUSE || vk == Native.VK_PAUSE && key.extraInfo == CopilotPauseMarker) &&
                key.extraInfo != Native.OurInputMarker &&
                ((key.flags & Native.LLKHF_INJECTED) == 0 || key.extraInfo == CopilotPauseMarker || key.extraInfo == Native.ReplayInputMarker || Program.TestMode && key.extraInfo == UIntPtr.Zero)) {
                if (message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) {
                    bool captured = correctionKeyDown; correctionKeyDown = false;
                    if (vk == suppressedKeyUp) suppressedKeyUp = -1;
                    if (captured) return (IntPtr)1;
                }
                if (keyDownEvent && correctionKeyDown) return (IntPtr)1;
                if (keyDownEvent) {
                    DiagnosticLog.Write("correction.key_received");
                    if (Down(vk) || Pressed(Native.VK_SHIFT)) DiagnosticLog.Write("correction.modifier_still_down");
                    if (pending != null || undoPending) DiagnosticLog.Write("correction.queued_while_busy");
                }
                // Modified Pause/Break retains its usual application shortcut.
                if (keyDownEvent && !Down(vk) && !Pressed(Native.VK_SHIFT) && pending == null && !undoPending) {
                    IntPtr pauseWindow = Native.GetForegroundWindow();
                    PendingWord item = null;
                    if (pauseWindow != IntPtr.Zero &&
                        InputAccess.Allows(pauseWindow) &&
                        !excludedApps.Contains(ApplicationName(pauseWindow) ?? "") &&
                        !PasswordField(Native.GetWindowThreadProcessId(pauseWindow, IntPtr.Zero))) {
                        if (pauseWindow == foreground && word.Count > 0) item = CaptureWord(0, '\0', pauseWindow);
                        else if (recent != null && pauseWindow == recent.foreground &&
                            (recent.focus == IntPtr.Zero || FocusedWindow(pauseWindow) == recent.focus)) item = recent;
                        if (item != null && (item.converted == null || item.converted == item.original)) item = null;
                        IntPtr pauseFocus = FocusedWindow(pauseWindow);
                        int revision = editingRevision;
                        word.Clear(); recent = null; manual = item; undoPending = true; suppressedKeyUp = vk; correctionKeyDown = true;
                        dispatcher.BeginInvoke((Action)delegate { ProcessCorrection(pauseWindow, pauseFocus, revision); });
                        return (IntPtr)1;
                    }
                }
            }
            if ((key.flags & Native.LLKHF_INJECTED) != 0 && key.extraInfo != Native.ReplayInputMarker && key.extraInfo != CopilotPauseMarker &&
                !(Program.TestMode && key.extraInfo == UIntPtr.Zero))
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            if ((message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) && vk == suppressedKeyUp) {
                suppressedKeyUp = -1; return (IntPtr)1;
            }
            if (pending != null || undoPending) {
                if (message == Native.WM_KEYDOWN || message == Native.WM_SYSKEYDOWN || message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP)
                    queuedKeys.Add(ReplayKey(key, message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP));
                return (IntPtr)1;
            }
            if (message == Native.WM_KEYUP || message == Native.WM_SYSKEYUP) {
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if (message != Native.WM_KEYDOWN && message != Native.WM_SYSKEYDOWN)
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);

            suggestionPopup.Dismiss();

            if (!IsModifierKey(vk) && vk != Native.VK_F23) {
                if (Program.TestMode && undo != null) Console.WriteLine("undo cleared by key vk=" + vk + " flags=" + key.flags);
                undo = null; recent = null;
            }

            IntPtr active = Native.GetForegroundWindow();
            if (!InputAccess.Allows(active)) {
                word.Clear(); recent = null; undo = null; ResetTypingContext();
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if (dictionaryWindow != null && !dictionaryWindow.IsDisposed && dictionaryWindow.ContainsFocus) {
                word.Clear(); recent = null; undo = null; ResetTypingContext();
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if (active != foreground) { foreground = active; activeApp = ApplicationName(active); word.Clear(); recent = null; undo = null; ResetTypingContext(); }
            if (activeApp != null && (Program.TestMode || !activeApp.Equals(selfAppName, StringComparison.OrdinalIgnoreCase))) lastApp = activeApp;
            if (activeApp != null && excludedApps.Contains(activeApp)) { word.Clear(); undo = null;
                DiagnosticLog.Write("skip.excluded_app");
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam); }
            bool modifier = IsModifierKey(vk);
            if (vk == Native.VK_SPACE && WinDown())
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            if (active == IntPtr.Zero || (!modifier && vk != Native.VK_F23 && Down(vk))) {
                word.Clear(); recent = null; ResetTypingContext(); return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if (modifier || vk == Native.VK_F23)
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            uint thread = Native.GetWindowThreadProcessId(active, IntPtr.Zero);
            if (PasswordField(thread)) { DiagnosticLog.Write("skip.password"); word.Clear(); ResetTypingContext(); return Native.CallNextHookEx(keyboardHook, code, wParam, lParam); }

            if (vk == Native.VK_BACK) {
                if (word.Count > 0) word.RemoveAt(word.Count - 1);
                else ambiguousPrevious = null;
                if (recentTyped.Length > 0) recentTyped.Length--;
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if (vk == Native.VK_ESCAPE || vk == 35 || vk == 36 || vk == 37 || vk == 38 || vk == 39 || vk == 40 || vk == 45 || vk == 46) {
                word.Clear(); ResetTypingContext(); return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            IntPtr layout = Native.GetKeyboardLayout(thread);
            int language = unchecked((ushort)layout.ToInt64());
            if (language != 0x0409 && language != 0x0419) {
                word.Clear(); return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            byte[] state = new byte[256];
            if (!Native.GetKeyboardState(state)) {
                word.Clear(); return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            state[Native.VK_SHIFT] = (byte)((Native.GetAsyncKeyState(Native.VK_SHIFT) & 0x8000) != 0 ? 0x80 : 0);
            state[20] = (byte)(capsActive ? 1 : 0);
            char c = vk == Native.VK_SPACE ? ' ' : vk == Native.VK_RETURN ? '\n' : vk == Native.VK_TAB ? '\t' : Translate(key.vkCode, key.scanCode, state, layout);
            if (typoEnabled && SpaceBeforePunctuationForTest(recentTyped.ToString(), c) &&
                (!codeProtectionEnabled || !IsCodeApp(activeApp) && !InsideMarkdownCodeForTest(recentTyped.ToString()))) {
                RememberTyped(c); word.Clear(); recent = null; undo = null; ResetPhrase();
                IntPtr focus = FocusedWindow(active);
                int revision = editingRevision;
                undoPending = true; suppressedKeyUp = vk;
                dispatcher.BeginInvoke((Action)delegate { ProcessPunctuation(active, focus, revision, c, layout); });
                return (IntPtr)1;
            }
            RememberTyped(c);
            if (codeProtectionEnabled && c == '`') {
                // A delimiter belongs to the code span, including its last word.
                // Pass it unchanged; start tracking the next ordinary word afresh.
                word.Clear(); recent = null; ResetPhrase();
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            bool possibleRussianLetter = language == 0x0409 &&
                (c == '\'' || (word.Count > 0 ? c == ',' || c == ';' || c == '.' : c == ',' || c == '['));
            if (IsRussian(c) || IsEnglish(c) || possibleRussianLetter || word.Count > 0 && IsWordJoiner(c)) {
                if (word.Count == 0) wordLanguage = language;
                if (word.Count >= 64) word.Clear();
                word.Add(new KeyStroke { vk = key.vkCode, scan = key.scanCode, language = language,
                    keyboardState = state, character = c });
                return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            if ((word.Count < 2 && !(word.Count == 1 && c != '\0' &&
                IsShortLayoutWord(CaptureWord(vk, c, active)))) || c == '\0') {
                if (vk == Native.VK_TAB) ResetTypingContext();
                if (word.Count == 0 || c != ' ') ambiguousPrevious = null;
                if (c != ' ') phraseRussian = null;
                if (word.Count == 1 && c != '\0' && vk != Native.VK_RETURN && vk != Native.VK_TAB)
                    recent = CaptureWord(vk, c, active);
                word.Clear(); return Native.CallNextHookEx(keyboardHook, code, wParam, lParam);
            }
            PendingWord itemForProcessing = CaptureWord(vk, c, active);
            word.Clear();
            pending = itemForProcessing;
            suppressedKeyUp = vk;
            dispatcher.BeginInvoke((Action)ProcessPending);
            return (IntPtr)1;
        }

        private string JoinWord() { StringBuilder result = new StringBuilder(word.Count); foreach (KeyStroke stroke in word) result.Append(stroke.character); return result.ToString(); }

        private void ResolveDotEnding(PendingWord item) {
            // A final dot can be punctuation or the Russian letter ю. Ask the dictionaries
            // outside the keyboard hook, preserving punctuation after a known source word.
            if (item.wholeTarget == null || IsKnown(item.sourceCheck.ToLowerInvariant(), item.sourceRussian) ||
                !IsKnown(item.wholeTarget.ToLowerInvariant(), !item.sourceRussian)) return;
            item.sourceCheck = item.original;
            item.targetCheck = item.converted = item.wholeTarget;
            item.suffix = "";
        }

        private static bool IsShortLayoutWord(PendingWord item) {
            if (item == null || item.sourceCheck == null || item.targetCheck == null || item.mixedLayout ||
                item.sourceCheck != item.sourceCheck.ToLowerInvariant()) return false;
            if (item.targetCheck.Length < (item.sourceRussian ? 2 : 1) || item.targetCheck.Length > 3) return false;
            HashSet<string> target = item.sourceRussian ? commonEnglishShort : commonRussianShort;
            HashSet<string> source = item.sourceRussian ? commonRussianShort : commonEnglishShort;
            return target.Contains(item.targetCheck) && !source.Contains(item.sourceCheck);
        }

        private static readonly HashSet<string> commonRussianShort = new HashSet<string>((
            "я в и с к у о а не на по за из от до об во со ко то он мы вы ты она оно они как так что где кто " +
            "чем там тут вот уже еще ещё был это его её ее мне вам нам все всё вся всю для при про над под без " +
            "или да нет ну ли же бы два три раз сам мой моя мою мои наш ваш их ему ей им нас вас эта эти эту " +
            "тот той чей чья чьё дом год час день кот лес мир сон дал дай иди").Split(' '), StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> commonEnglishShort = new HashSet<string>((
            "a i an as at be by do go he if in is it me my no of on or so to up us we am ok oh hi i'm i'd " +
            "the and are but can did for get got has had her him his how its let may new not now old one our " +
            "out own say see she too two use was way who why yes yet you all any big few man men off put run " +
            "set try day end far ask add ago lot top bad buy car cat dog eat fix god job key law low sit " +
            "bye bug log app web").Split(' '),
            StringComparer.OrdinalIgnoreCase);

        private PendingWord CaptureWord(int delimiterVk, char delimiter, IntPtr active) {
            bool sourceRussian = wordLanguage == 0x0419;
            string original = JoinWord();
            int coreLength = word.Count;
            while (coreLength > 0 && !IsRussian(word[coreLength - 1].character) &&
                !IsEnglish(word[coreLength - 1].character)) coreLength--;
            if (coreLength < word.Count && !sourceRussian) {
                string fullRussian = ConvertWord(russianLayout, word.Count);
                if (fullRussian != null && fullRussian.Equals("об", StringComparison.OrdinalIgnoreCase))
                    coreLength = word.Count;
            }
            string suffix = original.Substring(coreLength);
            string targetCheck = ConvertWord(sourceRussian ? englishLayout : russianLayout, coreLength);
            bool mixed = false;
            for (int i = 0; i < coreLength; i++) if (word[i].language != wordLanguage) mixed = true;
            uint thread = Native.GetWindowThreadProcessId(active, IntPtr.Zero);
            return new PendingWord { original = original, sourceCheck = original.Substring(0, coreLength),
                targetCheck = targetCheck, suffix = suffix,
                wholeTarget = !sourceRussian && suffix.IndexOf('.') >= 0 ? ConvertWord(russianLayout, word.Count) : null,
                converted = targetCheck == null ? null : targetCheck + suffix,
                sourceRussian = sourceRussian, mixedLayout = mixed,
                delimiterVk = delimiterVk, delimiter = delimiter,
                foreground = active, focus = FocusedWindow(active), originalLayout = Native.GetKeyboardLayout(thread) };
        }

        private string ConvertWord(IntPtr targetLayout, int length) {
            StringBuilder result = new StringBuilder(length);
            for (int i = 0; i < length; i++) {
                KeyStroke stroke = word[i];
                char c = stroke.character == '\u2019' ? stroke.character : Translate(stroke.vk, stroke.scan, stroke.keyboardState, targetLayout);
                if (!IsRussian(c) && !IsEnglish(c) && !IsWordJoiner(c)) return null;
                result.Append(c);
            }
            return result.ToString();
        }

        private static Native.Input VirtualKey(ushort vk, bool up) {
            Native.Input input = new Native.Input(); input.type = 1;
            input.data.key.vk = vk; input.data.key.flags = up ? Native.KEYEVENTF_KEYUP : 0; input.data.key.extraInfo = Native.OurInputMarker;
            return input;
        }
        private static Native.Input UnicodeKey(char c, bool up) {
            Native.Input input = new Native.Input(); input.type = 1;
            input.data.key.scan = c; input.data.key.flags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0); input.data.key.extraInfo = Native.OurInputMarker;
            return input;
        }
        private static bool ReplaceTypedWord(int length, string replacement, int delimiterVk, char delimiter) {
            if (!InputAccess.Allows(Native.GetForegroundWindow())) return false;
            List<Native.Input> inputs = new List<Native.Input>();
            for (int i = 0; i < length; i++) { inputs.Add(VirtualKey(Native.VK_BACK, false)); inputs.Add(VirtualKey(Native.VK_BACK, true)); }
            for (int i = 0; i < replacement.Length; i++) {
                char c = replacement[i];
                if (c == '\n' && i > 0 && replacement[i - 1] == '\r') continue;
                inputs.Add(UnicodeKey(c, false)); inputs.Add(UnicodeKey(c, true));
            }
            if (delimiterVk == Native.VK_SPACE || delimiterVk == Native.VK_RETURN || delimiterVk == Native.VK_TAB) {
                inputs.Add(VirtualKey((ushort)delimiterVk, false)); inputs.Add(VirtualKey((ushort)delimiterVk, true));
            } else if (delimiterVk != 0) { inputs.Add(UnicodeKey(delimiter, false)); inputs.Add(UnicodeKey(delimiter, true)); }
            return SendKeys(inputs);
        }

        private static bool TryCaretPoint(IntPtr active, out Point point) {
            point = Point.Empty;
            uint threadId = Native.GetWindowThreadProcessId(active, IntPtr.Zero);
            Native.GuiThreadInfo info = new Native.GuiThreadInfo();
            info.cbSize = (uint)Marshal.SizeOf(typeof(Native.GuiThreadInfo));
            if (!Native.GetGUIThreadInfo(threadId, ref info) || info.caret == IntPtr.Zero) return false;
            if (info.caretRect.bottom <= info.caretRect.top) return false;
            point = new Point(info.caretRect.left, info.caretRect.top);
            return Native.ClientToScreen(info.caret, ref point) &&
                HintPosition.Visible(new Rectangle(point.X, point.Y, 1, info.caretRect.bottom - info.caretRect.top));
        }

        internal static Point? PopupAnchor(Point? fallbackCaret = null) {
            Point caret;
            if (TryCaretPoint(Native.GetForegroundWindow(), out caret)) return caret;
            return fallbackCaret;
        }

        private async void NotifyChange(string before, string after, bool canUndo, Rectangle? fieldBounds, Point? fallbackCaret = null) {
            if (!spellingEnabled) return;
            suggestionPopup.Dismiss();
            int generation = suggestionPopup.Generation;
            IntPtr targetWindow = Native.GetForegroundWindow(), targetFocus = FocusedWindow(targetWindow);
            // Allow the edit control to consume replacement input before reading its new caret.
            await Task.Delay(60);
            if (Program.TestMode) Console.WriteLine("hint before context: generation=" + (generation == suggestionPopup.Generation) +
                " window=" + (Native.GetForegroundWindow() == targetWindow) + " focus=" + (FocusedWindow(targetWindow) == targetFocus));
            if (generation != suggestionPopup.Generation || !spellingEnabled || Native.GetForegroundWindow() != targetWindow ||
                FocusedWindow(targetWindow) != targetFocus) return;
            InputContext current = await GetInputContextAsync();
            if (Program.TestMode) Console.WriteLine("hint context: protected=" + current.Protected + " reason=" + current.SkipReason +
                " generation=" + (generation == suggestionPopup.Generation) + " window=" + (Native.GetForegroundWindow() == targetWindow) +
                " focus=" + (FocusedWindow(targetWindow) == targetFocus));
            if (generation != suggestionPopup.Generation || current.Protected || Native.GetForegroundWindow() != targetWindow ||
                FocusedWindow(targetWindow) != targetFocus) return;
            if (before.Length > 100) before = before.Substring(0, 100) + "…";
            if (after.Length > 100) after = after.Substring(0, 100) + "…";
            string content = canUndo ? before + " → " + after : "↩ " + before + Environment.NewLine + after;
            Point? anchor = PopupAnchor(current.CaretPoint);
            if (!anchor.HasValue) { DiagnosticLog.Write("hint.caret_unavailable"); return; }
            suggestionPopup.ShowAt(anchor.Value, null, content, canUndo ? 2500 : 1800);
        }

        internal void ReplaceAndNotifyForTest(string original, string replacement) {
            if (!ReplaceTypedWord(original.Length, replacement, 0, '\0')) throw new Exception("Test replacement failed");
            NotifyChange(original, replacement, true, null);
        }

        protected override void ExitThreadCore() {
            if (keyboardHook != IntPtr.Zero) Native.UnhookWindowsHookEx(keyboardHook);
            if (mouseHook != IntPtr.Zero) Native.UnhookWindowsHookEx(mouseHook);
            tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); suggestionPopup.Dispose(); dispatcher.Dispose(); speller.Dispose();
            if (dictionaryWindow != null) dictionaryWindow.Dispose();
            base.ExitThreadCore();
        }
    }

    static class Program {
        internal static bool TestMode;
        private static bool SendTestCopilotPause() {
            Native.Input shiftDown = new Native.Input(); shiftDown.type = 1; shiftDown.data.key.vk = 160;
            Native.Input shiftUp = shiftDown; shiftUp.data.key.flags = Native.KEYEVENTF_KEYUP;
            Native.Input pauseDown = new Native.Input(); pauseDown.type = 1; pauseDown.data.key.vk = Native.VK_PAUSE;
            pauseDown.data.key.extraInfo = new UIntPtr(0x123);
            Native.Input pauseUp = pauseDown; pauseUp.data.key.flags = Native.KEYEVENTF_KEYUP;
            Native.Input[] keys = { shiftDown, shiftUp, pauseDown, pauseUp };
            return Native.SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Native.Input))) == keys.Length;
        }
        private static bool ToggleCapsForTest() {
            Native.Input down = new Native.Input(); down.type = 1; down.data.key.vk = 20;
            Native.Input up = down; up.data.key.flags = Native.KEYEVENTF_KEYUP;
            Native.Input[] keys = { down, up };
            return Native.SendInput(2, keys, Marshal.SizeOf(typeof(Native.Input))) == 2;
        }
        private static int IntegrationTest(int startIndex, int caseLimit = Int32.MaxValue) {
            TestMode = true;
            bool initialCaps = (Native.GetKeyState(20) & 1) != 0;
            const int undoCase = 12, excludedCase = 13, customCase = 14, passwordCase = 15, firstManualCase = 16;
            int result = 0, testIndex = startIndex;
            string[] physicalKeys = { "GHBDTN ", "HELLO ", "HELLO ", "GHBDTN ", "HELLLO ", "GHBDTN HELLO ", "GHBDTN.", "GHBDTN ",
                "HF,JNFTN ", "YT ", "GJKYJCNM ", "HF,JNFTN YT GJKYJCNM ", "GHBDTN ", "GHBDTN ", "GHBDTN ", "HF,JNFTN ",
                "B", "B", "B ", "B ", "B", "HELLO " };
            string[] expectedText = { "привет ", "hello ", "hello ", "привет ", "hello ", "привет hello ", "привет.", "ПРИВЕТ ",
                "работает ", "не ", "полностью ", "работает не полностью ", "ghbdtn ", "ghbdtn ", "ghbdtn ", "hf,jnftn ",
                "и", "b", "и ", "b ", "b", "руддщ " };
            bool[] useRussianLayout = { false, true, false, true, false, false, false, false, false, false, false, false, false, false, false, false,
                false, true, false, true, false, false };
            bool[] holdShift = { false, false, false, false, false, false, false, true, false, false, false, false, false, false, false, false,
                false, false, false, false, false, false };
            const int prepositionStart = 22;
            string[] shortWordCases = { "D:в", "R:к", "J:о", "C:с", "E:у", "BP:из", "YF:на", "JN:от", "J,:об", "LJ:до",
                "PF:за", "GJ:по", "CJ:со", "DJ:во", "RJ:ко", "B:и", "AT:at", "BY:by", "IN:in", "OF:of", "ON:on", "TO:to", "UP:up", "AS:as" };
            int typoUndoCase = prepositionStart + shortWordCases.Length;
            Array.Resize(ref physicalKeys, typoUndoCase + 3);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            for (int i = 0; i < shortWordCases.Length; i++) {
                string[] parts = shortWordCases[i].Split(':');
                physicalKeys[prepositionStart + i] = parts[0] + " ";
                expectedText[prepositionStart + i] = parts[1] + " ";
                useRussianLayout[prepositionStart + i] = i >= 16;
            }
            physicalKeys[typoUndoCase] = "HELLLO ";
            expectedText[typoUndoCase] = "helllo ";
            physicalKeys[typoUndoCase + 1] = "GHBDNT ";
            expectedText[typoUndoCase + 1] = "привет ";
            useRussianLayout[typoUndoCase + 1] = true;
            int popupCase = physicalKeys.Length - 1;
            physicalKeys[popupCase] = "GHBDTN ";
            expectedText[popupCase] = "привет ";
            useRussianLayout[popupCase] = false;
            int mixedCaseStart = physicalKeys.Length;
            Array.Resize(ref physicalKeys, mixedCaseStart + 3);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[mixedCaseStart] = "CVJ;|TIM ";
            expectedText[mixedCaseStart] = "сможешь ";
            physicalKeys[mixedCaseStart + 1] = "GHBDTN; ";
            expectedText[mixedCaseStart + 1] = "привет; ";
            physicalKeys[mixedCaseStart + 2] = "HELLO; ";
            expectedText[mixedCaseStart + 2] = "hello; ";
            int leadingCaseStart = physicalKeys.Length;
            Array.Resize(ref physicalKeys, leadingCaseStart + 10);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[leadingCaseStart] = "[JXTIM ";
            expectedText[leadingCaseStart] = "хочешь ";
            physicalKeys[leadingCaseStart + 1] = ",ELTIM ";
            expectedText[leadingCaseStart + 1] = "будешь ";
            physicalKeys[leadingCaseStart + 2] = "[HELLO ";
            expectedText[leadingCaseStart + 2] = "[hello ";
            physicalKeys[leadingCaseStart + 3] = ",HELLO ";
            expectedText[leadingCaseStart + 3] = ",hello ";
            physicalKeys[leadingCaseStart + 4] = "'NJ ";
            expectedText[leadingCaseStart + 4] = "это ";
            physicalKeys[leadingCaseStart + 5] = "'|NJ ";
            expectedText[leadingCaseStart + 5] = "это ";
            physicalKeys[leadingCaseStart + 6] = "'HELLO ";
            expectedText[leadingCaseStart + 6] = "'hello ";
            physicalKeys[leadingCaseStart + 7] = "HELLO' ";
            expectedText[leadingCaseStart + 7] = "hello' ";
            physicalKeys[leadingCaseStart + 8] = "I'M ";
            expectedText[leadingCaseStart + 8] = "i'm ";
            physicalKeys[leadingCaseStart + 9] = "DON'T ";
            expectedText[leadingCaseStart + 9] = "don't ";
            int shortExtraStart = physicalKeys.Length;
            Array.Resize(ref physicalKeys, shortExtraStart + 2);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[shortExtraStart] = "RFR ";
            expectedText[shortExtraStart] = "как ";
            physicalKeys[shortExtraStart + 1] = "THE ";
            expectedText[shortExtraStart + 1] = "the ";
            useRussianLayout[shortExtraStart + 1] = true;
            int phraseCase = physicalKeys.Length;
            Array.Resize(ref physicalKeys, phraseCase + 1);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[phraseCase] = "HERE WE ";
            expectedText[phraseCase] = "here we ";
            useRussianLayout[phraseCase] = true;
            int codeCase = physicalKeys.Length;
            Array.Resize(ref physicalKeys, codeCase + 1);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[codeCase] = "`GHBDTN ";
            expectedText[codeCase] = "`ghbdtn ";
            int caseFixCase = physicalKeys.Length;
            Array.Resize(ref physicalKeys, caseFixCase + 1);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[caseFixCase] = "G~HBDTN~ ";
            expectedText[caseFixCase] = "Привет ";
            useRussianLayout[caseFixCase] = true;
            int capsCase = physicalKeys.Length;
            Array.Resize(ref physicalKeys, capsCase + 1);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[capsCase] = "G!HBDTN ";
            expectedText[capsCase] = "Привет ";
            useRussianLayout[capsCase] = true;
            int itStart = physicalKeys.Length;
            Array.Resize(ref physicalKeys, itStart + 4);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            physicalKeys[itStart] = physicalKeys[itStart + 1] = "HYPERVISOR ";
            expectedText[itStart] = expectedText[itStart + 1] = "hypervisor ";
            useRussianLayout[itStart + 1] = true;
            physicalKeys[itStart + 2] = physicalKeys[itStart + 3] = "AEKCNTR ";
            expectedText[itStart + 2] = expectedText[itStart + 3] = "фулстек ";
            useRussianLayout[itStart + 3] = true;
            int learningStart = physicalKeys.Length;
            Array.Resize(ref physicalKeys, learningStart + 3);
            Array.Resize(ref expectedText, physicalKeys.Length);
            Array.Resize(ref useRussianLayout, physicalKeys.Length);
            Array.Resize(ref holdShift, physicalKeys.Length);
            for (int i = learningStart; i < physicalKeys.Length; i++) {
                physicalKeys[i] = "HELLLO "; expectedText[i] = "helllo ";
            }
            string[] beforeManual = { "b", "и", "b ", "и ", "b", "hello " };
            bool undoSent = false;
            int manualStage = 0;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp();
            Form form = new Form();
            form.Text = "Rasklad integration test";
            form.Width = 500; form.Height = 160;
            TextBox box = new TextBox();
            box.Dock = DockStyle.Fill;
            form.Controls.Add(box);
            TextBox passwordBox = new TextBox();
            passwordBox.Dock = DockStyle.Bottom;
            passwordBox.UseSystemPasswordChar = true;
            form.Controls.Add(passwordBox);
            Timer send = new Timer(); send.Interval = 200;
            Timer inspect = new Timer(); inspect.Interval = 1500;
            send.Tick += delegate {
                send.Stop();
                IntPtr previous = Native.GetForegroundWindow();
                uint previousThread = previous == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(previous, IntPtr.Zero);
                uint currentThread = Native.GetCurrentThreadId();
                bool attached = previousThread != 0 && previousThread != currentThread && Native.AttachThreadInput(currentThread, previousThread, true);
                bool foregroundSet = Native.SetForegroundWindow(form.Handle);
                TextBox targetBox = testIndex == passwordCase ? passwordBox : box;
                form.Activate(); form.ActiveControl = targetBox; targetBox.Select(); targetBox.Focus();
                if (attached) Native.AttachThreadInput(currentThread, previousThread, false);
                if (!targetBox.Focused) { targetBox.Select(); Application.DoEvents(); }
                Native.ActivateKeyboardLayout(Native.LoadKeyboardLayout(useRussianLayout[testIndex] ? "00000419" : "00000409", 0), 0);
                Console.WriteLine("foreground=" + (Native.GetForegroundWindow() == form.Handle) + "; focus=" + targetBox.Focused);
                if (!foregroundSet) Console.WriteLine("test foreground request failed; attached=" + attached);
                if (Native.GetForegroundWindow() != form.Handle || !targetBox.Focused) {
                    result = 2; form.Close(); Application.ExitThread(); return;
                }
                if (testIndex == excludedCase) app.SetCurrentAppExcludedForTest(true);
                if (testIndex == customCase) {
                    app.SetCurrentAppExcludedForTest(false);
                    app.AddCustomWordForTest("ghbdtn", false);
                }
                if (testIndex == passwordCase) app.RemoveCustomWordForTest("ghbdtn", false);
                if (testIndex == typoUndoCase) app.SetAutoEnabledForTest(false);
                if (testIndex == learningStart) { app.ResetLearningForTest(); app.SetAutoEnabledForTest(false); }
                if (testIndex == typoUndoCase + 1) app.SetAutoEnabledForTest(true);
                if (testIndex == capsCase && (Native.GetKeyState(20) & 1) != 0 && !ToggleCapsForTest()) {
                    result = 3; form.Close(); Application.ExitThread(); return;
                }
                app.ResetContextForTest();
                targetBox.Clear();
                List<Native.Input> keys = new List<Native.Input>();
                bool shiftedPart = false;
                if (holdShift[testIndex]) {
                    Native.Input shift = new Native.Input(); shift.type = 1; shift.data.key.vk = Native.VK_SHIFT;
                    keys.Add(shift);
                }
                foreach (char c in physicalKeys[testIndex]) {
                    if (c == '!') {
                        Native.Input capsDown = new Native.Input(); capsDown.type = 1; capsDown.data.key.vk = 20;
                        Native.Input capsUp = capsDown; capsUp.data.key.flags = Native.KEYEVENTF_KEYUP;
                        keys.Add(capsDown); keys.Add(capsUp);
                        continue;
                    }
                    if (c == '~') {
                        Native.Input shiftPart = new Native.Input(); shiftPart.type = 1;
                        shiftPart.data.key.vk = Native.VK_SHIFT;
                        shiftPart.data.key.flags = shiftedPart ? Native.KEYEVENTF_KEYUP : 0;
                        keys.Add(shiftPart); shiftedPart = !shiftedPart;
                        continue;
                    }
                    if (c == '|') {
                        if (Native.SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(Native.Input))) != keys.Count) {
                            result = 3; form.Close(); Application.ExitThread(); return;
                        }
                        keys.Clear();
                        Native.ActivateKeyboardLayout(Native.LoadKeyboardLayout("00000419", 0), 0);
                        continue;
                    }
                    Native.Input down = new Native.Input(); down.type = 1;
                    down.data.key.vk = c == '.' ? (ushort)190 : c == ',' ? (ushort)188 :
                        c == ';' ? (ushort)186 : c == '[' ? (ushort)219 : c == '\'' ? (ushort)222 : c == '`' ? (ushort)192 : (ushort)c;
                    Native.Input up = down; up.data.key.flags = Native.KEYEVENTF_KEYUP;
                    keys.Add(down); keys.Add(up);
                }
                if (holdShift[testIndex]) {
                    Native.Input shift = new Native.Input(); shift.type = 1; shift.data.key.vk = Native.VK_SHIFT; shift.data.key.flags = Native.KEYEVENTF_KEYUP;
                    keys.Add(shift);
                }
                if (shiftedPart) { result = 3; form.Close(); Application.ExitThread(); return; }
                uint sent = Native.SendInput((uint)keys.Count, keys.ToArray(), Marshal.SizeOf(typeof(Native.Input)));
                if (sent != keys.Count) {
                    Console.WriteLine("sendinput=" + sent + "/" + keys.Count + "; error=" + Marshal.GetLastWin32Error());
                    result = 3; form.Close(); Application.ExitThread(); return;
                }
                inspect.Interval = testIndex >= prepositionStart ? 700 : 1500;
                inspect.Start();
            };
            inspect.Tick += async delegate {
                inspect.Stop();
                if (testIndex == undoCase && !undoSent) {
                    if (box.Text != "привет ") { result = 1; form.Close(); Application.ExitThread(); return; }
                    undoSent = true;
                    if (!SendTestCopilotPause()) result = 3;
                    inspect.Start();
                    return;
                }
                if (testIndex >= firstManualCase && testIndex < prepositionStart && testIndex != 18 &&
                    manualStage < (testIndex == 20 ? 2 : 1)) {
                    string before = manualStage == 0 ? beforeManual[testIndex - firstManualCase] : "и";
                    if (box.Text != before) { result = 1; form.Close(); Application.ExitThread(); return; }
                    manualStage++;
                    if (!SendTestCopilotPause()) result = 3;
                    inspect.Start();
                    return;
                }
                if ((testIndex == typoUndoCase || testIndex == learningStart || testIndex == learningStart + 1) && manualStage == 0) {
                    if (!app.PopupFitsForTest) { result = 7; form.Close(); Application.ExitThread(); return; }
                    if (box.Text != "hello ") { result = 1; form.Close(); Application.ExitThread(); return; }
                    manualStage = 1;
                    if (!SendTestCopilotPause()) result = 3;
                    inspect.Start();
                    return;
                }
                TextBox targetBox = testIndex == passwordCase ? passwordBox : box;
                if (testIndex == typoUndoCase || testIndex == learningStart || testIndex == learningStart + 1 || testIndex == learningStart + 2) {
                    bool learned = app.HasCustomWordForTest("helllo", false);
                    bool expectedLearned = testIndex == learningStart + 1 || testIndex == learningStart + 2;
                    Console.WriteLine("learned=" + learned + " expected=" + expectedLearned);
                    if (learned != expectedLearned) result = 10;
                }
                if (testIndex == leadingCaseStart + 8 && app.PopupVisibleForTest) result = 11;
                if (testIndex == popupCase && !app.PopupVisibleForTest) result = 5;
                if (testIndex == popupCase && !app.PopupFitsForTest) result = 7;
                if (testIndex == popupCase && !app.PopupNoTaskbarForTest) result = 8;
                if (testIndex == capsCase && (Native.GetKeyState(20) & 1) != 0) result = 9;
                if (testIndex == popupCase) Console.WriteLine("popup=" + app.PopupBoundsForTest +
                    " field=" + box.RectangleToScreen(box.ClientRectangle));
                if (testIndex == passwordCase) {
                    Task<bool> passwordCheck = Task.Run(() => {
                        try { AutomationElement focused = AutomationElement.FocusedElement;
                            return focused != null && focused.Current.IsPassword; }
                        catch (Exception) { return false; }
                    });
                    if (await Task.WhenAny(passwordCheck, Task.Delay(1000)) != passwordCheck || !await passwordCheck) result = 4;
                }
                Console.WriteLine(physicalKeys[testIndex] + " => " + targetBox.Text);
                if (targetBox.Text != expectedText[testIndex]) result = 1;
                if (testIndex >= firstManualCase) {
                    uint threadId = Native.GetWindowThreadProcessId(form.Handle, IntPtr.Zero);
                    int currentLanguage = unchecked((ushort)Native.GetKeyboardLayout(threadId).ToInt64());
                    bool shouldBeRussian = testIndex >= prepositionStart ?
                        (testIndex >= typoUndoCase ? useRussianLayout[testIndex] : !useRussianLayout[testIndex]) :
                        testIndex == 16 || testIndex == 18 || testIndex == 21;
                    if (testIndex >= mixedCaseStart) shouldBeRussian = testIndex != mixedCaseStart + 2;
                    if (testIndex >= leadingCaseStart) shouldBeRussian = testIndex < leadingCaseStart + 2;
                    if (testIndex == leadingCaseStart + 4 || testIndex == leadingCaseStart + 5) shouldBeRussian = true;
                    if (testIndex >= shortExtraStart) shouldBeRussian = !useRussianLayout[testIndex];
                    if (testIndex == codeCase) shouldBeRussian = false;
                    if (testIndex == caseFixCase) shouldBeRussian = true;
                    if (testIndex == capsCase) shouldBeRussian = true;
                    if (testIndex >= itStart && testIndex < learningStart) shouldBeRussian = testIndex >= itStart + 2;
                    if (testIndex >= learningStart) shouldBeRussian = false;
                    if (TestMode) Console.WriteLine("language=" + currentLanguage.ToString("X4") + " expected=" + (shouldBeRussian ? "0419" : "0409"));
                    if (currentLanguage != (shouldBeRussian ? 0x0419 : 0x0409)) result = 6;
                }
                testIndex++;
                manualStage = 0;
                if (testIndex < physicalKeys.Length && testIndex - startIndex < caseLimit) send.Start();
                else { form.Close(); Application.ExitThread(); }
            };
            form.Shown += delegate { box.Focus(); send.Start(); };
            form.Show();
            Application.Run(app);
            send.Dispose(); inspect.Dispose(); form.Dispose();
            if (((Native.GetKeyState(20) & 1) != 0) != initialCaps) ToggleCapsForTest();
            return result;
        }

        private static int SelectionIntegrationTest(bool automation = false) {
            TestMode = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            TrayApp app = new TrayApp();
            app.ForceAutomationSelectionForTest = automation;
            Form form = new Form(); form.Text = "Selection conversion test"; form.Size = new Size(600, 180);
            TextBox box = new TextBox(); box.Dock = DockStyle.Fill; box.Multiline = true; form.Controls.Add(box);
            TextBox password = new TextBox(); password.Dock = DockStyle.Bottom; password.UseSystemPasswordChar = true;
            form.Controls.Add(password);
            Timer timer = new Timer(); timer.Interval = automation ? 1400 : 700;
            string[] original = { "prefix ghbdtn vbh suffix", "prefix привет мир suffix", "ghbdtn", "ghbdtn", "prefix ghbdtn\r\nvbh suffix" };
            string[] expected = { "prefix привет мир suffix", "prefix ghbdtn vbh suffix", "ghbdtn", "ghbdtn", "prefix привет\r\nмир suffix" };
            int index = 0, stage = 0, result = 0;
            Action prepare = delegate {
                app.ResetContextForTest();
                TextBox target = index == 2 ? password : box;
                form.Activate(); form.ActiveControl = target; target.Select(); target.Text = original[index];
                target.Select(index == 0 || index == 1 || index == 4 ? 7 : 0,
                    index == 3 ? 0 : index == 0 || index == 1 ? 10 : index == 4 ? 11 : target.TextLength);
                target.Focus();
                if (!target.Focused || Native.GetForegroundWindow() != form.Handle) {
                    Console.WriteLine("selection test focus missing: control=" + target.Focused + " foreground=" + (Native.GetForegroundWindow() == form.Handle) + " visible=" + target.Visible + " enabled=" + target.Enabled + " canFocus=" + target.CanFocus + " bounds=" + target.Bounds);
                    result = 2; form.Close(); Application.ExitThread(); return;
                }
                if (!SendTestCopilotPause()) result = 3;
                timer.Start();
            };
            form.Shown += delegate { Native.SetForegroundWindow(form.Handle); timer.Start(); };
            timer.Tick += delegate {
                timer.Stop();
                if (stage == 0) { stage = 1; prepare(); return; }
                string wanted = stage == 2 ? original[index] : expected[index];
                TextBox target = index == 2 ? password : box;
                Console.WriteLine("selection case=" + index + " undo=" + (stage == 2) + " match=" + (target.Text == wanted));
                if (target.Text != wanted) {
                    Console.WriteLine("actual=" + target.Text + " expected=" + wanted + " selection=" + target.SelectionStart + ":" + target.SelectionLength);
                    result = 1; form.Close(); Application.ExitThread(); return;
                }
                if (stage == 1 && (index == 0 || index == 1 || index == 4)) {
                    stage = 2; if (!SendTestCopilotPause()) result = 3; timer.Start(); return;
                }
                index++; stage = 1;
                if (index == original.Length) { form.Close(); Application.ExitThread(); }
                else prepare();
            };
            form.Show(); Application.Run(app);
            timer.Dispose(); form.Dispose(); return result;
        }

        private static int SelfTest() {
            TestMode = true;
            try { RuntimeChecks.Run(); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
            ITDictionaryTests.Run();
            if (!TrayApp.CustomWordsRoundTripForTest()) return 1;
            if (!TrayApp.IsCodeAppForTest("Code") || !TrayApp.IsCodeAppForTest("pwsh") ||
                TrayApp.IsCodeAppForTest("chrome")) return 1;
            string[] codeInput = { "текст", "`код", "`код` текст", "```\nкод", "```\nкод\n```\nтекст" };
            bool[] codeExpected = { false, true, false, true, false };
            for (int i = 0; i < codeInput.Length; i++)
                if (TrayApp.InsideMarkdownCodeForTest(codeInput[i]) != codeExpected[i]) return 1;
            string[] caseInput = { "пРИВЕТ", "правописанИЕ", "macOS", "HELLO", "привет", "пРИвЕТ" };
            string[] caseExpected = { "Привет", "правописание", null, null, null, null };
            for (int i = 0; i < caseInput.Length; i++) {
                string actual = TrayApp.CaseCorrectionForTest(caseInput[i], delegate(string word) {
                    return word == "Привет" || word == "правописание";
                });
                if (actual != caseExpected[i]) {
                    Console.WriteLine("case " + caseInput[i] + " => " + actual + ", expected " + caseExpected[i]);
                    return 1;
                }
            }
            string[] typed = { "helo", "teh", "wrdo", "becuase", "HELO", "нормлаьно", "работет", "kubernetes", "helllo" };
            string[] suggestions = { "help", "the", "world", "because", "HELP", "нормально", "работает", null, "hello" };
            string[] corrected = { "help", null, null, "because", null, "нормально", "работает", null, "hello" };
            for (int i = 0; i < typed.Length; i++) {
                string actual = TrayApp.TypoCorrection(typed[i], suggestions[i]);
                if (actual != corrected[i]) {
                    Console.WriteLine("typo " + typed[i] + " => " + actual + ", expected " + corrected[i]);
                    return 1;
                }
            }
            using (WindowsSpeller speller = new WindowsSpeller()) {
                string[] compoundWords = { "кто-нибуть", "well-knwon", "don'tt", "don’tt", "'don'tt'" };
                string[] compoundExpected = { "кто-нибудь", "well-known", "don't", "don’t", "'don't'" };
                for (int i = 0; i < compoundWords.Length; i++) {
                    bool ru = i == 0;
                    string actual = TrayApp.SelectTypoCorrection(compoundWords[i], ru,
                        delegate(string word) { return speller.IsCorrect(word, ru); },
                        speller.TypoSuggestions(TrayApp.TypoCore(compoundWords[i]).ToLowerInvariant(), ru, 8));
                    Console.WriteLine("Compound dictionary " + i + " match=" + (actual == compoundExpected[i]));
                    if (actual != compoundExpected[i]) return 1;
                }
                string[] typoWords = { "лючек", "крючек", "значек", "пучек", "червячек", "мешечек", "горшечек", "мячек", "ключек", "Лючек", "ЛЮЧЕК", "скачек", "замочек", "цветочек", "человечек", "платочек", "сыночек" };
                string[] typoExpected = { "лючок", "крючок", "значок", "пучок", "червячок", "мешочек", "горшочек", "мячик", "ключик", "Лючок", null, null, null, null, null, null, null };
                for (int i = 0; i < typoWords.Length; i++) {
                    string actual = TrayApp.SelectTypoCorrection(typoWords[i], true,
                        delegate(string word) { return speller.IsCorrect(word, true); }, speller.Suggestions(typoWords[i].ToLowerInvariant(), true, 8));
                    Console.WriteLine("RU typo " + i + " match=" + (actual == typoExpected[i]));
                    if (actual != typoExpected[i]) return 1;
                }
                string[] words = { "привет", "превед", "hello", "helllo", "ghbdtn", "руддщ" };
                bool[] expected = { true, false, true, false, false, false };
                bool[] languages = { true, true, false, false, false, true };
                for (int i = 0; i < words.Length; i++) {
                    bool actual = speller.IsCorrect(words[i], languages[i]);
                    Console.WriteLine(words[i] + ": " + actual);
                    if (actual != expected[i]) return 1;
                }
                return 0;
            }
        }

        [STAThread] static int Main(string[] args) {
            try {
                if (args.Length == 1 && (args[0] == "--menu-test-ru" || args[0] == "--menu-test-en" || args[0] == "--menu-test-fallback")) {
                    System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(
                        args[0] == "--menu-test-ru" ? "ru-RU" : args[0] == "--menu-test-en" ? "en-US" : "de-DE");
                    return TrayMenuTests.Run();
                }
                if (args.Length == 1 && args[0] == "--dpi-test") return UiDpiChecks.Run();
                if (args.Length == 1 && args[0] == "--copilot-test") return CopilotChecks.Run();
                if (args.Length == 1 && args[0] == "--correction-key-test") return CorrectionKeyChecks.Run();
                if (args.Length == 1 && args[0] == "--typo-ending-test") return CorrectionKeyChecks.Run(true);
                if (args.Length == 1 && args[0] == "--compound-typo-test") return CorrectionKeyChecks.Run(false, true);
                if (args.Length == 1 && args[0] == "--fn-test") return FnKeyChecks.Run();
                if (args.Length == 1 && args[0] == "--fn-stress-test") return FnKeyChecks.Run(true);
                if (args.Length == 1 && args[0] == "--punctuation-test") return PunctuationChecks.Run();
                if (args.Length == 1 && args[0] == "--startup-test") return StartupRegistration.Check();
                if (args.Length == 1 && args[0] == "--self-test") return SelfTest();
                if (args.Length == 1 && args[0] == "--hint-test") return HintDesignTests.Run();
                if (args.Length == 1 && args[0] == "--menu-test") return TrayMenuTests.Run();
                if (args.Length == 1 && args[0] == "--integration-test-selection") return SelectionIntegrationTest();
                if (args.Length == 1 && args[0] == "--integration-test-selection-uia") return SelectionIntegrationTest(true);
                if (args.Length == 1 && args[0] == "--dictionary-ui-test") return ITDictionaryTests.RunWindowTest();
                if (args.Length == 1 && args[0] == "--integration-test") return IntegrationTest(0);
                if (args.Length == 1 && args[0] == "--integration-test-short") return IntegrationTest(30);
                if (args.Length == 1 && args[0] == "--integration-test-password") return IntegrationTest(15);
                if (args.Length == 1 && args[0] == "--integration-test-typo") return IntegrationTest(46);
                if (args.Length == 1 && args[0] == "--integration-test-mixed") return IntegrationTest(49);
                if (args.Length == 1 && args[0] == "--integration-test-leading") return IntegrationTest(52);
                if (args.Length == 1 && args[0] == "--integration-test-apostrophe") return IntegrationTest(56, 6);
                if (Process.GetProcessesByName("punto").Length > 0) {
                    MessageBox.Show(UiText.Get("Закройте Punto Switcher через его значок в области уведомлений, затем запустите Rasklad.", "Close Punto Switcher from its tray icon, then start Rasklad."), AppBranding.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 2;
                }
                bool firstInstance;
                // Keep the previous mutex ID to prevent older copies from running alongside Rasklad.
                using (Mutex mutex = new Mutex(false, "Local\\PuntoWinSpeller", out firstInstance)) {
                    if (!firstInstance) {
                        MessageBox.Show(UiText.Get("Rasklad уже запущен.", "Rasklad is already running."), AppBranding.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return 3;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    TrayApp app = new TrayApp();
                    Application.Run(app);
                    return 0;
                }
            } catch (Exception ex) {
                MessageBox.Show(ex.Message, AppBranding.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}

