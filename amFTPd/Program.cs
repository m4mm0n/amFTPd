/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  File:           Program.cs
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15 16:36:40
 *  Last Modified:  2025-12-14 21:55:50
 *  CRC32:          0x68995DB6
 *  
 *  Description:
 *      Represents the entry point of the amFTPd application, a managed FTP daemon.
 * 
 *  License:
 *      MIT License
 *      https://opensource.org/licenses/MIT
 *
 *  Notes:
 *      Please do not use for illegal purposes, and if you do use the project please refer to the original author.
 * ====================================================================================================
 */


using amFTPd.Config.Daemon;
using amFTPd.Core;
using amFTPd.Logging;
using amFTPd.Properties;
using amFTPd.Utils;
using System.Reflection;

namespace amFTPd
{
    /// <summary>
    /// Represents the entry point of the amFTPd application, a managed FTP daemon.
    /// </summary>
    /// <remarks>This class initializes the FTP server, loads the configuration, sets up logging, and handles
    /// the server lifecycle, including graceful shutdown on cancellation requests.</remarks>
    internal class Program
    {
        static async Task Main(string[] args)
        {
            PrintBanner();

            var logger = new CombinedFtpLogger(new ConsoleFtpLogger(),
                new FileFtpLogger(new FileFtpLoggerOptions
                { FilePath = "logs/amftpd.log", MinLevel = FtpLogLevel.Trace }));
            var configFile = args.Length > 0 ? args[0] : "amftpd.json";

            var runtime = await AmFtpdConfigLoader.LoadAsync(configFile, logger);

            var server = new FtpServer(runtime, logger);

            var serverTask = server.StartAsync();


            Console.CancelKeyPress += async (_, e) =>
            {
                e.Cancel = true;
                logger.Log(FtpLogLevel.Info, "[amFTPd] Shutdown requested, stopping server...");
                server.Stop();
            };

            try
            {
                await serverTask;
            }
            catch (Exception ex)
            {
                logger.Log(FtpLogLevel.Error, "Server crashed", ex);
            }

            logger.Log(FtpLogLevel.Info, "[amFTPd] Server stopped.");
            logger.Dispose();
        }

        static void PrintBanner()
        {
            AnsiConsoleImage.WriteImage(Resources.amftpd_logo);
            var ver = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            $"amFTPd - a managed FTP daemon v{ver}".WriteBoxedBanner();
            Console.Title = $"amFTPd - a managed FTP daemon v{ver}";
            "Press Ctrl+C to stop.\n".WriteStyledLogLine();
        }
    }
}
