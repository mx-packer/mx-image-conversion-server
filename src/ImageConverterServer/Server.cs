using ImageConverterServer.Utilities;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace ImageConverterServer
{
    internal class Server : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly TaskCompletionSource<bool> _cancellationSource = new();

        internal Task? ServerTask { get; private set; }

        internal event Action<IPEndPoint, string?> OnResponse = delegate { };

        internal event Action<Exception, string?> OnException = delegate { };

        internal Task Open(int port)
        {
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
                    await SendIndexAsync(request, response).ConfigureAwait(false);
                    break;
                case "/favicon.ico":
                    response.AddHeader("Cache-Control", "Max-Age=99999");
                    response.ContentType = "image/x-icon";
                    break;
                case "/convert":
                    await SendConvertedImageAsync(request, response).ConfigureAwait(false);
                    break;
                default:
                    await SendNotFoundAsync(request, response).ConfigureAwait(false);
                    break;
            }
        }

        private async Task SendNotFoundAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            byte[] content;

            try
            {
                var _assembly = Assembly.GetExecutingAssembly();

                using (var reader = new StreamReader(_assembly.GetManifestResourceStream("ImageConverterServer.Resources.404.html")!))
                {
                    string html = await reader.ReadToEndAsync().ConfigureAwait(false);
                    content = Encoding.UTF8.GetBytes(html);
                }
            }
            catch (Exception exception)
            {
                response.StatusCode = 500;
                content = Encoding.UTF8.GetBytes($"Internal server error : {exception.Message}");

                OnException(exception, "An internal server error has occurred.");
            }

            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponse(request.RemoteEndPoint, $"A HTTP Error(404, Not Found) has occurred(URL : {request.Url}).");
        }

        private async Task SendIndexAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            byte[] content;

            try
            {
                var _assembly = Assembly.GetExecutingAssembly();

                using (var reader = new StreamReader(_assembly.GetManifestResourceStream("ImageConverterServer.Resources.index.html")!))
                {
                    string html = await reader.ReadToEndAsync().ConfigureAwait(false);
                    content = Encoding.UTF8.GetBytes(html);
                }
            }
            catch (Exception exception)
            {
                response.StatusCode = 500;
                content = Encoding.UTF8.GetBytes($"Internal server error : {exception.Message}");

                OnException(exception, "An internal server error has occurred.");
            }

            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = content.LongLength;
            await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

            OnResponse(request.RemoteEndPoint, $"The Index page has been sent successfully.");
        }

        private bool ParseBoolean(int value)
        {
            if (value == 0)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        private async Task SendConvertedImageAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            if (request.HttpMethod == "GET")
            {
                byte[] content;

                try
                {
                    var queryValues = request.QueryString;
                    string? type = queryValues.Get("type")?.ToLower();
                    string? path = queryValues.Get("path");

                    switch (type)
                    {
                        case "avif":
                            {
                                response.ContentType = "image/avif";

                                int q = int.Parse(queryValues.Get("q") ?? "60");
                                int effort = int.Parse(queryValues.Get("effort") ?? "1");
                                bool lossless = ParseBoolean(int.Parse(queryValues.Get("lossless") ?? "1"));
                                bool useSubsampling = ParseBoolean(int.Parse(queryValues.Get("uss") ?? "0"));

                                using (MemoryStream stream = new MemoryStream())
                                {
                                    Log.Information($"Conversion Settings : Type({type}), Path({path}), Q({q}), Effort({effort}), Lossless({lossless}), UseSubsampling({useSubsampling})");
                                    ImageProcessor.ConvertToAvif(stream, path!, q: q, effort: effort, lossless: lossless, useSubsampling: useSubsampling);
                                    content = stream.ToArray();
                                }

                                break;
                            }
                        case "png":
                            {
                                response.ContentType = "image/png";

                                int q = int.Parse(queryValues.Get("q") ?? "80");
                                int effort = int.Parse(queryValues.Get("effort") ?? "4");
                                int compression = int.Parse(queryValues.Get("compression") ?? "6");
                                bool interlace = ParseBoolean(int.Parse(queryValues.Get("interlace") ?? "1"));

                                using (MemoryStream stream = new MemoryStream())
                                {
                                    Log.Information($"Conversion Settings : Type({type}), Path({path}), Q({q}), Effort({effort}), Compression({compression}), Interlace({interlace})");
                                    ImageProcessor.ConvertToPng(stream, path!, q: q, effort: effort, compression: compression, interlace: interlace);
                                    content = stream.ToArray();
                                }

                                break;
                            }
                        default:
                            throw new Exception("The conversion type does not selected.");
                    }
                }
                catch (Exception exception)
                {
                    response.ContentType = "text/plain; charset=utf-8";
                    response.StatusCode = 500;
                    content = Encoding.UTF8.GetBytes($"Internal server error : {exception.Message}");

                    OnException(exception, "An internal server error has occurred.");
                }

                response.ContentLength64 = content.LongLength;
                await response.OutputStream.WriteAsync(content).ConfigureAwait(false);

                OnResponse(request.RemoteEndPoint, $"A converted image sent successfully.");
            }
        }
    }
}
