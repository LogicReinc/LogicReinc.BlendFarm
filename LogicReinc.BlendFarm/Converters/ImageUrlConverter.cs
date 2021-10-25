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

            if (_cache.ContainsKey(url))
                return _cache[url];

            using (WebClient client = new WebClient())
            using (MemoryStream stream = new MemoryStream(client.DownloadData(url)))
            {
                Bitmap bitmap = new Bitmap(stream);
                _cache.Add(url, bitmap);
            }
            return _cache[url];
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
