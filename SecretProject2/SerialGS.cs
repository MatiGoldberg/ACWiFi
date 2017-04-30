using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;
// ADDED:
using System.IO.Ports;
using System.Text;
using System.Collections;

namespace SecretProject2
{
    class SerialGS
    {
        #region [--- Class Variables --]
        enum CmdState { Ready, MsgSent, PendingReply, Incomplete }
        
        private static int CurrentBufferPosition { get; set; }
        private static byte[] Buffer { get; set; }
        private static bool MsgPending = false;
        private static CmdState MsgState = CmdState.Ready;

        private static ArrayList Messages = new ArrayList();
        private static bool BufferLocked = false;

        private static SerialPort serialPort;
        private static bool DebugMode;
        #endregion

        #region [--- Constructors ---]
        public SerialGS(string portName = SerialPorts.COM1, int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One, bool debug_mode = false)
        {
            // Serial Port Configuration
            serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits);
            serialPort.ReadTimeout = 1000; // Set to 10ms.
            serialPort.DataReceived += new SerialDataReceivedEventHandler(SerialGS_DataReceived);

            // Buffer Configuration //
            CurrentBufferPosition = 0;
            Buffer = new byte[SerialConst.MAX_MESSAGE_LEN];

            // Debug mode
            DebugMode = debug_mode;
        }

        public void Run()
        {
            while (true)
            {
                ParseMessage();
                Thread.Sleep(SerialConst.SHORT_SLEEP_MS);
            }
        }
        # endregion

        #region [--- Exported Functions ---]
        public void OpenSerialPort()
        {
            if (!serialPort.IsOpen)
            {
                serialPort.Open();
            }
        }

        public void Write(string msg, bool verbose = false)
        {
            System.Text.UTF8Encoding encoder = new System.Text.UTF8Encoding();
            byte[] bytesToSend = encoder.GetBytes(msg + '\r');
            serialPort.Write(bytesToSend, 0, bytesToSend.Length);
            ChangeCmdStateTo(CmdState.MsgSent);
            
            if (verbose)
                TRACE("Write", "TX>> [" + PrintSpecialChars(msg) + "]");

            Thread.Sleep(SerialConst.SHORT_SLEEP_MS);
        }

        public string[] ReadAndEraseAll()
        {
            if (BufferLocked) throw new Exception("ReadAndEraseAll() cannot be called when buffer is locked.");
            TRACE("ReadAndEraseAll", "Reading {" + Messages.Count.ToString() + "} messages.");
            string[] messages = (string[])Messages.ToArray(typeof(string));
            EmptyMsgBuffer();
            return messages;
        }
        
        public string[] ReadAll()
        {
            // This function returns all messages sotred in message buffer without deleting it //
            // the MessageBuffer is LOCKED until a single call to POP is made //

            TRACE("ReadAll","Reading {" + Messages.Count.ToString() + "} messages.");
            BufferLocked = true;
            return (string[])Messages.ToArray(typeof(string));
        }

        public void Pop(int index = -1, bool verbose = false)
        {
            if (index == -1)
            {
                // only unlock buffer
                BufferLocked = false;
                if (verbose)
                    TRACE("Pop", "no item was removed (-1)");

                return;
            }
            
            if (index < Messages.Count)
            {
                if (verbose)
                    TRACE("Pop", "Removing [" + PrintSpecialChars((string)Messages[index]) + "]");
                Messages.RemoveAt(index);
            }
            else
            {
                TRACE("Pop", "ERROR: Index out of range.");
            }
            BufferLocked = false;
        }

        public string Read(bool verbose = false)
        {
            if (BufferLocked) throw new Exception("Read() cannot be called when buffer is locked");
            
            string msg = "No Reply";

            if (MessagePending())
            {
                // FIFO
                msg = (string)Messages[0];
                Messages.RemoveAt(0);

                if (verbose)
                {
                    TRACE("Read","RX>> [" + PrintSpecialChars(msg) + "]");

                    if (MessagePending())
                    {
                        TRACE("Read","Pending Messages {" + Messages.Count.ToString() + "}");
                    }
                }
            }

            return msg;
        }

        public void EmptyMsgBuffer()
        {
            if (BufferLocked) return;
            Messages = new ArrayList();
        }

        public bool MessagePending()
        {
            return (Messages.Count > 0);
        }

        public void SetDebugMode(bool debugmode)
        {
            DebugMode = debugmode;
            TRACE("SetDebugMode", "Debug mode set.");
        }

        public void RestartClass()
        {
            EraseBuffer();
            EmptyMsgBuffer();
            MsgState = CmdState.Ready;
            MsgPending = false;
            BufferLocked = false;
        }

        // --- Reply Control --- //
        public bool WaitingForReply()
        {
            return (MsgState > CmdState.Ready);
        }
        #endregion

        #region [--- Debug Aide ---]
        private static string GetCmdStateName(CmdState cmdState)
        {
            string[] CmdStateNames = { "Ready", "MsgSent", "PendingReply", "Incomplete" };
            return CmdStateNames[(Int32)cmdState];
        }

        private static void ChangeCmdStateTo(CmdState NewCmdState, bool verbose = false)
        {
            if (MsgState == NewCmdState) return;
            if (verbose)
                TRACE("ChangeCmdStateTo","# Msg SubState Change: " + GetCmdStateName(MsgState) + " --> " + GetCmdStateName(NewCmdState) + " #");
            MsgState = NewCmdState;
        }
        #endregion

