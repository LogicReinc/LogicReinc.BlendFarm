using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace LogicReinc.BlendFarm.Server
{
    public static class Utility
    {
        public static void WaitAndPrint(this ProcessStartInfo start)
        {
            Process process = new Process()
            {
                StartInfo = start
            };

            process.Start();

            while (!process.StandardOutput.EndOfStream)
            {
                var line = process.StandardOutput.ReadLine();
                Console.WriteLine(line);
            }

            process.WaitForExit();
        }
    }
}
