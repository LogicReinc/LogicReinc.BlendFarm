using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
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
        /// Set once Cancel is called, a cancelled process is never (re)started or continued
        /// </summary>
        public bool IsCancelled => _cancelled;
        private volatile bool _cancelled = false;

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

        /// <summary>
        /// Last time Blender printed anything, used by the watchdog
        /// </summary>
        public DateTime LastOutput { get; private set; } = DateTime.Now;
        private System.Threading.Timer _watchdog = null;

        public List<string> Results { get; private set; } = new List<string>();
        public List<string> Exceptions { get; private set; } = new List<string>();

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
            int currentCount = 0;

            lock (_continueLock)
            {
                IsContinueing = !_cancelled;
                currentCount = ContinueCount + 1;
                ContinueCount = currentCount;
            }
            OnBlenderContinue?.Invoke(this);
            if (CONTINUE_TIMEOUT == 0)
                Cancel();
            else
            {
                Task.Delay(CONTINUE_TIMEOUT).ContinueWith((x) =>
                {
                    //Same lock as Continue, so a continuation can't start between the check and the kill
                    lock (_continueLock)
                    {
                        if (ContinueCount == currentCount && IsContinueing)
                        {
                            Console.WriteLine($"Continuation timeout, ending process..");
                            Cancel();
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Starts the process and handle output
        /// </summary>
        public Result Run()
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
            process.EnableRaisingEvents = true;
            process.Exited += (a, b) => Active = false;
            if (_cancelled)
                return new Result()
                {
                    Results = Results.ToArray(),
                    Exceptions = Exceptions.ToArray()
                };
            Process = process;
            process.Start();
            Active = true;
            //A cancel that raced with the start could not kill the process yet
            if (_cancelled)
                Cancel();
            LastOutput = DateTime.Now;
            StartWatchdog();

            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                if (ProcessBlenderLine(line))
                    return new Result()
                    {
                        Results = Results.ToArray(),
                        Exceptions = Exceptions.ToArray()
                    };
            }

            process.WaitForExit();
            _watchdog?.Dispose();
            _watchdog = null;
            return new Result()
            {
                Results = Results.ToArray(),
                Exceptions = Exceptions.ToArray()
            };
        }

        public void Continue(string newPath)
        {
            lock (_continueLock)
            {
                if (!IsContinueing || _cancelled)
                    throw new InvalidOperationException("Attempting to continue a process that is not in continue state");
                IsContinueing = false;
            }
            LastOutput = DateTime.Now;
            Process.StandardInput.WriteLine(newPath);
            while (!Process.StandardOutput.EndOfStream)
            {
                var line = Process.StandardOutput.ReadLine();
                if (ProcessBlenderLine(line))
                    return;
            }

            Process.WaitForExit();
            _watchdog?.Dispose();
            _watchdog = null;
        }

        /// <summary>
        /// Cancel the process
        /// </summary>
        public void Cancel()
        {
            _cancelled = true;
            IsContinueing = false;
            Active = false;
            _watchdog?.Dispose();
            _watchdog = null;
            try
            {
                if (Process != null && !Process.HasExited)
                    Process.Kill(true);
            }
            catch (InvalidOperationException) { } //Already exited
        }

        /// <summary>
        /// Kills Blender when it stops producing output while rendering
        /// </summary>
        private void StartWatchdog()
        {
            int minutes = ServerSettings.Instance.WatchdogMinutes;
            if (minutes <= 0)
                return;
            _watchdog?.Dispose();
            _watchdog = new System.Threading.Timer((state) =>
            {
                //Waiting for a continuation is expected silence, that has its own timeout
                if (!Active || IsContinueing)
                    return;
                if (DateTime.Now.Subtract(LastOutput).TotalMinutes >= minutes)
                {
                    string msg = $"Watchdog: Blender produced no output for {minutes} minutes, killing it";
                    Console.WriteLine(msg);
                    Exceptions.Add(msg);
                    OnBlenderException?.Invoke(msg);
                    Cancel();
                }
            }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        }


        /// <summary>
        /// Handles a Blender print line
        /// </summary>
        /// <param name="line"></param>
        private bool ProcessBlenderLine(string line)
        {
            LastOutput = DateTime.Now;
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
                {
                    string exception = line.Substring("EXCEPTION:".Length);
                    OnBlenderException?.Invoke(exception);
                    Exceptions.Add(exception);
                }
                else if (line.StartsWith("SUCCESS:"))
                {
                    string result = line.Substring("SUCCESS:".Length);
                    OnBlenderCompleteTask?.Invoke(result);
                    Results.Add(result);
                }
                else if (line.StartsWith("AWAITING CONTINUE:"))
                {
                    HandleContinue();
                    return true;
                }

                OnBlenderOutput?.Invoke(line);
            }
            catch (Exception ex)
            {
                //Handlers (eg. sending results) run on the output thread, don't let them stop output processing
                Console.WriteLine("Exception handling Blender output: " + ex.Message);
            }
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

        public class Result
        {
            public string[] Results { get; set; }
            public string[] Exceptions { get; set; }
        }
    }
}
