using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm
{
    public static class Statics
    {
        private static object _previewImageLock = new object();
        private static Avalonia.Media.Imaging.Bitmap _noPreviewImage { get; set; }
        public static Avalonia.Media.Imaging.Bitmap NoPreviewImage
        {
            get
            {
                lock (_previewImageLock)
                {
                    if (_noPreviewImage == null)
                    {
                        using (System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(700, 200))
                        {
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bmp))
                            using (System.Drawing.Font font = new System.Drawing.Font("Arial", 16))
                            using (System.Drawing.SolidBrush brush = new System.Drawing.SolidBrush(System.Drawing.Color.Gray))
                            {
                                g.DrawString("Could not generate Preview\n(Some Render Formats do not support preview)",
                                    font, brush, 5, 5);
                            }
                            _noPreviewImage = bmp.ToAvaloniaBitmap();
                        }
                    }
                }
                return _noPreviewImage;
            }
        }

        /// <summary>
        /// True for bitmaps shared between projects, which must never be disposed
        /// </summary>
        public static bool IsSharedImage(Bitmap bitmap)
        {
            return bitmap != null && ReferenceEquals(bitmap, _noPreviewImage);
        }

        /// <summary>
        /// Disposes a replaced UI bitmap once the UI has moved on to its replacement.
        /// Background priority runs after the (Normal priority) jobs that swap the image source and after rendering.
        /// </summary>
        public static void DisposeReplacedBitmap(Bitmap old)
        {
            if (old == null || IsSharedImage(old))
                return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    old.Dispose();
                }
                catch { }
            }, DispatcherPriority.Background);
        }

        /// <summary>
        /// Image format matching the file extension, PNG for anything System.Drawing can't encode
        /// </summary>
        public static ImageFormat GetImageFormatForPath(string path)
        {
            string ext = Path.GetExtension(path ?? "")?.TrimStart('.').ToLowerInvariant();
            switch (ext)
            {
                case "jpg":
                case "jpeg":
                    return ImageFormat.Jpeg;
                case "bmp":
                    return ImageFormat.Bmp;
                case "tif":
                case "tiff":
                    return ImageFormat.Tiff;
                case "gif":
                    return ImageFormat.Gif;
                default:
                    return ImageFormat.Png;
            }
        }

        /// <summary>
        /// Saves an image using the format matching its extension (Image.Save(path) always writes PNG)
        /// </summary>
        public static void SaveImage(System.Drawing.Image image, string path)
        {
            image.Save(path, GetImageFormatForPath(path));
        }
        public static void SaveImage(Bitmap bitmap, string path)
        {
            ImageFormat format = GetImageFormatForPath(path);
            if (format.Equals(ImageFormat.Png))
            {
                bitmap.Save(path);
                return;
            }
            using (MemoryStream str = new MemoryStream())
            {
                bitmap.Save(str);
                str.Position = 0;
                using (System.Drawing.Image image = System.Drawing.Image.FromStream(str))
                    image.Save(path, format);
            }
        }

        public static string SanitizePath(string inputPath)
        {
            if (inputPath == null)
                return inputPath;

            //Fix Linux Space escape
            inputPath = inputPath.Replace("\\040", " ");


            return inputPath;
        }

        public static string FormatAnimationFrameFileName(string frameFormat, int frame, int startFrame, int endFrame)
        {
            //Delegates to the shared, unit-tested templating engine (frame-number '#' handling only).
            return Shared.OutputNaming.ApplyFrameHashes(frameFormat, frame, startFrame, endFrame);
        }

        public static Bitmap ToAvaloniaBitmap(this System.Drawing.Image bitmap)
        {
            //TODO: This needs to be better..
            using (MemoryStream str = new MemoryStream())
            {
                bitmap.Save(str, ImageFormat.Png);
                str.Position = 0;
                return new Bitmap(str);
            }
        }
    }
}
