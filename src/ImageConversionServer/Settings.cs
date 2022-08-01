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
        internal static class Cache
        {
            internal static bool UseCaching = false;
        }

        internal static class Avif
        {
            internal static int Q = 60;
            internal static int Effort = 1;
            internal static bool UseLossless = false;
            internal static bool UseSubsampling = true;
        }

        internal static class Png
        {
            internal static int Q = 80;
            internal static int Effort = 4;
            internal static int CompressionLevel = 6;
            internal static bool UseInterlace = false;
        }
    }
}
