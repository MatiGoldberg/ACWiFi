using System;
using Microsoft.SPOT;
using System.Threading;

namespace SecretProject2
{
    class ACControl
    {
        #region [--- Variables ---]
        private static double CurrentRoomTemp { get; set; }
        private static double TempRate { get; set; }
        private static double DestTemp { get; set; }
        private static TMP36 TempSensor = new TMP36(TMP36.Channel.GPIO_PIN_A0);
        private static ACRemote RemoteControl = new ACRemote();

        private static bool ACOn = false;
        private static StopWatch stopwatch = new StopWatch();
        #endregion

        #region [--- Constructors ---]
        public void Run()
        {
            SampleTemp();
                        
            while (true)
            {
                Thread.Sleep(ACConst.POLLING_PERIOD_MS);
                if (!ACOn) continue;

                SampleTemp();
                ExecuteLogic();
            }
        }
        
        #endregion

        #region [--- Exported Functions ---]
        public static void SetRoomTempTo(double dest_temp)
        {
            SetAcState(true);
            DestTemp = dest_temp;
        }

        public static void TurnAcOff()
        {
            SetAcState(false);
        }
       
        public static string GetOnTime()
        {
            return "System ON time: " + stopwatch.ElapsedTime();
        }

        // ~~ DEBUG ~~
        public static double GetCurrentRoomTemp()
        {
            return CurrentRoomTemp;
        }

        public static double GetTempRate()
        {
            return TempRate;
        }
        #endregion

        #region [--- Internal Functions ---]
        
        private static void SetAcState(bool state)
        {
            ACOn = state;

            if (state == true)
                stopwatch.Start();
            else
                stopwatch.Stop();
        }
        
        private static void SampleTemp()
        {
            double LastRoomTemp = CurrentRoomTemp;
            CurrentRoomTemp = TempSensor.ReadValue();
            TempRate = (CurrentRoomTemp - LastRoomTemp) / (ACConst.POLLING_PERIOD_MS / 1000);
        }


        /// <summary>
        /// Simple 3 threshold logic: Far up, Far down, close.
        /// </summary>
        private static void ExecuteLogic()
        {
            double TempDiff = CurrentRoomTemp - DestTemp;
            
            if (TempDiff > ACConst.CLOSE_TO_DEST_TH)
            {
                // AC on full blast
                RemoteControl.SetFanTo(ACRemote.ACFanState.High);
                RemoteControl.SetAcTemp(10);
            }

            else if (TempDiff < -ACConst.CLOSE_TO_DEST_TH)
            {
                RemoteControl.PushOff();
            }

            else //(System.Math.Abs(TempDiff) < ACConst.CLOSE_TO_DEST_TH)
            {
                // AC on min
                RemoteControl.SetFanTo(ACRemote.ACFanState.Low);
                RemoteControl.SetAcTemp(18);
            }

        }
        #endregion
    }

    class ACConst
    {
        public const int POLLING_PERIOD_MS = 5000;

        public const double CLOSE_TO_DEST_TH = 1.0;
    }
}
