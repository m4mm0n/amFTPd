/*
 * ====================================================================================================
 *  Project:        amFTPd - a managed FTP daemon
 *  Author:         Geir Gustavsen, ZeroLinez Softworx
 *  Created:        2025-11-15
 *  Last Modified:  2025-11-23
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


using amFTPd.Config.Daemon;
using amFTPd.Core;
using amFTPd.Logging;
using System.Reflection;
using amFTPd.Utils;

namespace amFTPd
{
    /// <summary>
    /// Represents the entry point of the amFTPd application, a managed FTP daemon.
    /// </summary>
    /// <remarks>This class initializes the FTP server, loads the configuration, sets up logging, and handles
    /// the server lifecycle, including graceful shutdown on cancellation requests.</remarks>
    internal class Program
    {
        /// <summary>
        /// The entry point of the application that initializes and starts the FTP server.
        /// </summary>
        /// <remarks>This method configures logging, loads the server configuration, and starts the FTP
        /// server. It also handles graceful shutdown on receiving a cancellation signal (e.g., Ctrl+C). The
        /// configuration file path can be provided as a command-line argument; otherwise, a default configuration file
        /// named "amftpd.json" is used.</remarks>
        /// <param name="args">An array of command-line arguments. The first argument, if provided, specifies the path to the server
        /// configuration file.</param>
        /// <returns>A task that represents the asynchronous operation of the server.</returns>
        static async Task Main(string[] args)
        {
            PrintBanner();

            var logger = new CombinedFtpLogger(new ConsoleFtpLogger(),
                new FileFtpLogger(new FileFtpLoggerOptions
                    { FilePath = "logs/amftpd.log", MinLevel = FtpLogLevel.Trace }));
            var configFile = args.Length > 0 ? args[0] : "amftpd.json";

            var runtime = await AmFtpdConfigLoader.LoadAsync(configFile, logger);

            var server = new FtpServer(runtime, logger);

            logger.Log(FtpLogLevel.Info, $"[amFTPd] Root path : {runtime.FtpConfig.RootPath}");
            logger.Log(FtpLogLevel.Info, $"[amFTPd] Bind      : {runtime.FtpConfig.BindAddress}:{runtime.FtpConfig.Port}");
            logger.Log(FtpLogLevel.Info, $"[amFTPd] Users DB  : {Path.GetFullPath(configFile)}");

            var serverTask = server.StartAsync();

            Console.CancelKeyPress += (_, e) =>
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
            var line2 = "A Managed FTP daemon";
            new[]
            {
                @".______  ._____.___ .____________._._______ .______  ",
                @":      \ :         |:_ ____/\__ _:|: ____  |:_ _   \ ",
                @"|   .   ||   \  /  ||   _/    |  :||    :  ||   |   |",
                @"|   :   ||   |\/   ||   |     |   ||   |___|| . |   |",
                @"|___|   ||___| |   ||_. |     |   ||___|    |. ____/ ",
                @"    |___|      |___|  :/      |___|          :/      ",
                @"                      :                      :       "
            }.WriteBoxedBanner();
            var ver = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            $"amFTPd - a managed FTP daemon v{ver}".WriteBoxedBanner();
            "Press Ctrl+C to stop.\n".WriteStyledLogLine();
        }
    }
}
