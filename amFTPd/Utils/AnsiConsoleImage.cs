/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           AnsiConsoleImage.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-12-02 05:07:13
 *  Last Modified:  2025-12-13 04:46:22
 *  CRC32:          0xD956CD5C
 *  
 *  Description:
 *      Enables ANSI/VT on Windows; no-op on other OSes. Safe to call multiple times.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using Image = SixLabors.ImageSharp.Image;

namespace amFTPd.Utils
{
    public static class AnsiConsoleImage
    {
        // Windows-only VT flags
        private const int STD_OUTPUT_HANDLE = -11;
        private const int ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004;

        // P/Invoke declarations – safe as long as we only call them on Windows.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out int lpMode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, int dwMode);

        private static bool _vtInitialized;

        /// <summary>
        /// Enables ANSI/VT on Windows; no-op on other OSes.
        /// Safe to call multiple times.
        /// </summary>
        public static void EnableVirtualTerminalProcessing()
        {
            if (_vtInitialized)
                return;

            _vtInitialized = true;

            // Make sure UTF-8 output is used everywhere
            try
            {
                Console.OutputEncoding = Encoding.UTF8;
            }
            catch
            {
                // ignore
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return;

            try
            {
                var handle = GetStdHandle(STD_OUTPUT_HANDLE);
                if (handle == IntPtr.Zero) return;

                if (!GetConsoleMode(handle, out var mode)) return;

                mode |= ENABLE_VIRTUAL_TERMINAL_PROCESSING;
                SetConsoleMode(handle, mode);
            }
            catch
            {
                // If this fails, ANSI may still work (Windows Terminal, etc.)
            }
        }

        /// <summary>
        /// Load and render an image (PNG/JPEG/etc.) to the console at full console width.
        /// </summary>
        public static void WriteImage(string filePath, int? maxWidth = null)
        {
            using var img = Image.Load<Rgba32>(filePath);
            WriteImage(img, maxWidth);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <param name="maxWidth"></param>
        public static void WriteImage(Stream file, int? maxWidth = null)
        {
            using var img = Image.Load<Rgba32>(file);
            WriteImage(img, maxWidth);
        }

        public static void WriteImage(Bitmap image, int? maxWidth = null)
        {
#pragma warning disable CA1416
            using var img = Image.Load<Rgba32>(BitmapToBytes(image, ImageFormat.Png));
#pragma warning restore CA1416
            WriteImage(img, maxWidth);
        }

        static byte[] BitmapToBytes(Bitmap bitmap, ImageFormat format)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            if (format == null) throw new ArgumentNullException(nameof(format));

            using var ms = new MemoryStream();
#pragma warning disable CA1416
            bitmap.Save(ms, format);
#pragma warning restore CA1416
            return ms.ToArray();
        }

        /// <summary>
        /// Render an Image&lt;Rgba32&gt; to the console at full console width (or maxWidth),
        /// using 24-bit ANSI color and upper-half block characters.
        /// Does NOT dispose the passed image.
        /// </summary>
        public static void WriteImage(Image<Rgba32> img, int? maxWidth = null)
        {
            if (img == null) throw new ArgumentNullException(nameof(img));

            EnableVirtualTerminalProcessing();

            var consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            var targetCharWidth = maxWidth.HasValue ? Math.Min(maxWidth.Value, consoleWidth) : consoleWidth;
            if (targetCharWidth <= 0) targetCharWidth = 80;

            // Don't upscale: if the image is already narrower, use its own width.
            targetCharWidth = Math.Min(targetCharWidth, img.Width);

            // Scale image so that its width == targetCharWidth.
            // Height is scaled proportionally.
            var scale = (double)targetCharWidth / img.Width;
            var scaledWidth = targetCharWidth;
            var scaledHeight = Math.Max(1, (int)Math.Round(img.Height * scale));

            using var scaled = img.Clone();
            scaled.Mutate(ctx => ctx.Resize(scaledWidth, scaledHeight));

            // Each console row shows two image rows (top in FG, bottom in BG) using '▀'.
            var charRows = (scaledHeight + 1) / 2; // round up

            var sb = new StringBuilder();

            for (var yChar = 0; yChar < charRows; yChar++)
            {
                sb.Clear();

                var yTop = yChar * 2;
                var yBottom = yTop + 1;

                if (yTop >= scaledHeight) yTop = scaledHeight - 1;
                if (yBottom >= scaledHeight) yBottom = scaledHeight - 1;

                for (var x = 0; x < scaledWidth; x++)
                {
                    var top = scaled[x, yTop];
                    var bottom = scaled[x, yBottom];

                    // Treat transparent-ish pixels as black
                    if (top.A < 128) top = new Rgba32(0, 0, 0, 255);
                    if (bottom.A < 128) bottom = new Rgba32(0, 0, 0, 255);

                    sb.Append($"\u001b[38;2;{top.R};{top.G};{top.B}m");       // FG
                    sb.Append($"\u001b[48;2;{bottom.R};{bottom.G};{bottom.B}m"); // BG
                    sb.Append('▀');
                }

                sb.Append("\u001b[0m");
                Console.WriteLine(sb.ToString());
            }

            Console.Write("\u001b[0m");
        }

        /// <summary>
        /// Render the image at a percentage of the current console width.
        /// Example: 50f = 50% of Console.WindowWidth.
        /// </summary>
        public static void WriteImageScaled(string filePath, float percentageOfConsoleWidth)
        {
            using var img = Image.Load<Rgba32>(filePath);
            WriteImageScaled(img, percentageOfConsoleWidth);
        }

        /// <summary>
        /// Render an existing image at a percentage of console width.
        /// Does NOT dispose the passed image.
        /// </summary>
        public static void WriteImageScaled(Image<Rgba32> img, float percentageOfConsoleWidth)
        {
            if (img == null) throw new ArgumentNullException(nameof(img));
            if (percentageOfConsoleWidth <= 0f || percentageOfConsoleWidth > 100f)
                throw new ArgumentOutOfRangeException(nameof(percentageOfConsoleWidth), "Percentage must be in (0, 100].");

            var consoleWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            var targetWidth = (int)Math.Max(1, Math.Round(consoleWidth * (percentageOfConsoleWidth / 100f)));

            WriteImage(img, targetWidth);
        }
    }
}
