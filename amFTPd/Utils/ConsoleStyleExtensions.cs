/* ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           ConsoleStyleExtensions.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-13 04:32:32
 *  CRC32:          0x1E33AF9D
 *  
 *  Description:
 *      Converts a raw log line ([ts] LEVEL: msg) into an ANSI-styled one.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ==================================================================================================== */







using System.Text;
using System.Text.RegularExpressions;

namespace amFTPd.Utils
{
    public static class ConsoleStylingExtensions
    {
        // ─────────────────────────────────────────────
        //  ANSI constants
        // ─────────────────────────────────────────────
        private const string Reset = "\x1b[0m";
        private const string Bold = "\x1b[1m";
        private const string Dim = "\x1b[2m";
        private const string Italic = "\x1b[3m";

        private const string FgBlack = "\x1b[30m";
        private const string FgRed = "\x1b[31m";
        private const string FgGreen = "\x1b[32m";
        private const string FgYellow = "\x1b[33m";
        private const string FgBlue = "\x1b[34m";
        private const string FgMagenta = "\x1b[35m";
        private const string FgCyan = "\x1b[36m";
        private const string FgWhite = "\x1b[37m";
        private const string FgGray = "\x1b[90m";
        private const string FgBrightRed = "\x1b[91m";
        private const string FgBrightGreen = "\x1b[92m";

        // Strip ANSI escape sequences for width calculations
        private static readonly Regex AnsiRegex =
            new(@"\x1B\[[0-9;]*[A-Za-z]", RegexOptions.Compiled);

        // [timestamp] LEVEL: message
        private static readonly Regex LogRegex =
            new(@"^\[(?<ts>.+?)\]\s+(?<level>[A-Z]+):\s*(?<msg>.*)$",
                RegexOptions.Compiled);

        // Tokens inside the message we want to emphasize (italic)
        private static readonly Regex MessageTokenRegex =
            new(@"\b(CMD:|ARG:|USER\b|PASS\b|DATA\([^)]+\):|Connection(?: closed)?:|Client connected\.?|Listening on\b|Waiting for incoming data connection\.\.\.)",
                RegexOptions.Compiled);

        // ─────────────────────────────────────────────
        //  Public: styled log line
        // ─────────────────────────────────────────────

        /// <summary>
        /// Converts a raw log line ([ts] LEVEL: msg) into an ANSI-styled one.
        /// </summary>
        public static string ToStyledLog(this string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return string.Empty;

            var m = LogRegex.Match(line);
            if (!m.Success)
            {
                // Not in our format – still highlight known tokens if any.
                return StyleMessageTokens(line);
            }

            var ts = m.Groups["ts"].Value;
            var level = m.Groups["level"].Value;
            var msg = m.Groups["msg"].Value;

            var levelColor = GetLevelColor(level);

            var tsStyled = $"{Dim}[{ts}]{Reset}";
            var levelStyled = $"{Bold}{levelColor}{level}{Reset}";
            var msgStyled = StyleMessageTokens(msg);

            return $"{tsStyled} {levelStyled}: {msgStyled}{Reset}";
        }

        /// <summary>
        /// Writes the line to the console as a styled log.
        /// </summary>
        public static void WriteStyledLogLine(this string line)
            => Console.WriteLine(line.ToStyledLog());

        // ─────────────────────────────────────────────
        //  Public: ANSI-aware centering
        // ─────────────────────────────────────────────

        /// <summary>
        /// Writes a line centered in the current console width, ANSI-aware.
        /// </summary>
        public static void WriteCenteredAnsi(this string text)
        {
            if (text == null)
            {
                Console.WriteLine();
                return;
            }

            var visible = AnsiRegex.Replace(text, "");
            var width = SafeWindowWidth();
            var leftPadding = Math.Max((width - visible.Length) / 2, 0);

            if (leftPadding > 0 && Console.CursorLeft != leftPadding)
            {
                try { Console.SetCursorPosition(leftPadding, Console.CursorTop); }
                catch { /* redirected output / no TTY, just fall back */ }
            }

            Console.WriteLine(text);
        }

        // HELLFIRE palette (16-color terminal-safe)
        private static readonly string[] HellfireColors =
        [
            "\x1b[30m",     // black
    "\x1b[31;2m",   // dark red
    "\x1b[31m",     // red
    "\x1b[91m",     // bright red
    "\x1b[33m" // yellow
        ];

        // Cracktro-style cycling border colors
        private static readonly string[] DemoBorderColors =
        [
            "\x1b[31m", "\x1b[91m", "\x1b[33m", "\x1b[93m",
    "\x1b[92m", "\x1b[96m", "\x1b[94m", "\x1b[95m"
        ];


