using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageConversionServer
{
    internal static class Settings
    {
        internal static class General
        {
            internal static int Port { get; set; } = 49696;
        }
        internal static class Cache
        {
            internal static bool UseCaching { get; set; } = true;

            internal static int Duration { get; set; } = 10; // Unit: minutes

            internal static List<string>? ItemsToPreload { get; set; } = null;
        }

        internal static class Avif
        {
            internal static int Q { get; set; } = 60;
            internal static int Effort { get; set; } = 1;
            internal static bool UseLossless { get; set; } = false;
            internal static bool UseSubsampling { get; set; } = true;
        }

        internal static class Png
        {
            internal static int Q { get; set; } = 80;
            internal static int Effort { get; set; } = 4;
            internal static int CompressionLevel { get; set; } = 6;
            internal static bool UseInterlace { get; set; } = false;
        }
    }
}
