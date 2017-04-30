using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;

namespace SecretProject2
{
    class Led
    {
        #region [--- Variables ---]
        public enum LedState { Off, On, BlinkFast, BlinkSlow };
        private static OutputPort led = null;
        private static LedState state { get; set; }
        private static bool LastWrite = false;
        #endregion

        #region [--- Constructor and Main ---]
        public Led(Cpu.Pin pin = Pins.ONBOARD_LED)
        {
            led = new OutputPort(pin, false);
            state = LedState.Off;
        }

        public void Run()
        {
            while (true)
            {
                switch (state)
                {
                    case LedState.Off:
                        LedOff();
                        break;

                    case LedState.On:
                        LedOn();
                        break;

                    case LedState.BlinkFast:
                        LedBlinkFast();
                        break;

                    case LedState.BlinkSlow:
                        LedBlinkSlow();
                        break;

                    default:
                        break;
                }
                Thread.Sleep(LedConsts.POLLING_PERIOD_MS);   
            }
        }
        #endregion

        #region [--- Exported Functions ---]
        public void SetState(LedState new_state)
        {
            state = new_state;
        }
        #endregion

        #region [--- Internal functions ---]
        private void LedOff()
        {
            if (LastWrite == false) return;
            SetLed(false);
        }

        private void LedOn()
        {
            if (LastWrite == true) return;
            SetLed(true);
        }

        private void LedBlinkFast()
        {
            SetLed(!LastWrite);
            Thread.Sleep(LedConsts.BLINK_FAST);
        }

        private void LedBlinkSlow()
        {
            SetLed(!LastWrite);
            Thread.Sleep(LedConsts.BLINK_SLOW);
        }

        private void SetLed(bool value)
        {
            led.Write(value);
            LastWrite = value;
        }
        #endregion
    }

    class LedConsts
    {
        public const int POLLING_PERIOD_MS = 100;
        public const int BLINK_FAST = 2;
        public const int BLINK_SLOW = 10;
    }
}
