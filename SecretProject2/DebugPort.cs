using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;
// Added:
using System.IO.Ports;
using System.Text;
using System.Collections;

namespace SecretProject2
{
    class DebugPort
    {
        #region [--- Variables ---]
        // Serial Port Parameters
        private static SerialPort ComPort;
        public bool IsOpen = false;

        // Byte Buffer Parameters
        private static int CurrentBufferPosition { get; set; }
        private static byte[] Buffer { get; set; }
        private static bool MsgPending = false;

        // Message Queue Parameters
        private static ArrayList Messages = new ArrayList();
        //private static bool BufferLocked = false;
        public int QueueLength = 0;

        private static bool DebugMode = false;
        #endregion

        #region [--- Constructor and Main ---]
        public DebugPort(string portname = SerialPorts.COM2, int boudrate = 115200, Parity parity = Parity.None, int databits = 8, StopBits stopbits = StopBits.One)
        {
            // Serial Port Initialization
            ComPort = new SerialPort(portname, boudrate, parity, databits, stopbits);
            ComPort.ReadTimeout = 1000; //=10ms
            ComPort.DataReceived += new SerialDataReceivedEventHandler(ComPort_DataReceived);

            // Buffer Initialization
            CurrentBufferPosition = 0;
            Buffer = new byte[UARTConsts.MAX_BUFFER_LENGTH];

            OpenPort();
        }

        // Thread main
        public void Run()
        {
            while (true)
            {
                ParseMessages();
                Thread.Sleep(UARTConsts.SHORT_SLEEP_MS);
            }
        }
#endregion

        #region [--- Exported Functions ---]
        public void OpenPort()
        {
            if (!ComPort.IsOpen)
            {
                ComPort.Open();
                TRACE("OpenPort", "COM2 opened");
            }

            IsOpen = ComPort.IsOpen;
        }

        public void Write(string msg)
        {
            System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
            byte[] bytesToSend = encoder.GetBytes(msg + "\r\n");
            ComPort.Write(bytesToSend, 0, bytesToSend.Length);
            TRACE("Write","Sending to PC [" + PrintSpecialChars(msg) + "]");
            Thread.Sleep(UARTConsts.SHORT_SLEEP_MS);
        }

        public string Pop()
        {
            if (Messages.Count > 0)
            {
                string msg = (string)Messages[0];
                Messages.RemoveAt(0);
                QueueLength--;
                TRACE("Pop", "Popped: [" + PrintSpecialChars(msg) + "]");
                return msg;
            }
            else
            {
                TRACE("Pop", "Queue Empty");
                return null;
            }
        }
#endregion
        
        #region [--- Internal Functions ---]
        private void ParseMessages()
        {
            if (!MsgPending) return;
            MsgPending = false;

            TRACE("ParseMessage", "Starting...");
            string text = ReadMessagesFromBuffer();

            string[] MsgList = text.Split(UARTConsts.DELIMETER);
            foreach (string msg in MsgList)
            {
                if (msg.Length > 0)
                {
                    Messages.Add(msg);
                    TRACE("ParseMessage", "Pushed: [" + PrintSpecialChars(msg) + "]");
                }
            }

            EraseBuffer();
            TRACE("ParseMessage", "Ended.");
            QueueLength = Messages.Count;
        }
        #endregion
        
        #region [--- Debug ---]
        private static string PrintSpecialChars(string text)
        {
            char[] array = text.ToCharArray();

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == '\r')
                    array[i] = 'r';
                else if (array[i] == '\n')
                    array[i] = 'n';
                else if (array[i] == UARTConsts.ESC)
                    array[i] = 'e';
            }

            return new string(array);
        }

        private static void TRACE(string func, string msg)
        {
            if (!DebugMode) return;
            TimeSpan ts = Microsoft.SPOT.Hardware.Utility.GetMachineTime();
            Debug.Print("[" + ts.ToString() + "] [PcConnection." + func + "()] " + msg);
        }

        public void SetDebugMode(bool debugmode)
        {
            DebugMode = debugmode;
            TRACE("SetDebugMode", "Debug mode set to " + debugmode.ToString());
        }
        #endregion

        #region [--- Buffer Functions ---]
        private static string ReadMessagesFromBuffer()
        {
            string text;
            try
            {
                text = new string(Encoding.UTF8.GetChars(Buffer));
                return text;
            }
            catch (Exception e)
            {
                Debug.Print("EXCEPTION: " + e.Message);
                return "";
            }
        }

        private static void EraseBuffer()
        {
            CurrentBufferPosition = 0;
        }
        #endregion

        #region [--- Interrupt Servie Routine ---]
        void ComPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            lock (Buffer)
            {
                int incoming_bytes = ComPort.BytesToRead;

                if (incoming_bytes == 0) return;

                // read message, only if you have enough space
                if (incoming_bytes + CurrentBufferPosition > UARTConsts.MAX_BUFFER_LENGTH)
                {
                    MsgPending = true;
                    return;
                }
                incoming_bytes = ComPort.Read(Buffer, CurrentBufferPosition, incoming_bytes);

                // filter out the crap: throw messages that end with '\0'
                if (Buffer[CurrentBufferPosition + incoming_bytes - 1] == UARTConsts.STRING_ENDER) return;

                CurrentBufferPosition += incoming_bytes;
                // prevent "echoes" due to residues
                Buffer[CurrentBufferPosition] = UARTConsts.STRING_ENDER; // ignore old data
                Buffer[CurrentBufferPosition + 1] = UARTConsts.STRING_ENDER; // ignore old data

                // message ender: '@\r\n' ; buffer full.
                if (CurrentBufferPosition >= 3)
                {
                    if ((Buffer[CurrentBufferPosition - 3] == (byte)'@') &&
                        (Buffer[CurrentBufferPosition - 2] == (byte)'\r') &&
                        (Buffer[CurrentBufferPosition - 1] == (byte)'\n'))
                    {
                        // Raise msg flag
                        MsgPending = true;
                        // remove CR, LF
                        Buffer[CurrentBufferPosition - 2] = UARTConsts.STRING_ENDER;
                    }
                }
            }

        }
        #endregion
    }

    class UARTConsts
    {
        public const int MAX_BUFFER_LENGTH = 1024;
        public const int SHORT_SLEEP_MS = 50;

        public const char DELIMETER = '@';
        public const byte STRING_ENDER = (byte)'\0';
        public const char ESC = '\x1b';
    }
}
