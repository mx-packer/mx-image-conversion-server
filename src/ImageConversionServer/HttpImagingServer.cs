using ImageConversionServer.Entities;
using ImageConversionServer.Utilities;
using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;

namespace ImageConversionServer
{
    internal class HttpImagingServer : IDisposable
    {
        private HttpListener? _httpListener = null;
        private Thread? _listenerLoop = null;
        private Thread[]? _requestProcessors = null;
        private BlockingCollection<HttpListenerContext> _messages = null;

        private Thread? _preloader;

        private CacheManager? _cacheManager = null;

        internal virtual int Port { get; set; } = 80;

        internal virtual string[] Prefixes
        {
            get
            {
                return new string[] { string.Format(Settings.General.Prefix, Port) };
            }
        }

        internal HttpImagingServer(int threadCount)
        {
            _requestProcessors = new Thread[threadCount];
            _messages = new BlockingCollection<HttpListenerContext>();
            _httpListener = new HttpListener();

            if (Settings.Cache.UseCaching)
            {
                _cacheManager = new CacheManager(100000, Settings.Cache.Duration);
            }
        }

        internal void Start(int port)
        {
            if (_listenerLoop != null)
            {
                Log.Warning("The server is already running.");
            }

            if (Settings.Cache.UsePreloading)
            {
                _preloader = new Thread(Preload);
                _preloader.Start();
            }

            _listenerLoop = new Thread(HandleRequests);

            Port = port;

            foreach (string prefix in Prefixes)
            {
                _httpListener?.Prefixes.Add(prefix);
            }

            _listenerLoop.Start();

            for (int i = 0; i < _requestProcessors?.Length; i++)
            {
                _requestProcessors[i] = StartProcessor(i, _messages);
            }
        }

        internal void Stop()
        {
            _messages.CompleteAdding();

            foreach (Thread worker in _requestProcessors!)
            {
                worker.Join();
            }

            _httpListener?.Stop();

            if (Settings.Cache.UsePreloading)
            {
                _preloader?.Join();
            }

            _listenerLoop?.Join();
        }

        public void Dispose()
        {
            Stop();
        }

        private void Preload()
        {
            string[] extensions = new string[] { ".avif", ".png" };

            List<string> filesToPreload = new List<string>();

            // Index files.
            foreach (string item in Settings.Cache.ItemsToPreload!)
            {
                if (IndexingHelper.IsDirectory(item))
                {
                    filesToPreload.AddRange(IndexingHelper.GetFiles(new DirectoryInfo(item), extensions));
                }
                else
                {
                    if (extensions.Contains(Path.GetExtension(item), StringComparer.OrdinalIgnoreCase))
                    {
                        filesToPreload.Add(item);
                    }
                }
            }

            // Load files.
            Stopwatch stopwatch = new Stopwatch();

            switch (Settings.Cache.PreloadingConversionFormat.ToLower())
            {
                case "avif":
                    foreach (string file in filesToPreload)
                    {
                        try
                        {
                            using (MemoryStream stream = new MemoryStream())
                            {
                                stopwatch.Restart();

                                // Convert and cache an image.
                                ImageProcessor.ConvertToAvif(stream, file!, q: Settings.Avif.Q, effort: Settings.Avif.Effort, lossless: Settings.Avif.UseLossless, useSubsampling: Settings.Avif.UseSubsampling);
                                _cacheManager?.Storage.AddOrUpdate(file!, stream.ToArray());

                                stopwatch.Stop();

                                Log.Information($"<Preloader> The image has been cached(FORMAT: {Settings.Cache.PreloadingConversionFormat}, PATH: {file}, ELAPSED TIME: {stopwatch.ElapsedMilliseconds}ms).");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"An unknown error occurred while preloading an image.");
                        }
                    }
                    break;
                case "png":
                    foreach (string file in filesToPreload)
                    {
                        try
                        {
                            using (MemoryStream stream = new MemoryStream())
                            {
                                stopwatch.Restart();

                                // Convert and cache an image.
                                ImageProcessor.ConvertToPng(stream, file!, q: Settings.Png.Q, effort: Settings.Png.Effort, compression: Settings.Png.CompressionLevel, interlace: Settings.Png.UseInterlace);
                                _cacheManager?.Storage.AddOrUpdate(file!, stream.ToArray());

                                stopwatch.Stop();

                                Log.Information($"<Preloader> The image has been cached(FORMAT: {Settings.Cache.PreloadingConversionFormat}, PATH: {file}, ELAPSED TIME: {stopwatch.ElapsedMilliseconds}ms).");
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, $"An unknown error occurred while preloading an image.");
                        }
                    }
                    break;
            }
        }

