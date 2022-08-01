using ImageConversionServer.Entities;
using ImageConversionServer.Utilities;
using Serilog;
using System;
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
    internal sealed class HttpImagingServer : IDisposable
    {
        #region ::Variables::

        private readonly Stopwatch _stopwatch = new Stopwatch();

        private readonly HttpListener _listener = new();
        private readonly TaskCompletionSource<bool> _cancellationSource = new();

        private readonly CacheManager _cacheManager = new(100000);

        internal int Port { get; private set; }

        internal Task? ServerTask { get; private set; }

        internal event Action<IPEndPoint, string?> OnResponsed = delegate { };
        internal event Action<Exception, string?> OnException = delegate { };

        #endregion

        #region ::Controllers::

        internal Task Open(int port)
        {
            Port = port;

            _listener.Prefixes.Clear();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();

            // Create and start the loop.
            ServerTask = CreateProcessingLoopAsync();
            return ServerTask;
        }

        internal void Close()
        {
            Dispose();
        }

        public void Dispose()
        {
            _cancellationSource.TrySetResult(false);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region ::Service Logic::

        private async Task CreateProcessingLoopAsync()
        {
            using (_listener)
            {
                while (await AcceptRequestAsync().ConfigureAwait(false)) ;
                _listener.Close();
            }
        }

        private bool IsCancelled()
        {
            var task = _cancellationSource.Task;
            return task.IsCompleted || task.IsCanceled || task.IsFaulted;
        }

        private async Task<bool> AcceptRequestAsync()
        {
            if (IsCancelled())
            {
                return false;
            }

            Task<HttpListenerContext> listenerContextTask = _listener.GetContextAsync();
            await Task.WhenAny(listenerContextTask, _cancellationSource.Task).ConfigureAwait(false);

            if (IsCancelled())
            {
                return false;
            }

            _ = Task.Run(() => SendResponseAsync(listenerContextTask));

            return true;
        }

        private async Task SendResponseAsync(Task<HttpListenerContext> listenerContextTask)
        {
            HttpListenerContext listenerContext = await listenerContextTask.ConfigureAwait(false);

            HttpListenerRequest request = listenerContext.Request;
            using HttpListenerResponse response = listenerContext.Response;

            switch (request.Url?.LocalPath)
            {
                case "/":
                case "/index":
                case "/index.html":
                    await CreateIndexResponseAsync(request, response).ConfigureAwait(false);
                    break;
                case "/settings/cache":
                    await CreateCacheSettingsApiResponseAsync(request, response).ConfigureAwait(false);
                    break;
                case "/settings/avif":
                    await CreateAvifSettingsApiResponseAsync(request, response).ConfigureAwait(false);
                    break;
                case "/settings/png":
                    await CreatePngSettingsApiResponseAsync(request, response).ConfigureAwait(false);
                    break;
                case "/convert":
                    await CreateConversionApiResponseAsync(request, response).ConfigureAwait(false);
                    break;
                case "/favicon.ico":
                    response.AddHeader("Cache-Control", "Max-Age=99999");
                    response.ContentType = "image/x-icon";
                    break;
                default:
                    await CreateErrorResponseAsync(request, response, 404, "FILE NOT FOUND", "The page you are looking for might have been removed had its name changed or is temporarily unavailable.", "HOME PAGE", "/").ConfigureAwait(false);
                    break;
            }
        }



        private async Task CreateIndexResponseAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            byte[] content;

            string html = string.Empty;

            var _assembly = Assembly.GetExecutingAssembly();

            using (var reader = new StreamReader(_assembly.GetManifestResourceStream("ImageConversionServer.Resources.index.html")!))
            {
                StringFormatter formatter = new StringFormatter(await reader.ReadToEndAsync().ConfigureAwait(false));
                formatter.Add("@version", (Assembly.GetExecutingAssembly().GetName().Version!).ToString());
                formatter.Add("@port", Port);

                html = formatter.ToString();
            }

            content = Encoding.UTF8.GetBytes(html);

            response.StatusCode = 200;
            response.ContentType = "text/html; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponsed(request.RemoteEndPoint, $"The request has been processed.");
        }

        private async Task CreateErrorResponseAsync(HttpListenerRequest request, HttpListenerResponse response, int statusCode, string title, string message, string action, string actionLink)
        {
            byte[] content;

            string html = string.Empty;

            var _assembly = Assembly.GetExecutingAssembly();

            using (var reader = new StreamReader(_assembly.GetManifestResourceStream("ImageConversionServer.Resources.error.html")!))
            {
                StringFormatter formatter = new StringFormatter(await reader.ReadToEndAsync().ConfigureAwait(false));
                formatter.Add("@statuscode", statusCode);
                formatter.Add("@title", title);
                formatter.Add("@message", message);
                formatter.Add("@action", action);
                formatter.Add("@link_action", actionLink);

                html = formatter.ToString();
            }

            content = Encoding.UTF8.GetBytes(html);

            response.StatusCode = 200;
            response.ContentType = "text/html; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponsed(request.RemoteEndPoint, $"The request has been processed.");
        }

        private async Task CreateCacheSettingsApiResponseAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            byte[] content;

            Status status;

            var parameters = request.QueryString;

            try
            {
                bool useCaching = false;

                if (bool.TryParse(parameters.Get("use_caching"), out useCaching))
                {
                    Settings.Cache.UseCaching = useCaching;
                }
                else
                {
                    throw new InvalidDataException("An invalid parameter has been entered.");
                }

                status = new Status("S000", "Successfully processed.");

                Log.Information($"Cache settings have been changed(Use Caching : {useCaching}).");
            }
            catch (Exception ex)
            {
                string errorCode = string.Empty;
                string errorMessage = string.Empty;

                if (string.IsNullOrEmpty(parameters.Get("use_caching")))
                {
                    errorCode = "S001";
                    errorMessage = "The query parameter type does not match(use_caching).";
                }

                status = new Status(errorCode, errorMessage);

                OnException(ex, errorMessage);
            }

            string json = JsonSerializer.Serialize(status);
            content = Encoding.UTF8.GetBytes(json);

            response.StatusCode = string.Equals(status.Code, "S000", StringComparison.OrdinalIgnoreCase) ? 200 : 500;
            response.ContentType = "application/json; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponsed(request.RemoteEndPoint, $"The request has been processed.");
        }

        private async Task CreateAvifSettingsApiResponseAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            byte[] content;

            Status status;

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

                OnException(ex, errorMessage);
            }

            string json = JsonSerializer.Serialize(status);
            content = Encoding.UTF8.GetBytes(json);

            response.StatusCode = string.Equals(status.Code, "S000", StringComparison.OrdinalIgnoreCase) ? 200 : 500;
            response.ContentType = "application/json; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponsed(request.RemoteEndPoint, $"The request has been processed.");
        }

        private async Task CreatePngSettingsApiResponseAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            byte[] content;

            Status status;

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

                OnException(ex, errorMessage);
            }

            string json = JsonSerializer.Serialize(status);
            content = Encoding.UTF8.GetBytes(json);

            response.StatusCode = string.Equals(status.Code, "S000", StringComparison.OrdinalIgnoreCase) ? 200 : 500;
            response.ContentType = "application/json; charset=UTF-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponsed(request.RemoteEndPoint, $"The request has been processed.");
        }

        private async Task CreateConversionApiResponseAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.HttpMethod == "GET")
            {
                byte[] content;

                try
                {
                    _stopwatch.Restart();

                    var parameters = request.QueryString;
                    string? format = parameters.Get("format")?.ToLower();
                    string? filePath = parameters.Get("filepath");

                    switch (format)
                    {
                        case "avif":
                            {
                                response.ContentType = "image/avif";

                                if (!_cacheManager.Storage.TryGet(filePath!, out content))
                                {
                                    using (MemoryStream stream = new MemoryStream())
                                    {
                                        ImageProcessor.ConvertToAvif(stream, filePath!, q: Settings.Avif.Q, effort: Settings.Avif.Effort, lossless: Settings.Avif.UseLossless, useSubsampling: Settings.Avif.UseSubsampling);
                                        content = stream.ToArray();

                                        _cacheManager.Storage.AddOrUpdate(filePath!, content);
                                    }
                                }

                                break;
                            }
                        case "png":
                            {
                                response.ContentType = "image/png";

                                if (!_cacheManager.Storage.TryGet(filePath!, out content))
                                {
                                    using (MemoryStream stream = new MemoryStream())
                                    {
                                        ImageProcessor.ConvertToPng(stream, filePath!, q: Settings.Png.Q, effort: Settings.Png.Effort, compression: Settings.Png.CompressionLevel, interlace: Settings.Png.UseInterlace);
                                        content = stream.ToArray();

                                        _cacheManager.Storage.AddOrUpdate(filePath!, content);
                                    }
                                }

                                break;
                            }
                        default:
                            throw new InvalidDataException("An unknown image format has been entered.");
                    }

                    _stopwatch.Stop();

                    response.StatusCode = 200;
                }
                catch (Exception ex) // On caught an error.
                {
                    string errorCode = string.Empty;
                    string errorMessage = string.Empty;

                    var parameters = request.QueryString;
                    string? format = parameters.Get("format");
                    string? filePath = parameters.Get("filepath");

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

                    var status = new Status(errorCode, errorMessage);

                    string json = JsonSerializer.Serialize(status);
                    content = Encoding.UTF8.GetBytes(json);

                    response.StatusCode = 500;
                    response.ContentType = "application/json; charset=UTF-8";

                    OnException(ex, errorMessage);
                }

                response.ContentLength64 = content.LongLength;
                await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

                OnResponsed(request.RemoteEndPoint, $"The request has been processed({_stopwatch.ElapsedMilliseconds}ms).");
            }
        }

        #endregion
    }
}