        /// <summary>
        /// Draw one HELLFIRE animation frame inside a colored animated box.
        /// </summary>
        public static void WriteHellfireFrame(this IEnumerable<string> lines, int frame)
        {
            var arr = lines.ToArray();
            var maxLen = arr.Max(l => AnsiRegex.Replace(l, "").Length);

            var pad = 2;
            var innerWidth = maxLen + pad * 2;

            var frameColorIndex = frame % DemoBorderColors.Length;
            var borderColor = DemoBorderColors[frameColorIndex];

            // Top border
            WriteCentered(borderColor + "╔" + new string('═', innerWidth) + "╗" + "\x1b[0m");

            // Render each line with flickering fire
            foreach (var raw in arr)
            {
                var visible = AnsiRegex.Replace(raw, "");
                var extra = maxLen - visible.Length;

                var fireLine = GenerateHellfireText(raw, frame);

                var full =
                    borderColor + "║" + "\x1b[0m" +
                    new string(' ', pad) +
                    fireLine +
                    new string(' ', pad + extra) +
                    borderColor + "║" + "\x1b[0m";

                WriteCentered(full);
            }

            // Bottom border
            WriteCentered(borderColor + "╚" + new string('═', innerWidth) + "╝" + "\x1b[0m");
        }


        /// <summary>
        /// Animate hellfire flickering banner indefinitely.
        /// </summary>
        public static void WriteHellfireBanner(this IEnumerable<string> lines, int fps = 16)
        {
            Console.CursorVisible = false;
            var delay = 1000 / fps;
            var frame = 0;

            while (true)
            {
                Console.SetCursorPosition(0, 0);
                lines.WriteHellfireFrame(frame++);
                Thread.Sleep(delay);
            }
        }


        /// <summary>
        /// Generate flickering text using hellfire palette.
        /// </summary>
        private static string GenerateHellfireText(string line, int seed)
        {
            var rnd = new Random(seed * 7919 + line.Length * 131);

            var output = new StringBuilder();
            foreach (var c in line)
            {
                var col = HellfireColors[rnd.Next(HellfireColors.Length)];
                output.Append(col).Append(c);
            }
            output.Append("\x1b[0m");
            return output.ToString();
        }


        /// <summary>
        /// ANSI-aware centered line writer (same as your existing one)
        /// </summary>
        private static void WriteCentered(string text)
        {
            var clean = AnsiRegex.Replace(text, "");
            var left = Math.Max((Console.WindowWidth - clean.Length) / 2, 0);

            try { Console.SetCursorPosition(left, Console.CursorTop); }
            catch { }

            Console.WriteLine(text);
        }


        // ─────────────────────────────────────────────
        //  Public: boxed banner (centered, ANSI-aware)
        // ─────────────────────────────────────────────

        /// <summary>
        /// Writes a centered box around the given lines (ANSI-aware).
        /// </summary>
        public static void WriteBoxedBanner(this IEnumerable<string> lines)
        {
            if (lines == null) return;

            var arr = lines.ToArray();
            if (arr.Length == 0) return;

            var maxVisibleLen = 0;
            foreach (var l in arr)
            {
                var visible = AnsiRegex.Replace(l ?? string.Empty, "");
                if (visible.Length > maxVisibleLen)
                    maxVisibleLen = visible.Length;
            }

            var padding = 2;
            var innerWidth = maxVisibleLen + padding * 2;

            var top = "╔" + new string('═', innerWidth) + "╗";
            var bottom = "╚" + new string('═', innerWidth) + "╝";

            top.WriteCenteredAnsi();
            foreach (var raw in arr)
            {
                var line = raw ?? string.Empty;
                var visible = AnsiRegex.Replace(line, "");

                var extraSpaces = maxVisibleLen - visible.Length;
                var leftSpaces = padding;
                var rightSpaces = padding + extraSpaces;

                var boxed =
                    "║" +
                    new string(' ', leftSpaces) +
                    line +
                    new string(' ', rightSpaces) +
                    "║";

                boxed.WriteCenteredAnsi();
            }
            bottom.WriteCenteredAnsi();
        }

        /// <summary>
        /// Convenience overload for a single-line banner.
        /// </summary>
        public static void WriteBoxedBanner(this string line)
            => new[] { line }.WriteBoxedBanner();

        // ─────────────────────────────────────────────
        //  Internals
        // ─────────────────────────────────────────────

        private static string GetLevelColor(string level)
        {
            return level switch
            {
                "TRACE" => FgGray,
                "DEBUG" => FgCyan,
                "VERBOSE" => FgCyan,
                "INFO" => FgGreen,
                "NOTICE" => FgBlue,
                "WARN" => FgYellow,
                "WARNING" => FgYellow,
                "ERROR" => FgRed,
                "ERR" => FgRed,
                "FATAL" => FgBrightRed,
                "CRIT" => FgBrightRed,
                "SUCCESS" => FgBrightGreen,
                "OK" => FgBrightGreen,
                _ => FgWhite
            };
        }

        private static string StyleMessageTokens(string msg)
        {
            if (string.IsNullOrEmpty(msg))
                return msg ?? string.Empty;

            return MessageTokenRegex.Replace(
                msg,
                m => $"{Italic}{m.Value}{Reset}");
        }

        private static int SafeWindowWidth()
        {
            try
            {
                return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
            }
            catch
            {
                // In case of redirected output / non-interactive host
                return 80;
            }
        }
    }
}
