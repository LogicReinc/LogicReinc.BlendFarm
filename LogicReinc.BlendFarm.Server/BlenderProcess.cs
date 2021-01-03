using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Represents a single blender process instance
    /// </summary>
    public class BlenderProcess
    {
        private static Regex REGEX_Progress = new Regex("Fra:.*Time:(.*?)\\|.*?Remaining:(.*?)\\|.*?Rendered(.*?)\\/(.*?)Tiles");

        public string CMD { get; private set; }
        public string ARG { get; private set; }
        public event Action<string> OnBlenderOutput;

        public Process Process { get; private set; }

        /// <summary>
        /// Called when a task is complete with ID
        /// </summary>
        public event Action<string> OnBlenderCompleteTask;
        /// <summary>
        /// Called when an exception is caught by script
        /// </summary>
        public event Action<string> OnBlenderException;
        /// <summary>
        /// Called when a render status update is received
        /// </summary>
        public event Action<Status> OnBlenderStatus;

        public BlenderProcess(string blender, string args)
        {
            this.CMD = blender;
            this.ARG = args;
        }

        /// <summary>
        /// Starts the process and handle output
        /// </summary>
        public void Run()
        {
            Process process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = CMD,
                    Arguments = ARG,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            Process = process;
            process.Start();

            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                ProcessBlenderLine(line);
            }

            process.WaitForExit();
        }

        /// <summary>
        /// Cancel the process
        /// </summary>
        public void Cancel()
        {
            if (Process != null)
                Process.Kill();
        }


        /// <summary>
        /// Handles a Blender print line
        /// </summary>
        /// <param name="line"></param>
        private void ProcessBlenderLine(string line)
        {
            Console.WriteLine(line);

            try
            {
                Match match = REGEX_Progress.Match(line);

                //Handle Status
                if (OnBlenderStatus != null && match != null && match.Success && match.Groups.Count == 5)
                {
                    string timeStr = match.Groups[1].Value.Trim();
                    string remainStr = match.Groups[2].Value.Trim();
                    string renderedStr = match.Groups[3].Value.Trim();
                    string tilesTotalStr = match.Groups[4].Value.Trim();


                    if (OnBlenderStatus != null)
                        OnBlenderStatus(new Status()
                        {
                            TilesFinish = int.Parse(renderedStr),
                            TilesTotal = int.Parse(tilesTotalStr),

                            //TODO: Proper time parsing, if even bother at all
                            //Time = (int)TimeSpan.Parse(timeStr).TotalSeconds,
                            //TimeRemaining = (int)TimeSpan.Parse(remainStr).TotalSeconds
                        });
                }
                else if (line.StartsWith("EXCEPTION:"))
                    OnBlenderException?.Invoke(line.Substring("EXCEPTION:".Length));
                else if (line.StartsWith("SUCCESS:"))
                    OnBlenderCompleteTask?.Invoke(line.Substring("SUCCESS:".Length));

                OnBlenderOutput?.Invoke(line);
            }
            catch (Exception ex) { }
        }


        /// <summary>
        /// Contains the rendering status (tiles etc)
        /// </summary>
        public class Status
        {
            public int Time { get; set; }
            public int TimeRemaining { get; set; }
            public int TilesFinish { get; set; }
            public int TilesTotal { get; set; }
        }
    }
}
