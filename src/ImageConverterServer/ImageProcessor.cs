using NetVips;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageConverterServer
{
    internal static class ImageProcessor
    {
        /// <summary>
        /// Convert an image to AVIF format.
        /// </summary>
        /// <param name="stream">File stream that a converted image will be stored</param>
        /// <param name="filePath">An image file to convert</param>
        /// <param name="q">Q(Quality) Factor</param>
        /// <param name="lossless">Enable lossless compression</param>
        /// <param name="effort">CPU effort</param>
        /// <param name="useSubsampling">Enable chroma subsampling</param>
        internal static void ConvertToAvif(Stream stream, string filePath, int? q = null, int? effort = null, bool? lossless = null, bool? useSubsampling = null)
        {
            try
            {
                using (var image = Image.NewFromFile(filePath, true))
                {
                    var selectedSubsampleMode = Enums.ForeignSubsample.Off;

                    if (useSubsampling == true)
                    {
                        selectedSubsampleMode = Enums.ForeignSubsample.On;
                    }

                    // Recommended Settings : Q : 60, Lossless : false, Compression : AV1, Effort : 4, Subsampling : On, Strip : True 
                    image.HeifsaveStream(stream, q: q, lossless: lossless, compression: Enums.ForeignHeifCompression.Av1, effort: effort, subsampleMode: selectedSubsampleMode, strip: true);
                }
            }
            catch
            {
                throw;
            }
        }

        /// <summary>
        /// Convert an image to PNG format.
        /// </summary>
        /// <param name="stream">File stream that a converted image will be stored</param>
        /// <param name="filePath">An image file to convert</param>
        /// <param name="q">Q(Quality) Factor</param>
        /// <param name="effort"></param>
        /// <param name="compression">Compression level</param>
        /// <param name="interlace">Use interlace</param>
        internal static void ConvertToPng(Stream stream, string filePath, int? q = null, int? effort = null, int? compression = null, bool? interlace = null)
        {
            try
            {
                using (var image = Image.NewFromFile(filePath, true))
                {
                    // Recommended Settings : Q : 80, Effort : 6, Interlace : False, Compression : 6, Profile : sRGB, Filter : None, Bitdepth : 8, Strip : True 
                    image.PngsaveStream(stream, q: q, effort: effort,  compression: compression, interlace: interlace, profile: "sRGB", filter: Enums.ForeignPngFilter.None, bitdepth: 8, strip: true); // An image to convert to PNG must be more than 16*16px.
                }
            }
            catch
            {
                throw;
            }
        }
    }
}
