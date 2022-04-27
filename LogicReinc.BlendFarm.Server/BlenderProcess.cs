using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Server
{
    /// <summary>
    /// Represents a single blender process instance
    /// </summary>
    public class BlenderProcess
    {
        private static Regex REGEX_Progress = new Regex("Fra:.*Time:(.*?)\\|.*?Remaining:(.*?)\\|.*?Rendered(.*?)\\/(.*?)Tiles");
        private static Regex REGEX_Progress2 = new Regex("Fra:.*Time:(.*?)\\|.*?Remaining:(.*?)\\|.*?Sample(.*?)\\/([0-9]*)");

        private object _continueLock = new object();

        public const int CONTINUE_TIMEOUT = 10000;

        public string CMD { get; private set; }
        public string ARG { get; private set; }
        public event Action<string> OnBlenderOutput;

        public Process Process { get; private set; }
        public bool Active { get; private set; }

        public string Version { get; private set; }
        public string File { get; private set; }
        public long FileID { get; private set; }

        public bool IsContinueing { get; private set; }

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

        public event Action<BlenderProcess> OnBlenderContinue;

        public int ContinueCount { get; private set; }

        public BlenderProcess(string blender, string args, string version = null, string file = null, long fileId = -1)
        {
            this.CMD = blender;
            this.ARG = args;
            this.Version = version;
            this.File = file;
            this.FileID = fileId;
        }

        private void HandleContinue()
        {
            IsContinueing = true;
            OnBlenderContinue?.Invoke(this);
            int currentCount = 0;

            lock (_continueLock)
            {
                currentCount = ContinueCount + 1;
                ContinueCount = currentCount;
            }
            if (CONTINUE_TIMEOUT == 0)
                Cancel();
            else
            {
                Task.Delay(CONTINUE_TIMEOUT).ContinueWith((x) =>
                {
                    if (ContinueCount == currentCount && IsContinueing)
                    {
                        Console.WriteLine($"Continuation timeout, ending process..");
                        Cancel();
                    }
                });
            }
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
                    RedirectStandardInput = true,
                    CreateNoWindow = true
                }
            };
            process.Exited += (a, b) => Active = false;
            Process = process;
            process.Start();
            Active = true;

            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (ProcessBlenderLine(line))
                    return;
            }

            process.WaitForExit();
        }

        public void Continue(string newPath)
        {
            lock (_continueLock)
            {
                if (!IsContinueing)
                    throw new InvalidOperationException("Attempting to continue a process that is not in continue state");
                IsContinueing = false;
            }
            Process.StandardInput.WriteLine(newPath);
            while (!Process.StandardOutput.EndOfStream)
            {
                var line = Process.StandardOutput.ReadLine();
                if (ProcessBlenderLine(line))
                    return;
            }

            Process.WaitForExit();
        }

        /// <summary>
        /// Cancel the process
        /// </summary>
        public void Cancel()
        {
            IsContinueing = false;
            Active = false;
            if (Process != null)
                Process.Kill();
        }


        /// <summary>
        /// Handles a Blender print line
        /// </summary>
        /// <param name="line"></param>
        private bool ProcessBlenderLine(string line)
        {
            Console.WriteLine(line);

            try
            {
                Match match = REGEX_Progress.Match(line);
                if(match == null || !match.Success || match.Groups.Count != 5)
                    match = REGEX_Progress2.Match(line);

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
                else if (line.StartsWith("AWAITING CONTINUE:"))
                {
                    HandleContinue();
                    return true;
                }

                OnBlenderOutput?.Invoke(line);
            }
            catch (Exception ex) { }
            return false;
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
