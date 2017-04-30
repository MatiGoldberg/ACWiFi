using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;

namespace SecretProject2
{
    // Humidity measurement range 20 - 90%, accuracy ±4% at 25°C, ±5% at full range.
    // Temperature measurement range 0 - 50°C, accuracy ±1-2°C.
    
    public class DHT11Sensor //: DHTSensor
    {    
        private bool disposed;
        private InterruptPort portIn;
        private TristatePort portOut;
        private float rhum; // Relative Humidity
        private float temp; // Temperature
        private long data;
        private long bitMask;
        private long lastTicks;
        private byte[] bytes = new byte[4];
        private AutoResetEvent dataReceived = new AutoResetEvent(false);
      
        // pin1 > The identifier for the sensor's data bus port
        // pin2 > The identifier for the sensor's data bus port
    
      
        public DHT11Sensor(Cpu.Pin pin1, Cpu.Pin pin2, Port.ResistorMode resistormode)// : base(pin1, pin2, pullUp)
        {
            var resistorMode = resistormode; // Use Disabled for External Pullup
            portIn = new InterruptPort(pin2, false, resistorMode, Port.InterruptMode.InterruptEdgeLow);
            portIn.OnInterrupt += new NativeEventHandler(portIn_OnInterrupt);
            portIn.DisableInterrupt();  // Enabled automatically in the previous call
            portOut = new TristatePort(pin1, true, false, resistorMode);

            if (!CheckPins())
            {
                throw new InvalidOperationException("DHT sensor pins are not connected together.");
            }
        }

        protected static int StartDelay { get { return 18; } }

        protected void Convert(byte[] data)
        {
            Debug.Assert(data != null);
            Debug.Assert(data.Length == 4);
            // DHT11 has 8-bit resolution, so the decimal part is always zero.
            Debug.Assert(data[1] == 0, "Humidity decimal part should be zero.");
            Debug.Assert(data[3] == 0, "Temperature decimal part should be zero.");

            Humidity    = (float)data[0];
            Temperature = (float)data[2];
        }  

              
        ~DHT11Sensor()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // true - release both managed and unmanaged resources;
        // false - release only unmanaged resources.
        [MethodImpl(MethodImplOptions.Synchronized)]
        protected void Dispose(bool disposing)
        {
            if(!disposed)
            {
                try
                {
                    portIn.Dispose();
                    portOut.Dispose();
                }
                finally
                {
                    disposed = true;
                }
            }
        }

        public float Temperature
        {
            get { return temp; }
            protected set { temp = value; }
        }

        public float Humidity
        {
        get { return rhum; }
        protected set { rhum = value; }
        }

        public bool Read()
        {
            if(disposed)
            {
                throw new ObjectDisposedException();
            }
            // The 'bitMask' also serves as edge counter: data bit edges plus
            // extra ones at the beginning of the communication (presence pulse).
            bitMask = 1L << 42;
            data = 0;

            // Initiate communication
            portOut.Active = true;
            portOut.Write(false);       // Pull bus low
            Thread.Sleep(StartDelay);
            portIn.EnableInterrupt();   // Turn on the receiver
            portOut.Active = false;     // Release bus

            bool dataValid = false;

            // Now the interrupt handler is getting called on each falling edge.
            // The communication takes up to 5 ms, but the interrupt handler managed
            // code takes longer to execute than is the duration of sensor pulse
            // (interrupts are queued), so we must wait for the last one to finish
            // and signal completion. 20 ms should be enough, 50 ms is safe.
            if(dataReceived.WaitOne(50, false))
            {
                bytes[0] = (byte)((data >> 32) & 0xFF);
                bytes[1] = (byte)((data >> 24) & 0xFF);
                bytes[2] = (byte)((data >> 16) & 0xFF);
                bytes[3] = (byte)((data >>  8) & 0xFF);

                byte checksum = (byte)(bytes[0] + bytes[1] + bytes[2] + bytes[3]);
                if(checksum == (byte)(data & 0xFF))
                {
                    dataValid = true;
                    Convert(bytes);
                }
                else
                {
                    Debug.Print("DHT sensor data has invalid checksum.");
                }
            }
            else
            {
                portIn.DisableInterrupt();  // Stop receiver
                Debug.Print("DHT sensor data timeout.");  
            }
            return dataValid;    
        }

        // If the received data has invalid checksum too often, adjust this value
        // based on the actual sensor pulse durations. It may be a little bit
        // tricky, because the resolution of system clock is only 21.33 µs.
        private const long BitThreshold = 1050;

        private void portIn_OnInterrupt(uint pin, uint state, DateTime time)
        {
            var ticks = time.Ticks;
            if((ticks - lastTicks) > BitThreshold)
            {
                // If the time between edges exceeds threshold, it is bit '1'
                data |= bitMask;
            }
      
            if((bitMask >>= 1) == 0)
            {
                // Received the last edge, stop and signal completion
                portIn.DisableInterrupt();
                dataReceived.Set();
            }
            lastTicks = ticks;
        }

        // Returns true if the ports are wired together, otherwise false.
        private bool CheckPins()
        {
            Debug.Assert(portIn != null, "Input port should not be null.");
            Debug.Assert(portOut != null, "Output port should not be null.");
            Debug.Assert(!portOut.Active, "Output port should not be active.");

            portOut.Active = true;  // Switch to output
            portOut.Write(false);
            var expectedFalse = portIn.Read();
            portOut.Active = false; // Switch to input
            var expectedTrue = portIn.Read();
            return (expectedTrue && !expectedFalse);
        }
  }
}
