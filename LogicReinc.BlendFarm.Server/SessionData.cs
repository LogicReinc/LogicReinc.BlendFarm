using System;
using System.Collections.Generic;
using System.Text;
using System.Collections;
using System.IO;
using System.Linq;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Contains information about sessions (Blend files and such)
    /// Keeps track of them by SessionID
    /// </summary>
    public class SessionData
    {
        public static Dictionary<string, SessionData> Sessions { get; private set; } = new Dictionary<string, SessionData>();

        public string SessionID { get; set; }

        public long FileID { get; set; }
        public DateTime Updated { get; set; }

        /// <summary>
        /// Delete a session and associated blend file
        /// </summary>
        public void Delete()
        {
            if (Sessions.ContainsKey(SessionID))
                Sessions.Remove(SessionID);
            string blendFile = GetBlendFilePath();
            if (File.Exists(blendFile))
                File.Delete(blendFile);
        }

        /// <summary>
        /// Get or create a session with given SessionID 
        /// </summary>
        public static SessionData GetOrCreate(string sessionID)
        {
            if (!Sessions.ContainsKey(sessionID))
            {
                Sessions.Add(sessionID, new SessionData()
                {
                    SessionID = sessionID
                });
            }

            return Sessions[sessionID];
        }

        /// <summary>
        /// Deletes all sessions with provided IDs and their associated files
        /// </summary>
        public static void CleanUp(params string[] args)
        {
            List<SessionData> sessions = args.Distinct().Where(x => Sessions.ContainsKey(x)).Select(x => Sessions[x]).ToList();
            foreach (SessionData ses in sessions)
                try
                {
                    ses.Delete();
                }
                catch (Exception ex) { }
        }

        /// <summary>
        /// Called when updating file
        /// </summary>
        public void UpdatingFile()
        {
            FileID = -1;
        }
        /// <summary>
        /// Sets a new FileID (indicating updated blend file)
        /// </summary>
        public void UpdatedFile(long fileId)
        {
            FileID = fileId;
            Updated = DateTime.Now;
        }

        /// <summary>
        /// Get formatted path to blend file for this session
        /// </summary>
        /// <returns></returns>
        public string GetBlendFilePath()
        {
            return Path.GetFullPath(Path.Combine(ServerSettings.Instance.BlenderFiles, SessionID)) + ".blend";
        }

        /// <summary>
        /// Get formatted path to Blend file for a specific session
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        public static string GetFilePath(string session)
        {
            SessionData sess = null;
            if (Sessions.ContainsKey(session))
                sess = Sessions[session];

            if (sess == null)
                return null;
            if (sess.FileID == 0 || sess.Updated == DateTime.MinValue)
                return null;

            string path = sess.GetBlendFilePath();

            if (!File.Exists(path))
                return null;

            return sess.GetBlendFilePath();
        }
    }
}
