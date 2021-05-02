using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace LogicReinc.BlendFarm.Shared
{
    /// <summary>
    /// Parser for Blenders versions online file directory
    /// </summary>
    public class WebIndex
    {
        private static Regex REGEX_INDEX = new Regex("^<a href=\"(.*?)\">(.*?)<\\/a>\\s*(.*?)\\s\\s\\s*(.*?)$", RegexOptions.Multiline);

        public string Name { get; set; }
        public string Url { get; set; }

        public string Date { get; set; }
        public int Size { get; set; }
        public bool IsFile => Size > 0;

        public DateTime DateTime => DateTime.Parse(Date);

        public List<WebIndex> GetIndexes()
        {
            if (IsFile)
                throw new InvalidOperationException("Not a directory");
            return GetIndexes(Url);
        }

        public static List<WebIndex> GetIndexes(string url)
        {
            using(WebClient client = new IndexWebClient())
            {
                string html = client.DownloadString(url);

                List<WebIndex> Indexes = new List<WebIndex>();

                foreach(Match match in REGEX_INDEX.Matches(html))
                {
                    if(match.Groups.Count == 5)
                    {
                        string iurl = Path.Combine(url, match.Groups[1].Value);
                        string name = match.Groups[2].Value;
                        string size = match.Groups[4].Value.Trim();
                        string date = match.Groups[3].Value;

                        WebIndex index = new WebIndex()
                        {
                            Url = iurl,
                            Name = name,
                            Size = (size == "-") ? 0 : int.Parse(size),
                            Date = date
                        };
                        Indexes.Add(index);
                    }
                }
                return Indexes;
            }
        }


        private class IndexWebClient : WebClient
        {

            protected override WebRequest GetWebRequest(Uri address)
            {
                WebRequest req = base.GetWebRequest(address);

                req.Timeout = 5000;

                return req;
            }
        }
    }
}
