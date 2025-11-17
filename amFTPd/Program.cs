using amFTPd.Config.Daemon;
using amFTPd.Core;
using amFTPd.Logging;
using System.Reflection;
using amFTPd.Utils;

namespace amFTPd
{
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

            var server = new FtpServer(
                runtime.FtpConfig,
                runtime.UserStore,
                runtime.TlsConfig,
                logger,
                runtime.Sections);

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
