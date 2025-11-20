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

using System.Text.RegularExpressions;

namespace amFTPd.Utils
{
    internal static class ConsoleExt
    {
        /// <summary>
        /// Writes the specified text to the console, centered within the current console window width.
        /// </summary>
        /// <remarks>This method calculates the visible width of the text, excluding any ANSI escape
        /// codes, to determine the appropriate padding for centering. The text is then written with its original
        /// formatting preserved.</remarks>
        /// <param name="text">The text to write to the console. If <paramref name="text"/> is <see langword="null"/> or empty, a blank
        /// line is written instead.</param>
        public static void WriteLineCentered(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine();
                return;
            }

            // Strip ANSI codes for width calculation
            var visible = new Regex(@"\x1B\[[0-9;]*[A-Za-z]",
                RegexOptions.Compiled).Replace(text, "");

            var width = Console.WindowWidth;
            var leftPadding = Math.Max((width - visible.Length) / 2, 0);

            Console.SetCursorPosition(leftPadding, Console.CursorTop);
            Console.WriteLine(text); // print with ANSI intact
        }
        /// <summary>
        /// Writes the specified text to the console, centered within the current console window width.
        /// </summary>
        /// <remarks>The method calculates the visible width of the text by stripping ANSI escape codes,
        /// ensuring proper centering. The text is written with its original formatting, including any ANSI
        /// codes.</remarks>
        /// <param name="text">The text to write to the console. If the text is <see langword="null"/> or empty, the method does nothing.</param>
        public static void WriteCentered(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            // Strip ANSI codes for width calculation
            var visible = new Regex(@"\x1B\[[0-9;]*[A-Za-z]",
                RegexOptions.Compiled).Replace(text, "");
            var width = Console.WindowWidth;
            var leftPadding = Math.Max((width - visible.Length) / 2, 0);
            Console.SetCursorPosition(leftPadding, Console.CursorTop);
            Console.Write(text); // print with ANSI intact
        }

    }
}
