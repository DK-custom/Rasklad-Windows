using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

[assembly: AssemblyTitle("Rasklad")]
[assembly: AssemblyProduct("Rasklad")]
[assembly: AssemblyDescription("Русско-английская раскладка и правописание Windows")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.30.1")]

namespace RaskladWindows {
    static class AppBranding {
        internal const string Name = "Rasklad";
        internal static readonly Icon TrayIcon = LoadIcon(SystemInformation.SmallIconSize, "Tray.Icon");
        internal static readonly Icon WindowIcon = LoadIcon(SystemInformation.IconSize);
        private static Icon LoadIcon(Size size, string resource = "App.Icon") {
            using (Stream stream = typeof(AppBranding).Assembly.GetManifestResourceStream(resource)) {
                if (stream == null) throw new InvalidOperationException("Значок Rasklad не найден в приложении.");
                return new Icon(stream, size);
            }
        }
        internal static void Check() {
            using (Stream stream = typeof(AppBranding).Assembly.GetManifestResourceStream("Tray.Icon"))
            using (BinaryReader reader = new BinaryReader(stream)) {
                int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
                if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != sizes.Length)
                    throw new Exception("Invalid dedicated tray ICO header");
                foreach (int size in sizes) {
                    int width = reader.ReadByte(), height = reader.ReadByte();
                    reader.ReadUInt16(); int planes = reader.ReadUInt16(), bpp = reader.ReadUInt16();
                    uint length = reader.ReadUInt32(), offset = reader.ReadUInt32();
                    if ((width == 0 ? 256 : width) != size || (height == 0 ? 256 : height) != size ||
                        planes != 1 || bpp != 32 || offset + length > stream.Length)
                        throw new Exception("Tray ICO frame invalid: " + size);
                }
            }
            using (Icon original = LoadIcon(SystemInformation.SmallIconSize))
            using (Bitmap expected = original.ToBitmap())
            using (Bitmap actual = TrayIcon.ToBitmap()) {
                if (expected.Size != actual.Size) throw new Exception("Tray icon size changed");
                for (int y = 0; y < actual.Height; y++) for (int x = 0; x < actual.Width; x++)
                    if (actual.GetPixel(x, y) != expected.GetPixel(x, y)) throw new Exception("Original Rasklad artwork changed");
                actual.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Rasklad-tray-preview.png"));
            }
            Console.WriteLine("dedicated tray ICO: 8 frames, 32bpp, original purple artwork unchanged PASS");
            AssemblyProductAttribute product = (AssemblyProductAttribute)Attribute.GetCustomAttribute(typeof(AppBranding).Assembly, typeof(AssemblyProductAttribute));
            if (product == null || product.Product != Name) throw new Exception("Product name mismatch");
            foreach (Icon icon in new[] { TrayIcon, WindowIcon }) {
                using (Bitmap bitmap = icon.ToBitmap()) {
                    if (bitmap.Width < 16 || bitmap.GetPixel(0, 0).A != 0 || bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2).A == 0)
                        throw new Exception("Rasklad icon transparency or content invalid");
                    int left = bitmap.Width, right = -1;
                    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                        if (bitmap.GetPixel(x, y).A > 16) { left = Math.Min(left, x); right = Math.Max(right, x); }
                    if (left > 1 || right < bitmap.Width - 2) throw new Exception("Icon has excessive transparent margins");
                }
            }
            Console.WriteLine("Rasklad product and transparent tray/window icons PASS");
        }
    }
}
