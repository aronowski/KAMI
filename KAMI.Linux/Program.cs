using KAMI.Core;
using System;
using System.Threading;

namespace KAMI.Linux
{
    class Program
    {
        static int Main(string[] args)
        {
            string configPath = null;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--config")
                {
                    configPath = args[i + 1];
                    break;
                }
            }

            KAMICore kami;
            try
            {
                kami = new KAMICore(
                    ex => Console.Error.WriteLine(ex),
                    configPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Startup failed: {ex.Message}");
                return 1;
            }

            bool stopping = false;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                if (stopping) return;
                stopping = true;
                Console.WriteLine("\nShutting down...");
                kami.Stop();
                Environment.Exit(0);
            };

            kami.Start();

            while (true)
            {
                string line = kami.Status switch
                {
                    KAMIStatus.Unconnected => "Waiting for emulator...",
                    KAMIStatus.Connected   => "Emulator connected - no game loaded.",
                    KAMIStatus.Ready       => "Game loaded - press toggle key to inject.",
                    KAMIStatus.Injecting   => "Injecting mouse input.",
                    _                      => kami.Status.ToString(),
                };
                Console.Write($"\r[{kami.Status,-12}] {line,-50}");
                Thread.Sleep(500);
            }
        }
    }
}
