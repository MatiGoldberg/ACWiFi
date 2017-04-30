using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;

namespace SecretProject2
{
    class RemoteParameters
    {
        public const Int32 PAUSE_MS = 500; // TODO: Set this to Optimal Value
    }
    
    class ACRemote
    {
        #region [--- Variables ---]
        private static Int32 PauseMs = RemoteParameters.PAUSE_MS;
        
        public enum ACState { On, Off };
        public enum ACFanState { Low, Medium, High, Auto };
        public enum ACMode { Freeze, Auto, Due, Fan, Heat };
        public enum ACSwingState { On, Off };

        private static int AcTemp = 18; //degC
        private static ACState State = ACState.Off;
        private static ACMode Mode = ACMode.Freeze;
        private static ACFanState FanState = ACFanState.Low;
        private static ACSwingState SwingState = ACSwingState.Off;

        // Buttom {5-8} address //
        static OutputPort B0 = null;
        static OutputPort B1 = null;

        // Top {9-11} address //
        static OutputPort T0 = null;
        static OutputPort T1 = null;

        private static bool DebugMode = false;
        #endregion

        #region [--- Main ---]
        // --- Constructor ----------------------------------------------- //
        public ACRemote(Cpu.Pin b0 = Pins.GPIO_PIN_D5,
                        Cpu.Pin b1 = Pins.GPIO_PIN_D4,
                        Cpu.Pin t0 = Pins.GPIO_PIN_D11,
                        Cpu.Pin t1 = Pins.GPIO_PIN_D10,
                        bool debug_mode = false)
        {
            // Set Top (Mux2) and Buttom (Mux1) Address Pins //
            B0 = new OutputPort(b0, false);
            B1 = new OutputPort(b1, false);
            T0 = new OutputPort(t0, true);
            T1 = new OutputPort(t1, true);

            // set debug mode
            DebugMode = debug_mode;

            SetNeutralState();
        }

        // --- Thread Main ----------------------------------------------- //
        /*
        public void Run()
        {
            // The AC Thread works in Interrupts only //
            SetNeutralState();
            Thread.Sleep(Timeout.Infinite);
        }
        */
        #endregion

        #region [--- Exported functions ---]
        // --- Exported Functions ---------------------------------------- //
        public void SetTemperatureTo(int DestTemp)
        {
            TRACE("SetTemperatureTo", "Setting temperature {" + AcTemp.ToString() + " --> " + DestTemp.ToString() + "}");
            while (DestTemp != AcTemp)
            {
                if (DestTemp > AcTemp)
                {
                    PushTempUp();
                    Thread.Sleep(PauseMs);
                }
                else if (DestTemp < AcTemp)
                {
                    PushTempDown();
                    Thread.Sleep(PauseMs);
                }
            }
            // Else do nothing //
            Debug.Print(">> AC Temperature has been set to " + DestTemp.ToString() + "degC.");
            PushSend();
        }

        public void SetFanTo(ACFanState fanState)
        {
            TRACE("SetFanTo", "Setting fan state {" + GetFanStateName(FanState) + " --> " + GetFanStateName(fanState) + "}");
            while (FanState != fanState)
            {
                PushFan();
                Thread.Sleep(PauseMs);
            }
            Debug.Print(">> Fan state had been set to " + GetFanStateName(fanState) + ".");
            PushSend();
        }

        public void SetModeTo(ACMode mode)
        {
            TRACE("SetModeTo", "Setting fan state {" + GetModeName(Mode) + " --> " + GetModeName(mode) + "}");
            while (Mode != mode)
            {
                PushMode();
                Thread.Sleep(PauseMs);
            }
            Debug.Print(">> Mode had been set to " + GetModeName(Mode) + ".");
            PushSend();
        }

        public void TurnAcOff()
        {
            Debug.Print(">> AC Turned Off.");
            PushOff();
        }

        // TODO: Add additional AC functions //
        #endregion

        #region [--- DEBUG Functions ---]
        // ~~ DEBUG Functionality ~~
        public ACState GetState()
        {
            return State;
        }
        
        public Int32 GetACTemp()
        {
            return AcTemp;
        }

        public void SetAcTemp(Int32 val)
        {
            AcTemp = val;
        }

        public void TempIncrement()
        {
            PushTempUp();
        }

        public void TempDecrement()
        {
            PushTempDown();
        }

        public void IncrementMode()
        {
            PushMode();
        }

        public void IncrementDelay(Int32 value)
        {
            PauseMs += value;
        }

        public Int32 GetDelay()
        {
            return PauseMs;
        }

        public void SetDebugMode(bool mode)
        {
            DebugMode = mode;
        }
        #endregion

