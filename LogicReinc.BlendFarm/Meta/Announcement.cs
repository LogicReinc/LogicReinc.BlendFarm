using Avalonia.Media.Imaging;
using LogicReinc.BlendFarm.Windows;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Meta
{
    public class Announcement
    {
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public List<StorySegment> Segments { get; set; }

        public string DateText => $"{Date.Year}-{Date.Month}-{Date.Day}";

        public static List<Announcement> GetAnnouncements(string url)
        {
            using (WebClient client = new WebClient())
            {
                return JsonSerializer.Deserialize<List<Announcement>>(client.DownloadString(url));
            }
        }


    }

    public class StorySegment : INotifyPropertyChanged
    {
        private static Dictionary<string, Bitmap> _bitmapCache = new Dictionary<string, Bitmap>();


        public string Type { get; set; }
        public string Text { get; set; }

        public string[] Parameters { get; set; }
        public string[] Parameters2 { get; set; }

        //Type
        public bool IsText => Type == "Text";
        public bool IsImage => Type == "Image";
        public bool IsButton => Type == "Button";

        //Text Property
        public bool IsPartedText => TextPart2.Contains("|");
        public string TextPart1 => Text.Contains("|") ? Text.Split('|')[0] : Text;
        public string TextPart2 => Text.Contains("|") ? Text.Split('|')[1] : Text;

        public bool IsTextUrl => Text.StartsWith("http://") || Text.StartsWith("https://");
        public bool IsTextPart1Url => TextPart1.StartsWith("http://") || TextPart1.StartsWith("https://");
        public bool IsTextPart2Url => TextPart2.StartsWith("http://") || TextPart2.StartsWith("https://");

        public Bitmap BitmapFromText
        {
            get
            {
                if (!IsTextPart1Url)
                    return null;
                if (_bitmapCache.ContainsKey(TextPart1))
                    return _bitmapCache[TextPart1];

                Task.Run(() =>
                {
                    try
                    {
                        using (WebClient client = new WebClient())
                        using (MemoryStream stream = new MemoryStream(client.DownloadData(TextPart1)))
                        {
                            Bitmap bitmap = new Bitmap(stream);
                            _bitmapCache.Add(TextPart1, bitmap);
                        }
                        if (_bitmapCache.ContainsKey(TextPart1))
                            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BitmapFromText)));
                    }
                    catch(Exception ex)
                    {
                        if(!_bitmapCache.ContainsKey(TextPart1))
                            _bitmapCache.Add(TextPart1, null);
                    }
                });
                return null;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public StorySegment()
        {

        }

        public void Execute()
        {
            if(IsTextPart2Url)
                Process.Start(new ProcessStartInfo(TextPart2)
                {
                    UseShellExecute = true
                });
        }

    }
}