        private void HandleRequests()
        {
            try
            {
                _httpListener?.Start();

                while (_httpListener.IsListening)
                {
                    HttpListenerContext context = _httpListener.GetContext();
                    _messages.Add(context);
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
                Log.Error(ex, "An unknown error has occurred while handling requests.");
                Stop();
                Environment.Exit(1);
            }
        }

        private Thread StartProcessor(int number, BlockingCollection<HttpListenerContext> messages)
        {
            Thread thread = new Thread(() => Processor(number, messages));
            thread.Start();
            return thread;
        }

        private void Processor(int number, BlockingCollection<HttpListenerContext> messages)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;

            Log.Information($"<Processor {number}> the processor started on thread {threadId}.");

            try
            {
                for (; ; )
                {
                    HttpListenerContext context = messages.Take();

                    string departureToDestination = string.Empty;
                    string message = string.Empty;
                    Response(context, out departureToDestination, out message);
                    
                    Log.Information($"<Processor {number}> ({departureToDestination}) {message}");
                }
            }
            catch (Exception ex) {
                Log.Error(ex, "An unknown error has occurred while processing.");
            }

            Log.Information($"<Processor {number}> the processor has terminated.");
        }

        public virtual void Response(HttpListenerContext listenerContext, out string departureToDestination, out string message)
        {
            HttpListenerRequest request = listenerContext.Request;
            using HttpListenerResponse response = listenerContext.Response;

            string messageTemp = string.Empty;

            switch (request.Url?.LocalPath)
            {
                case "/":
                case "/index":
                case "/index.html":
                    CreateIndexResponse(request, response, out messageTemp);
                    break;
                case "/settings/avif":
                    CreateAvifSettingsApiResponse(request, response, out messageTemp);
                    break;
                case "/settings/png":
                    CreatePngSettingsApiResponse(request, response, out messageTemp);
                    break;
                case "/convert":
                    CreateConversionApiResponse(request, response, out messageTemp);
                    break;
                case "/favicon.ico":
                    response.AddHeader("Cache-Control", "Max-Age=99999");
                    response.ContentType = "image/x-icon";
                    break;
                case "/error":
                case "/error.html":
                default:
                    CreateErrorResponse(request, response, 404, "FILE NOT FOUND", "The page you are looking for might have been removed had its name changed or is temporarily unavailable.", "HOME PAGE", "/", out messageTemp);
                    break;
            }

            departureToDestination = $"{request.RemoteEndPoint}->{request.LocalEndPoint}";
            message = messageTemp;
        }

        private void CreateIndexResponse(HttpListenerRequest request, HttpListenerResponse response, out string message)
        {
            byte[] content = Array.Empty<byte>();

            string html = string.Empty;

            var _assembly = Assembly.GetExecutingAssembly();

            using (var reader = new StreamReader(_assembly.GetManifestResourceStream("ImageConversionServer.Resources.index.html")!))
            {
                StringFormatter formatter = new StringFormatter(reader.ReadToEnd());
                formatter.Add("@version", (Assembly.GetExecutingAssembly().GetName().Version!).ToString());
                formatter.Add("@port", Settings.General.Port);

                html = formatter.ToString();
            }

            content = Encoding.UTF8.GetBytes(html);

            response.StatusCode = 200;
            response.ContentType = "text/html; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            response.OutputStream.Write(content);

            message = $"The request has been processed(INDEX, CODE: 200).";
        }

