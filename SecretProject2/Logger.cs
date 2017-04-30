using System;
using Microsoft.SPOT;
using System.Collections;
using System.Text;
using System.Diagnostics;

namespace SecretProject2
{
    class Logger
    {
        private static ArrayList Log = new ArrayList();
        private static bool StopLogging = false;

        public static void Add(string msg)
        {
            if (StopLogging) return;

            uint FreeMem = Debug.GC(true);
            
            lock (Log)
            {
                if (FreeMem > Defines.MIN_FREE_MEM)
                {
                    Log.Add(msg);
                }
                else
                {
                    Log.Add("--- Out of memory ---");
                    StopLogging = true;
                }
            }

        }

        public static string Print()
        {
            if (Log.Count == 0)
                return "Log Empty.";

            string log = "---LOG START---\r\n";

            foreach (string line in Log)
            {
                log += line + "\r\n";
            }

            log += "---LOG END---";

            return log;
        }

        public static void Erase()
        {
            Log = new ArrayList();
            StopLogging = false;
        }

        public static string Pop()
        {
            string msg = (string)Log[0];
            lock (Log)
            {
                Log.RemoveAt(0);
            }
            return msg;
        }

        public static bool BufferFull()
        {
            return StopLogging;
        }

    }
}
