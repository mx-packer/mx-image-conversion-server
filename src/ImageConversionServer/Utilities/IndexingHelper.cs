using Serilog;
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

        public static List<string> GetFiles(DirectoryInfo directory, string[]? extensions = null)
        {
            List<string> items = new List<string>();

            // Index files in current directory.
            try
            {
                foreach (FileInfo file in directory.EnumerateFiles())
                {
                    if (extensions != null && !extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    items.Add(file.FullName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "An unexpected exception has occured.");
            }

            // Re-index files in sub-directory.
            try
            {
                foreach (DirectoryInfo subDirectory in directory.EnumerateDirectories())
                {
                    List<string> subItems = GetFiles(subDirectory, extensions);
                    items.AddRange(subItems);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "An unexpected exception has occured.");
            }

            return items;
        }
    }
}
