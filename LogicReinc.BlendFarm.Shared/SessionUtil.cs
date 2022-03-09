using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LogicReinc.BlendFarm.Shared
{
    public static class SessionUtil
    {
        public static string GetSessionNetworkPath(string path, string sessionId)
        {
            if (sessionId.Contains("-"))
                sessionId = sessionId.Substring(0, sessionId.IndexOf('-'));
            return Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + $".{sessionId}.blend");
        }
    }
}
