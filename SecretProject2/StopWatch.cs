using System;
using Microsoft.SPOT;

namespace SecretProject2
{
    class StopWatch
    {
        private TimeSpan StartTime { get; set; }
        private TimeSpan RunTime { get; set; }
        private bool Running {get; set;}

        public StopWatch()
        {
            StartTime = Now();
            RunTime = TimeSpan.Zero;
            Running = false;
        }

        public void Start()
        {
            StartTime = Now();
            Running = true;
        }

        public void Stop()
        {
            Running = false;
            RunTime = Now() - StartTime;
        }

        public string ElapsedTime()
        {
            TimeSpan elapsedtime;
            string text = "";

            if (Running)
            {
                elapsedtime = Now() - StartTime;
            }
            else
            {
                elapsedtime = RunTime;
            }

            if (elapsedtime.Days > 0)
                text += elapsedtime.Days.ToString() + "d";
            if (elapsedtime.Hours > 0)
                text += elapsedtime.Hours.ToString() + "h";
            if (elapsedtime.Minutes > 0)
                text += elapsedtime.Minutes.ToString() + "m";
            if (elapsedtime.Seconds > 0)
                text += elapsedtime.Seconds.ToString() + "s";

            return text;
        }

        private TimeSpan Now()
        {
            return Microsoft.SPOT.Hardware.Utility.GetMachineTime();
        }

    }
}
