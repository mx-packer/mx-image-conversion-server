using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageConversionServer.Utilities
{
    internal static class IndexingHelper
    {
        internal static bool IsDirectory(string path)
        {
            FileAttributes attr = File.GetAttributes(path);

            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                return true;
            else
                return false;
        }

        internal static bool isExists(string path)
        {
            return (Directory.Exists(path) || File.Exists(path));
        }
    }
}
