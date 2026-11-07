using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Converters
{
    public class ImageUrlConverter : IValueConverter
    {
        private static Dictionary<string, Bitmap> _cache = new Dictionary<string, Bitmap>();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string url = value as string;
            if (string.IsNullOrWhiteSpace(url))
                return null;

            lock (_cache)
            {
                if (_cache.TryGetValue(url, out Bitmap cached))
                    return cached;
            }

            try
            {
                using (WebClient client = new WebClient())
                using (MemoryStream stream = new MemoryStream(client.DownloadData(url)))
                {
                    Bitmap bitmap = new Bitmap(stream);
                    lock (_cache)
                    {
                        if (_cache.TryGetValue(url, out Bitmap existing))
                        {
                            bitmap.Dispose();
                            return existing;
                        }
                        _cache.Add(url, bitmap);
                    }
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                //Offline or a bad image: show nothing rather than crash the binding
                Console.WriteLine($"Failed to load image {url}: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
