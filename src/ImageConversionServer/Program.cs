using CommandLine;
using ImageConversionServer.Utilities;
using Serilog;
using Serilog.Events;
using System.Net;

namespace ImageConversionServer
{
    internal class CommandlineOptions
    {
        [Option('p', "port", Required = false, HelpText = "Set a port that the ICS will use.", Default = 6090)]
        public int Port { get; set; }
    }

    public class Program
    {
        public static async Task Main(string[] args)
        {
            CreateLogger();

            int port = 6090;

            if (CreateArguments(args, out port))
            {
                await CreateServer(port);
            }
            else
            {
                Environment.Exit(1);
            }
        }

        private static void CreateLogger()
        {
            // Print the signature.
            string signature = "___  _____   __    _____ _____  _____ \r\n|  \\/  |\\ \\ / /   |_   _/  __ \\/  ___|\r\n| .  . | \\ V /______| | | /  \\/\\ `--. \r\n| |\\/| | /   \\______| | | |     `--. \\\r\n| |  | |/ /^\\ \\    _| |_| \\__/\\/\\__/ /\r\n\\_|  |_/\\/   \\/    \\___/ \\____/\\____/ ";
            Console.WriteLine(signature);

            // Create a new logger.
            string fileName = @"data\logs\log-.log";
            string outputTemplateString = "{Timestamp:HH:mm:ss.ms} (Thread #{ThreadId}) [{Level}] {Message}{NewLine}{Exception}";

            var log = new LoggerConfiguration()
                .Enrich.WithProperty("ThreadId", Thread.CurrentThread.ManagedThreadId)
                .WriteTo.Console(restrictedToMinimumLevel: LogEventLevel.Verbose, outputTemplate: outputTemplateString)
                .WriteTo.File(fileName, restrictedToMinimumLevel: LogEventLevel.Warning, outputTemplate: outputTemplateString, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 100000)
                .CreateLogger();

            Log.Logger = log;
        }

        private static bool CreateArguments(string[] args, out int port)
        {
            bool errorFlag = false;

            // Get commandline arguments.
            int portArgument = 6090; // Default

            Parser.Default.ParseArguments<CommandlineOptions>(args)
                   .WithParsed<CommandlineOptions>(o =>
                   {
                       portArgument = o.Port;
                   });

            port = portArgument;

            // Check the port is open.
            if (NetworkHelper.IsLocalPortBusy(port))
            {
                Log.Fatal($"The port {port} is busy.");
                errorFlag = true;
            }

            return !errorFlag;
        }

        private static async Task CreateServer(int port)
        {
            try
            {
                using (var server = new HttpImagingServer())
                {
                    // Add event subscribers.
                    server.OnResponsed += (IPEndPoint endpoint, string? message) =>
                    {
                        Log.Information($"(from {endpoint}) {message}");
                    };

                    server.OnException += (Exception exception, string? message) =>
                    {
                        Log.Warning(exception, message);
                    };

                    // Open the server.
                    Task serverTask = Task.Run(() => server.Open(port));
                    Log.Information($"The server is listening on http://localhost:{port}.");

                    await serverTask.ConfigureAwait(false);
                    Log.Information($"The server has been closed.");
                }
            }
            catch (HttpListenerException ex)
            {
                const int EC_CODE_ACCESS_DENIED = 5;

                if (ex.ErrorCode == EC_CODE_ACCESS_DENIED)
                {
                    Log.Fatal("Please restart the application with administrator privilege.");
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "An unknown exception has occurred.");
            }
        }
    }
}