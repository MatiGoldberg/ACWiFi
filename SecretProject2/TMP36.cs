using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;

namespace SecretProject2
{
    class TMP36
    {
        public enum Channel { GPIO_PIN_A0, GPIO_PIN_A1, GPIO_PIN_A2, GPIO_PIN_A3, GPIO_PIN_A4, GPIO_PIN_A5 };
        private AnalogInput Sensor;
        private double Temperature = 25;

        private double Alpha {get; set;}
        private const int PERIOD_MS = 50;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="channel"></param>
        public TMP36(Channel channel)
        {
            switch (channel)
            {
                case Channel.GPIO_PIN_A0:
                    Sensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A0);
                    break;

                case Channel.GPIO_PIN_A1:
                    Sensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A1);
                    break;

                case Channel.GPIO_PIN_A2:
                    Sensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A2);
                    break;

                case Channel.GPIO_PIN_A3:
                    Sensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A3);
                    break;

                case Channel.GPIO_PIN_A4:
                    Sensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A4);
                    break;

                case Channel.GPIO_PIN_A5:
                    Sensor = new AnalogInput(AnalogChannels.ANALOG_PIN_A5);
                    break;

                default:
                    throw new ArgumentException("Invalid Channel (0-5)");
            }

            Alpha = 0.01;
            
        }

        public void Run()
        {
            while (true)
            {
                EstimateValue();
                Thread.Sleep(PERIOD_MS);
            }
        }

        private void EstimateValue()
        {
            double currentValue = Convert(Sensor.Read());
            Temperature = currentValue * Alpha + Temperature * (1 - Alpha);
        }

        public bool SetAlpha(double alpha)
        {
            if ((Alpha > 1) || (Alpha < 0)) return false;

            Alpha = alpha;
            return true;
        }

        /// <summary>
        /// Returns Sensor filtered value
        /// </summary>
        /// <returns></returns>
        public double ReadValue()
        {
            //Temperature = Convert(Sensor.Read());
            return Temperature;
        }

        /// <summary>
        /// Reads the current RAW value of the temp sensor.
        /// </summary>
        /// <returns></returns>
        public double ReadCurrent()
        {
            return Convert(Sensor.Read());
        }

        /// <summary>
        /// Return temperature as String.
        /// </summary>
        /// <returns></returns>
        public string StringValue(int digits = 1)
        {
           // Temperature = Convert(Sensor.Read());
            return RoundUp(Temperature.ToString(), digits);
        }

        private double Convert(double val)
        {
            double millivolts = val * 3300.0; // 3.3v 
            return (millivolts - 500) / 10; // in Celcius
        }

        private string RoundUp(string text, int digits = 1)
        {
            int index = text.IndexOf('.');
            return text.Substring(0, index + 1 + digits); ;
        }
    }
}