        #region [--- Private Functions ---]
        // --- Private Functions ------------------------------------------ //
        private static string GetFanStateName(ACFanState fanState)
        {
            string[] names = { "Low", "Medium", "High", "Auto" };
            return names[(Int32)fanState];
        }

        private static string GetModeName(ACMode mode)
        {
            string[] names = { "Freeze", "Auto", "Due", "Fan", "Heat" };
            return names[(Int32)mode];
        }

        private static void TRACE(string func, string msg)
        {
            if (!DebugMode) return;
            TimeSpan ts = Microsoft.SPOT.Hardware.Utility.GetMachineTime();
            Debug.Print("[" + ts.ToString() + "] [ACRemote." + func + "()] " + msg);
        }
        #endregion

        #region [--- Button Functions ---]
        // --- Button Functions ------------------------------------------- //
        private void PushSend()
        {
            // S1 = 11+8 = 2Y2 + 1Y3
            SetMultiplexers(2, 3);
            Thread.Sleep(PauseMs);
            SetNeutralState();
            State = ACState.On;
            TRACE("PushSend", "called.");
        }

        private void PushFan()
        {
            // S2 = 11+7 = 2Y2 + 1Y2
            SetMultiplexers(2, 2);
            Thread.Sleep(PauseMs);
            SetNeutralState();
            
            if (FanState == ACFanState.Auto)
                FanState = ACFanState.Low;
            else
                FanState++;
            
        }

        private void PushMode()
        {
            // S3 = 11+6 = 2Y2 + 1Y1
            SetMultiplexers(2, 1);
            Thread.Sleep(PauseMs);
            SetNeutralState();

            if (Mode == ACMode.Heat)
                Mode = ACMode.Freeze;
            else
                Mode++;
        }

        private void PushClkPlus()
        {
            // S4 = 11+5 = 2Y2 + 1Y0
            SetMultiplexers(2, 0);
            Thread.Sleep(PauseMs);
            SetNeutralState();
        }

        private void PushClkMinus()
        {
            // S5 = 10+8 = 2Y1 + 1Y3
            SetMultiplexers(1, 3);
            Thread.Sleep(PauseMs);
            SetNeutralState();
        }

        private void PushClkTime()
        {
            // S6 = 10+7 = 2Y1 + 1Y2
            SetMultiplexers(1, 2);
            Thread.Sleep(PauseMs);
            SetNeutralState();
        }

        private void PushTempDown()
        {
            // S7 = 10+6 = 2Y1 + 1Y1
            SetMultiplexers(1, 1);
            Thread.Sleep(PauseMs);
            SetNeutralState();
            AcTemp--;
        }

        private void PushTempUp()
        {
            // S8 = 10+5 = 2Y1 + 1Y0
            SetMultiplexers(1, 0);
            Thread.Sleep(PauseMs);
            SetNeutralState();
            AcTemp++;
        }

        public void PushOff()
        {
            // S9 = 9+8 = 2Y0 + 1Y3
            SetMultiplexers(0, 3);
            Thread.Sleep(PauseMs);
            SetNeutralState();

            State = ACState.Off;
            TRACE("PushOff", "called.");
        }

        private void PushSwing()
        {
            // S10 = 9+7 = 2Y0 + 1Y2
            SetMultiplexers(0, 2);
            Thread.Sleep(PauseMs);
            SetNeutralState();

            if (SwingState == ACSwingState.Off)
                SwingState = ACSwingState.On;
            else
                SwingState = ACSwingState.Off;
        }

        private void PushSleep()
        {
            // S11 = 9+6 = 2Y0 + 1Y1
            SetMultiplexers(0, 1);
            Thread.Sleep(PauseMs);
            SetNeutralState();
        }

        private void SetNeutralState()
        {
            // 2Y3 + 1Y0 is not connected //
            SetMultiplexers(3, 0);
        }

        private void SetMultiplexers(int top, int buttom)
        {
            Debug.Assert(top < 4);
            Debug.Assert(buttom < 4);

            switch (buttom)
            {
                case 0:
                    B0.Write(false);
                    B1.Write(false);
                    break;
                case 1:
                    B0.Write(true);
                    B1.Write(false);
                    break;
                case 2:
                    B0.Write(false);
                    B1.Write(true);
                    break;
                case 3:
                    B0.Write(true);
                    B1.Write(true);
                    break;
                default:
                    throw new IndexOutOfRangeException();
            }
            
            switch (top)
            {
                case 0:
                    T0.Write(false);
                    T1.Write(false);
                    break;
                case 1:
                    T0.Write(true);
                    T1.Write(false);
                    break;
                case 2:
                    T0.Write(false);
                    T1.Write(true);
                    break;
                case 3:
                    T0.Write(true);
                    T1.Write(true);
                    break;
                default:
                    throw new IndexOutOfRangeException();
            }
        }
        #endregion
    }
    
}
