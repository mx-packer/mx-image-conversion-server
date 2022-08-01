using CommandLine;
using ImageConversionServer.Utilities;
using Serilog;
using Serilog.Events;
using System.Net;

namespace ImageConversionServer
{
    internal class CommandlineOptions
    {
        [Option('p', "port", HelpText = "Set a port that the ICS will use(0~65535).", Default = 49696)]
        public int Port { get; set; }

        [Option('c', "use-caching", HelpText = "Set whether or not to use cache.", Default = true)]
        public bool UseCaching { get; set; }

        [Option('d', "cache-duration", HelpText = "Sets the cache duration(unit: min, 1~60).", Default = 10)]
        public int CacheDuration { get; set; }

        [Option('i', "items-to-preload", HelpText = "Set the files(or directories) to be preloaded. Each item is separated by '|(Vertical Bar)' letter.", Default = null, Separator = '|', Max = 100)]
        public IEnumerable<string>? ItemsToPreload { get; set; }

        [Option("avif-q", HelpText = "Sets the quality parameter of AVIF format(0~100).", Default = 60)]
        public int AvifQ { get; set; }

        [Option("avif-effort", HelpText = "Sets the effort parameter of AVIF format(1~10).", Default = 1)]
        public int AvifEffort { get; set; }

        [Option("avif-uselossless", HelpText = "Set whether or not to use lossless compression.", Default = false)]
        public bool AvifUseLossless { get; set; }

        [Option("avif-usesubsampling", HelpText = "Set whether or not to use chroma subsampling.", Default = true)]
        public bool AvifUseSubsampling { get; set; }

        [Option("png-q", HelpText = "Sets the quality parameter of PNG format(0~100).", Default = 60)]
        public int PngQ { get; set; }

        [Option("png-effort", HelpText = "Sets the effort parameter of PNG format(1~10).", Default = 1)]
        public int PngEffort { get; set; }

        [Option("png-compressionlevel", HelpText = "Sets the compression level parameter of PNG format(0~9).", Default = 6)]
        public int PngCompressionLevel { get; set; }

        [Option("png-useinterlace", HelpText = "Set whether or not to use interlace mode.", Default = false)]
        public bool PngUseInterlace { get; set; }
    }

    public class Program
    {
        public static void Main(string[] args)
        {
            CreateLogger();
            CreateArguments(args);
            CreateServer();
        }

        private static void CreateLogger()
        {
            // Print the signature.
            string signature = "___  _____   __    _____ _____  _____ \r\n|  \\/  |\\ \\ / /   |_   _/  __ \\/  ___|\r\n| .  . | \\ V /______| | | /  \\/\\ `--. \r\n| |\\/| | /   \\______| | | |     `--. \\\r\n| |  | |/ /^\\ \\    _| |_| \\__/\\/\\__/ /\r\n\\_|  |_/\\/   \\/    \\___/ \\____/\\____/ ";
            Console.WriteLine(signature);

            // Create a new logger.
            string fileName = @"data\logs\log-.log";
            string outputTemplateString = "{Timestamp:HH:mm:ss.ms} [{Level}] {Message}{NewLine}{Exception}";

            var log = new LoggerConfiguration()
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Verbose, outputTemplate: outputTemplateString)
                .WriteTo.File(fileName, restrictedToMinimumLevel: LogEventLevel.Warning, outputTemplate: outputTemplateString, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 100000)
                .CreateLogger();

            Log.Logger = log;
        }

        private static void CreateArguments(string[] args)
        {
            // Get commandline arguments.

            CommandlineOptions? options = null;

            Parser.Default.ParseArguments<CommandlineOptions>(args)
                   .WithParsed<CommandlineOptions>(o =>
                   {
                       Settings.General.Port = o.Port;

                       Settings.Cache.UseCaching = o.UseCaching;
                       Settings.Cache.Duration = o.CacheDuration;
                       Settings.Cache.ItemsToPreload = o.ItemsToPreload != null ? o.ItemsToPreload.ToList() : new List<string>();

                       Settings.Avif.Q = o.AvifQ;
                       Settings.Avif.Effort = o.AvifEffort;
                       Settings.Avif.UseLossless = o.AvifUseLossless;
                       Settings.Avif.UseSubsampling = o.AvifUseSubsampling;

                       Settings.Png.Q = o.PngQ;
                       Settings.Png.Effort = o.PngEffort;
                       Settings.Png.CompressionLevel = o.PngCompressionLevel;
                       Settings.Png.UseInterlace = o.PngUseInterlace;

                       options = o;
                   });

            // Check parameters.
            if (NetworkHelper.IsLocalPortBusy(Settings.General.Port))
            {
                Log.Fatal($"The port {Settings.General.Port} is busy.");
                Environment.Exit(1);
            }

            if (Settings.Cache.Duration <= 0 || Settings.Cache.Duration > 60)
            {
                Log.Fatal($"The cache duration is out of range(1~60).");
                Environment.Exit(1);
            }

            if (Settings.Cache.ItemsToPreload != null)
            {
                foreach (string item in Settings.Cache.ItemsToPreload)
                {
                    if (!IndexingHelper.isExists(item))
                    {
                        Log.Fatal($"One or more items do not exist.");
                        Environment.Exit(1);
                    }
                }
            }

            if (Settings.Avif.Q < 0 || Settings.Avif.Q > 100)
            {
                Log.Fatal($"The AVIF Q is out of range(1~60).");
                Environment.Exit(1);
            }

            if (Settings.Png.Effort < 1 || Settings.Png.Effort > 10)
            {
                Log.Fatal($"The AVIF effort is out of range(1~10).");
                Environment.Exit(1);
            }

            if (Settings.Png.Q < 0 || Settings.Png.Q > 100)
            {
                Log.Fatal($"The PNG Q is out of range(1~60).");
                Environment.Exit(1);
            }

            if (Settings.Png.Effort < 1 || Settings.Png.Effort > 10)
            {
                Log.Fatal($"The PNG effort is out of range(1~10).");
                Environment.Exit(1);
            }

            if (Settings.Png.CompressionLevel < 0 || Settings.Png.CompressionLevel > 9)
            {
                Log.Fatal($"The PNG compression level is out of range(0~9).");
                Environment.Exit(1);
            }

            Log.Information($"Arguments have been inputted: {(args.Length == 0 ? "null" : string.Join(' ', args))}");
            Log.Information($"Arguments have been applied: {Parser.Default.FormatCommandLine(options)}");
        }

        private static void CreateServer()
        {
            try
            {
                using (var server = new HttpImagingServerV2(8))
                {
                    Log.Information($"The server is listening on http://localhost:{Settings.General.Port}.");

                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Enter 'stop' to stop the server.");
                    Console.ForegroundColor = ConsoleColor.White;

                    server.Start(Settings.General.Port);

                    for (bool coninute = true; coninute;)
                    {
                        string input = Console.ReadLine()?.ToLower()!;

                        switch (input)
                        {
                            case "stop":
                                server.Stop();
                                coninute = false;
                                break;
                            default:
                                break;
                        }
                    }

                    Log.Information($"The server has been closed.");
                }
            }
            catch (HttpListenerException ex)
            {
                const int EC_CODE_ACCESS_DENIED = 5;

                if (ex.ErrorCode == EC_CODE_ACCESS_DENIED)
                {
                    Log.Fatal("Please restart the application with administrator privilege.");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "An unknown exception has occurred.");
            }
        }
    }
}