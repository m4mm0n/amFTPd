/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-20
 *  
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original
 *      author.
 * ====================================================================================================
 */

using System.Text;
using System.Text.RegularExpressions;

namespace amFTPd.Utils
{
    public static class ConsoleRenderer
    {
        private static readonly Regex AnsiRegex =
            new Regex(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        private static readonly string[] Hellfire =
        {
        "\x1b[30m",    // black
        "\x1b[31;2m",  // dark red
        "\x1b[31m",    // red
        "\x1b[91m",    // bright red
        "\x1b[33m",    // yellow
    };

        private static readonly string[] BorderCycle =
        {
        "\x1b[31m","\x1b[91m","\x1b[33m","\x1b[93m",
        "\x1b[92m","\x1b[96m","\x1b[94m","\x1b[95m"
    };

        public static void RunHellfireHeader(string[] lines)
        {
            int height = lines.Length + 2; // box overhead

            while (true)
            {
                int frame = (Environment.TickCount / 100) % 9999;

                DrawFrame(lines, frame);

                Thread.Sleep(60); // ~16 FPS
            }
        }

        private static void DrawFrame(string[] lines, int frame)
        {
            int width = Console.WindowWidth - 2;
            int borderColorIndex = frame % BorderCycle.Length;
            string bc = BorderCycle[borderColorIndex];

            int startY = 0;

            // Top border
            WriteAt(0, startY++, bc + "╔" + new string('═', width) + "╗" + "\x1b[0m");

            // Banner lines
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = lines[i];
                string fire = GenerateFire(raw, frame);

                int padLeft = (width - raw.Length) / 2;
                if (padLeft < 0) padLeft = 0;

                string line =
                    bc + "║" + "\x1b[0m" +
                    new string(' ', padLeft) +
                    fire +
                    new string(' ', width - padLeft - raw.Length) +
                    bc + "║" + "\x1b[0m";

                WriteAt(0, startY++, line);
            }

            // Bottom border
            WriteAt(0, startY, bc + "╚" + new string('═', width) + "╝" + "\x1b[0m");
        }

        private static string GenerateFire(string text, int seed)
        {
            var rnd = new Random(seed * 1337 + text.Length);
            var sb = new StringBuilder();

            foreach (char c in text)
            {
                string col = Hellfire[rnd.Next(Hellfire.Length)];
                sb.Append(col).Append(c);
            }
            sb.Append("\x1b[0m");
            return sb.ToString();
        }

        private static void WriteAt(int x, int y, string text)
        {
            try
            {
                Console.SetCursorPosition(x, y);
                Console.Write(text);
            }
            catch
            {
                // Console resized or no TTY — ignore
            }
        }
    }

    public static class ConsoleBanner
    {
        // Matches ANSI escape sequences like \x1b[31m, \x1b[1;32m, etc.
        private static readonly Regex AnsiRegex =
            new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        public static void WriteBoxedBanner(params string[] lines)
        {
            if (lines == null || lines.Length == 0)
            {
                return;
            }

            // Get visible length (without ANSI) of each line
            var maxVisibleLen = lines.Select(line => AnsiRegex.Replace(line ?? string.Empty, "")).Select(visible => visible.Length).Prepend(0).Max();

            var padding = 2; // spaces left/right inside box
            var innerWidth = maxVisibleLen + padding * 2;
            var totalWidth = innerWidth + 2; // + borders

            var consoleWidth = Console.WindowWidth;
            var leftPad = Math.Max((consoleWidth - totalWidth) / 2, 0);

            void WriteAtLeft(string text)
            {
                Console.SetCursorPosition(leftPad, Console.CursorTop);
                Console.WriteLine(text);
            }

            // Top border
            WriteAtLeft("╔" + new string('═', innerWidth) + "╗");

            // Content lines
            foreach (var line in lines)
            {
                var raw = line ?? string.Empty;
                var visible = AnsiRegex.Replace(raw, "");

                var extraSpaces = maxVisibleLen - visible.Length;
                var rightSpaces = padding + extraSpaces;
                var leftSpaces = padding;

                WriteAtLeft("║"
                            + new string(' ', leftSpaces)
                            + raw
                            + new string(' ', rightSpaces)
                            + "║");
            }

            // Bottom border
            WriteAtLeft("╚" + new string('═', innerWidth) + "╝");
        }
    }
}