        private void CreateErrorResponse(HttpListenerRequest request, HttpListenerResponse response, int statusCode, string title, string description, string action, string actionLink, out string message)
        {
            byte[] content = Array.Empty<byte>();

            string html = string.Empty;

            var _assembly = Assembly.GetExecutingAssembly();

            using (var reader = new StreamReader(_assembly.GetManifestResourceStream("ImageConversionServer.Resources.error.html")!))
            {
                StringFormatter formatter = new StringFormatter(reader.ReadToEnd());
                formatter.Add("@statuscode", statusCode);
                formatter.Add("@title", title);
                formatter.Add("@description", description);
                formatter.Add("@action", action);
                formatter.Add("@link_action", actionLink);

                html = formatter.ToString();
            }

            content = Encoding.UTF8.GetBytes(html);

            response.StatusCode = statusCode;
            response.ContentType = "text/html; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            response.OutputStream.Write(content);

            message = $"The request has been processed(ERROR, CODE: {response.StatusCode}).";
        }

        private void CreateAvifSettingsApiResponse(HttpListenerRequest request, HttpListenerResponse response, out string message)
        {
            byte[] content = Array.Empty<byte>();

            Status? status = null;

            var parameters = request.QueryString;

            try
            {
                int q = 0;

                if (int.TryParse(parameters.Get("q"), out q))
                {
                    Settings.Avif.Q = q;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                int effort = 0;

                if (int.TryParse(parameters.Get("effort"), out effort))
                {
                    Settings.Avif.Effort = effort;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                bool useLossless = false;

                if (bool.TryParse(parameters.Get("use_lossless"), out useLossless))
                {
                    Settings.Avif.UseLossless = useLossless;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                bool useSubsampling = false;

                if (bool.TryParse(parameters.Get("use_subsampling"), out useSubsampling))
                {
                    Settings.Avif.UseSubsampling = useSubsampling;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                status = new Status("S000", "Successfully processed.");

                Log.Information($"AVIF settings have been changed(Q : {q}, Effort : {effort}, Use Lossless : {useLossless}, Use Subsampling : {useSubsampling}).");
            }
            catch (Exception ex)
            {
                string errorCode = string.Empty;
                string errorMessage = string.Empty;

                if (string.IsNullOrEmpty(parameters.Get("q")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(q).";
                }
                else if (string.IsNullOrEmpty(parameters.Get("effort")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(effort).";
                }
                else if (string.IsNullOrEmpty(parameters.Get("use_lossless")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(use_lossless).";
                }
                else if (string.IsNullOrEmpty(parameters.Get("use_subsampling")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(use_subsampling).";
                }

                status = new Status(errorCode, errorMessage);

                Log.Error(ex, errorMessage);
            }

            string json = JsonSerializer.Serialize(status);
            content = Encoding.UTF8.GetBytes(json);

            response.StatusCode = string.Equals(status.Code, "S000", StringComparison.OrdinalIgnoreCase) ? 200 : 500;
            response.ContentType = "application/json; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            response.OutputStream.Write(content);

            message = $"The request has been processed(SETTINGS, CODE: {response.StatusCode}, MESSAGE: {status.Message}).";
        }

        private void CreatePngSettingsApiResponse(HttpListenerRequest request, HttpListenerResponse response, out string message)
        {
            byte[] content = Array.Empty<byte>();

            Status? status = null;

            var parameters = request.QueryString;

            try
            {
                int q = 0;

                if (int.TryParse(parameters.Get("q"), out q))
                {
                    Settings.Png.Q = q;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                int effort = 0;

                if (int.TryParse(parameters.Get("effort"), out effort))
                {
                    Settings.Png.Effort = effort;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                int compressionLevel = 0;

                if (int.TryParse(parameters.Get("compression_level"), out compressionLevel))
                {
                    Settings.Png.CompressionLevel = compressionLevel;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                bool useInterlace = false;

                if (bool.TryParse(parameters.Get("use_interlace"), out useInterlace))
                {
                    Settings.Png.UseInterlace = useInterlace;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                status = new Status("S000", "Successfully processed.");

                Log.Information($"PNG settings have been changed(Q : {q}, Effort : {effort}, Compression Level : {compressionLevel}, Use Interlace : {useInterlace}).");
            }
            catch (Exception ex)
            {
                string errorCode = string.Empty;
                string errorMessage = string.Empty;

                if (string.IsNullOrEmpty(parameters.Get("q")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(q).";
                }
                else if (string.IsNullOrEmpty(parameters.Get("effort")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(effort).";
                }
                else if (string.IsNullOrEmpty(parameters.Get("compression_level")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(compression_level).";
                }
                else if (string.IsNullOrEmpty(parameters.Get("use_interlace")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(use_interlace).";
                }

                status = new Status(errorCode, errorMessage);

                Log.Error(ex, errorMessage);
            }

            string json = JsonSerializer.Serialize(status);
            content = Encoding.UTF8.GetBytes(json);

            response.StatusCode = string.Equals(status.Code, "S000", StringComparison.OrdinalIgnoreCase) ? 200 : 500;
            response.ContentType = "application/json; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            response.OutputStream.Write(content);

            message = $"The request has been processed(SETTINGS, CODE: {response.StatusCode}, MESSAGE: {status.Message}).";
        }

        private void CreateConversionApiResponse(HttpListenerRequest request, HttpListenerResponse response, out string message)
        {
            byte[] content = Array.Empty<byte>();

            Stopwatch stopwatch = new Stopwatch();

            var parameters = request.QueryString;
            string? format = parameters.Get("format")?.ToLower();
            string? filePath = parameters.Get("filepath");

            try
            {
                stopwatch.Restart();

                switch (format)
                {
                    case "avif":
                        {
                            response.ContentType = "image/avif";

                            if (!Settings.Cache.UseCaching || _cacheManager?.Storage.TryGet(filePath!, out content) == false)
                            {
                                using (MemoryStream stream = new MemoryStream())
                                {
                                    ImageProcessor.ConvertToAvif(stream, filePath!, q: Settings.Avif.Q, effort: Settings.Avif.Effort, lossless: Settings.Avif.UseLossless, useSubsampling: Settings.Avif.UseSubsampling);
                                    content = stream.ToArray();

                                    if (Settings.Cache.UseCaching)
                                    {
                                        _cacheManager?.Storage.AddOrUpdate(filePath!, content);
                                    }
                                }
                            }

                            break;
                        }
                    case "png":
                        {
                            response.ContentType = "image/png";

                            if (!Settings.Cache.UseCaching || _cacheManager?.Storage.TryGet(filePath!, out content) == false)
                            {
                                using (MemoryStream stream = new MemoryStream())
                                {
                                    ImageProcessor.ConvertToPng(stream, filePath!, q: Settings.Png.Q, effort: Settings.Png.Effort, compression: Settings.Png.CompressionLevel, interlace: Settings.Png.UseInterlace);
                                    content = stream.ToArray();

                                    if (Settings.Cache.UseCaching)
                                    {
                                        _cacheManager?.Storage.AddOrUpdate(filePath!, content);
                                    }
                                }
                            }

                            break;
                        }
                    default:
                        throw new InvalidDataException("An unknown image format has been entered.");
                }

                stopwatch.Stop();

                response.StatusCode = 200;
            }
            catch (Exception ex) // On caught an error.
            {
                string errorCode = string.Empty;
                string errorMessage = string.Empty;

                if (string.IsNullOrEmpty(format) || string.IsNullOrEmpty(filePath))
                {
                    errorCode = "C001";
                    errorMessage = $"The value of mandatory parameters has not entered.";
                }
                else if (!File.Exists(filePath))
                {
                    errorCode = "C002";
                    errorMessage = $"The file does not exist.";
                }
                else if (!string.Equals(format, "avif", StringComparison.OrdinalIgnoreCase) && !string.Equals(format, "png", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "C003";
                    errorMessage = $"An unknown image format has been entered.";
                }
                else if (string.IsNullOrEmpty(errorCode))
                {
                    errorCode = "C004";
                    errorMessage = $"An unknown error occurred while converting the image.\r\n{ex.Message}\r\n{ex.StackTrace}";
                }

                Status status = new Status(errorCode, errorMessage);

                string json = JsonSerializer.Serialize(status);
                content = Encoding.UTF8.GetBytes(json);

                response.StatusCode = 500;
                response.ContentType = "application/json; charset=UTF-8";

                Log.Error(ex, errorMessage);
            }

            response.ContentLength64 = content.LongLength;
            response.OutputStream.Write(content);

            message = $"The request has been processed(CONVERT, CODE: {response.StatusCode}, FORMAT: {format}, PATH: {filePath}, ELAPSED TIME: {stopwatch.ElapsedMilliseconds}ms).";
        }
    }
}
