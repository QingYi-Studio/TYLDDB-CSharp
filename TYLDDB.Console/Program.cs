using System;
using TYLDDB;

namespace TYLDDBConsole
{
    internal class Program
    {
        internal static Settings consoleSettings;

        static Program()
        {
            consoleSettings = new Settings();
        }

        static void Main(string[] args)
        {
            Startup();
            if (args.Length > 0)
            {
                Console.WriteLine(args[0]);
            }
            else
            {
                if (consoleSettings.CommandMode == 0)
                {
                    Console.WriteLine("Use -h or --help to get the information.");
                }
                else if (consoleSettings.CommandMode == 1)
                {
                    Console.WriteLine("Use Get-Help to get the information.");
                }

                bool running = true;

                while (running)
                {
                    string commnadInput = Console.ReadLine()!;

                    if (commnadInput != null)
                    {
                        Console.WriteLine(commnadInput);
                        
                        if (commnadInput == "exit" || commnadInput == "Console-Exit")
                        {
                            running = false;
                        }
                    }
                }
            }
        }

        static void Startup()
        {
            var db = new LDDB();
            byte[] readingBuffer = new byte[1024];

            db.FilePath = "settings.lddb";
            db.ReadingFile(readingBuffer);
            db.LoadDatabase_V2("console");
            db.Parse_V1();
            if (db.AllTypeSearchFromSemaphoreThreadLock("command_mode")[0] == "cmd")
            {
                consoleSettings.CommandMode = 0;
            }
            else if(db.AllTypeSearchFromSemaphoreThreadLock("command_mode")[0] == "powershell")
            {
                consoleSettings.CommandMode = 1;
            }
            else
            {
                Console.WriteLine("""Reading mode error, please use "db settings root" or "db Root-Settings" to use the default template settings.""");
                consoleSettings.CommandMode = 2;
            }
            GC.Collect();
        }
    }
}