        #region [--- Parsing ---]
        private void ParseMessage()
        {
            if (MsgPending) // Bool used since Emun cannot be changed in Interrupt
            {
                ChangeCmdStateTo(CmdState.PendingReply);
                MsgPending = false;
            }
            else
            {
                return;
            }
            if (BufferLocked) return;

            TRACE("ParseMessage", "Starting...");
            int s = 0, e = 3;

            string text = ReadMessagesFromBuffer();
            TRACE("ReadMessagesFromBuffer","MsgBuffer: [" + PrintSpecialChars(text) + "]");

            while ((s < text.Length - 1))
            {
                // Data Messages (<ESC>O, <ESC>S, <ESC>F, ...)
                if (text[s] == '\x1b')
                {
                    e = text.IndexOf('\x1b', s + 2);
                    if (e != -1)
                    {
                        TRACE("ParseMessage","Added: RX [" + PrintSpecialChars(text.Substring(s, e - s + 2)) + "]");
                        Messages.Add(text.Substring(s, e - s + 2));
                        s = e + 2;
                    }
                    else
                    {
                        TRACE("ParseMessage","--Incomplete Data Msg--");
                        ChangeCmdStateTo(CmdState.Incomplete);
                        break;
                    }
                }
                // AT Commands
                else
                {

                    string msg_ender = FindFirstMsgEnder(text.Substring(s));
                    if (msg_ender == null)
                    {
                        if (text.Substring(s) == "\r\r\n")
                        {
                            TRACE("ParseMessage", "rejecting rrn");
                            break;
                        }
                        
                        TRACE("ParseMessage", "Incomplete: [" + PrintSpecialChars(text.Substring(s)) + "]");
                        ChangeCmdStateTo(CmdState.Incomplete);
                        break;
                    }
                    
                    e = text.IndexOf(msg_ender, s);
                    if (e == s)
                    {
                        TRACE("ParseMessage", "--Empty AT Msg--");
                        s += msg_ender.Length;
                    }
                    else
                    {
                        TRACE("ParseMessage", "Added: RX [" + PrintSpecialChars(text.Substring(s, e - s + msg_ender.Length)) + "]");
                        Messages.Add(text.Substring(s, e - s + msg_ender.Length));
                        s = e + msg_ender.Length;
                    }

                }

            }

            if (MsgState != CmdState.Incomplete)
            {
                EraseBuffer();
                ChangeCmdStateTo(CmdState.Ready);
            }
            TRACE("ParseMessage", "Ended.");
        }

        private static string FindFirstMsgEnder(string msg)
        {
            string[] AT_MESSAGE_ENDERS = new string[] { "\r\n\n\r\n", "\r\nERROR\r\n", "\r\nOK\r\n", "\r\nERROR: INVALID CID\r\n", "\r\nERROR: INVALID INPUT\r\n", "DISCONNECT 0\r\n", "DISCONNECT 1\r\n", "Battery)\r\n\r\n" };
            int i, first_index = msg.Length;
            string first_msg_ender = null;

            foreach (string msg_ender in AT_MESSAGE_ENDERS)
            {
                i = msg.IndexOf(msg_ender);
                if ((i >= 0) && (i < first_index))
                {
                    first_index = i;
                    first_msg_ender = msg_ender;
                }
            }

            return first_msg_ender;
        }

        private static string PrintSpecialChars(string text)
        {
            char[] array = text.ToCharArray();

            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == '\r')
                    array[i] = 'r';
                else if (array[i] == '\n')
                    array[i] = 'n';
                else if (array[i] == Const.ESC)
                    array[i] = 'e';
            }

            return new string(array);
        }
        #endregion

        #region [--- Buffer ---]
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

        private static void TRACE(string func, string msg)
        {
            if (!DebugMode) return;
            TimeSpan ts = Microsoft.SPOT.Hardware.Utility.GetMachineTime();
            Debug.Print("[" + ts.ToString() + "] [SerialGS." + func + "()] " + msg);
        }
        #endregion

        #region [--- Interrupt Serice Routine ---]
        // "If your interrupt service routine is small, make it smaller" //
        private void SerialGS_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            lock (Buffer)
            {
                int incoming_bytes = serialPort.BytesToRead;

                if (incoming_bytes == 0) return;

                // read message, only if you have enough space
                if (incoming_bytes + CurrentBufferPosition > SerialConst.MAX_MESSAGE_LEN)
                {
                    //MsgPending = true;
                    MsgState = CmdState.PendingReply;
                    return;
                }
                incoming_bytes = serialPort.Read(Buffer, CurrentBufferPosition, incoming_bytes);

                // filter out the crap: throw messages that end with '\0'
                if (Buffer[CurrentBufferPosition + incoming_bytes - 1] == SerialConst.STRING_ENDER) return;

                CurrentBufferPosition += incoming_bytes;
                // prevent "echoes" due to residues
                Buffer[CurrentBufferPosition] = SerialConst.STRING_ENDER; // ignore old data
                Buffer[CurrentBufferPosition + 1] = SerialConst.STRING_ENDER; // ignore old data
                
                // message enders: legit '\r\n' or '<ESC>x'; buffer full.
                if (CurrentBufferPosition >= 2)
                {
                    if ((Buffer[CurrentBufferPosition - 1] == '\n') && (Buffer[CurrentBufferPosition - 2] == '\r'))
                    {
                        MsgPending = true;
                    }
                    else if (Buffer[CurrentBufferPosition - 2] == Const.ESC)
                    {
                        MsgPending = true;
                    }
                }
            }
        }
        #endregion
    }

    class SerialConst
    {
        public const int MAX_MESSAGE_LEN = 2048;
        public const int SHORT_SLEEP_MS = 50;

        public const byte STRING_ENDER = (byte)('\0');
    }
    
}
