using System;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;

namespace SecretProject2
{
    public class Program
    {
        #region [--- System Variables -----------------------------------]

        private static bool DEBUG_MODE = false;
        private static string VERSION = "1.5";
        
        private static string ThreadName = MailBox.GetMailUserName(MailUser.MainThread);

        private static OutputPort GainSpanPower = new OutputPort(Pins.GPIO_PIN_D8, true);
        private static Led StatusLed = new Led();
        //private static OutputPort OnboardLed = new OutputPort(Pins.ONBOARD_LED, false);

        private static WifiModule Wifi;

        private static DebugPort DebugUart = new DebugPort();

        private static TMP36 TempSensor = new TMP36(TMP36.Channel.GPIO_PIN_A3);

        private static ACRemote RemoteControl = new ACRemote();

        private static Int32 ServerUpdateTimer = Defines.UPDATE_PERIOD;
        private static Int32 ServerGetTimer = Defines.GET_PERIOD;

        private static Timer _timer;
        private static TimerCallback _timerCallback = new TimerCallback(OnTimerExpire);

        private static bool TimeoutExpired = true;
        private static bool OnlineUpdate = true;
        
        private static int AssociationFailCounter = 0;

        //~~~DEBUG~~~//
        private static InterruptPort PushButton = new InterruptPort(Pins.ONBOARD_SW1, true, Port.ResistorMode.Disabled, Port.InterruptMode.InterruptEdgeLow);
        private static bool HandleButton = false;
        private static bool LogTemperatures = false;
        //~~~DEBUG~~~//

        #endregion

        #region [--- Main Functions -------------------------------------]
        public static void Main()
        {
            // Led Thread //
            var LedThread = new Thread(StatusLed.Run);
            LedThread.Start();
            StatusLed.SetState(Led.LedState.BlinkFast);
            
            // Wifi Power Setup //
            GainSpanReset();

            // Wifi Thread Setup //
            Wifi = new WifiModule(Defines.SSID, Defines.PASSPHRASE, Defines.SYSIP, Defines.HOST_IP, Defines.HOST_PORT, DEBUG_MODE);
            var WifiThread = new Thread(Wifi.Run);
            WifiThread.Start(); 

            // Debug port Setup
            var DebugThread = new Thread(DebugUart.Run);
            DebugUart.SetDebugMode(DEBUG_MODE);
            DebugThread.Start();
            DebugUart.Write("System Wakeup V" + VERSION);

            // Temperature Sensor(s) Setup
            var PrimaryTempSensorThread = new Thread(TempSensor.Run);
            PrimaryTempSensorThread.Start();

            Wifi.SetDebugMode(DEBUG_MODE);
            RemoteControl.SetDebugMode(DEBUG_MODE);

            //~~~DEBUG~~~//
            PushButton.OnInterrupt += new NativeEventHandler(PushButton_OnInterrupt);
            //~~~DEBUG~~~//

            while (true)
            {
                DebugTerminal();

                if (LogTemperatures)
                {
                    SaveTempToLog();
                    continue;
                }
                
                CheckAndHandleMails();

                if (HandleButton)
                {
                    HandlePushButton();
                    HandleButton = false;
                }

                Thread.Sleep(Defines.ONE_SECOND);

                if (!OnlineUpdate) continue;

                if (Wifi.isConnected())
                {
                    UpdateServer();
                    AssociationFailCounter = 0;
                    continue;
                }
                
                // not connected
                if (TimeoutExpired)
                {
                    AssociationFailCounter++;
                    if (AssociationFailCounter > Defines.ASSOCIATION_MAX_ATTEMPTS)
                    {
                        Debug.Print(">> System reset.");
                        TRACE("Main", "AssociationFailCounter has reached its limit. resetting system.");
                        ResetSystem();
                        AssociationFailCounter = 0;
                    }

                    SetTimer(Defines.ASSOCIATION_TIMEOUT);
                    PostMessage(MailUser.WifiThread, "Control", "Associate");
                }
                
            }
        }

        private static void ResetSystem()
        {
            PostMessage(MailUser.WifiThread, "Control", "Reset");
            // reset other classes?
        }

        private static void SetTimer(Int32 timeout_ms, bool verbose = false)
        {
            if (verbose)
                TRACE("SetTimer", "** Setting Timer {" + timeout_ms + "} **");

            TimeoutExpired = false;
            _timer = new Timer(_timerCallback, null, timeout_ms, Timeout.Infinite);
            // timer starts automatically //
        }

        private static void OnTimerExpire(object StateObj)
        {
            //Debug.Print("** Timer Expired **");
            TimeoutExpired = true;
            CancelTimer();
        }

        private static void CancelTimer(bool verbose = false)
        {
            if (verbose)
                TRACE("CancelTimer", "** Killing Timer **");

            _timer.Dispose();
        }

        static void PushButton_OnInterrupt(uint data1, uint data2, DateTime time)
        {
            TRACE("PushButton_OnInterrupt", "# Button Pushed #");
            HandleButton = true;
        }

        static void HandlePushButton()
        {
            OnlineUpdate = !OnlineUpdate;

            if (OnlineUpdate)
                TRACE("HandlePushButton", "Online Update is now ON");
            else
                TRACE("HandlePushButton", "Online Update is now OFF");

        }

        private static void UpdateServer()
        {
            if (ServerUpdateTimer == 0)
            {
                // send temperature data
                PostMessage(MailUser.WifiThread, "Post" , "extpost/" + TempSensor.StringValue());
                ServerUpdateTimer = Defines.UPDATE_PERIOD;
            }
            else
            {
                ServerUpdateTimer--;
            }

            if (ServerGetTimer == 0)
            {
                // get AC command
                PostMessage(MailUser.WifiThread, "Get", "extget");
                ServerGetTimer = Defines.GET_PERIOD;
            }
            else
            {
                ServerGetTimer--;
            }
        }
        
        private static void GainSpanReset()
        {
            // Set Association timer
            SetTimer(Defines.QUICK_ASSOCIATION_TIME);
            
            // HW reset GS module            
            Debug.Print("Reseting GainSpan...");
            GainSpanPower.Write(false);
            Thread.Sleep(Defines.GAINSPAN_RESET_MS);
            GainSpanPower.Write(true);
            Debug.Print("Reseting GainSpan... done.");
        }
        #endregion

        #region [--- Internal Messenging --------------------------------]
        private static void CheckAndHandleMails()
        {
            int messages = MailBox.MsgCount(MailUser.MainThread);

            for (int i = 0; i < messages; i++)
            {
                HandleMessage(MailBox.Get(MailUser.MainThread));
            }
        }

        private static void HandleMessage(string msg)
        {
            TRACE("HandleMessage","[" + ThreadName + " handling: " + MailBox.Limit(msg, 16) + "...]");

            string[] Mail = msg.Split(';');
            if (Mail.Length != 2)
            {
                TRACE("HandleMessage","* Message Discarded: Unrecognized messsage format *");
                return;
            }

            string Subject = Mail[0];
            string Content = Mail[1];

            switch (Subject)
            {
                case "TempSensor":
                    // --- TEMP SENSOR -------------------------- //
                    if (Content.Equals("GetTemp"))
                    {
                        PostMessage(MailUser.WifiThread, "Post", TempSensor.StringValue() + " degC");
                    }
                    break;

                case "OnboardLed":
                    // --- ONBOARD LED -------------------------- //
                    if (Content.Equals("TurnOn"))
                    {
                        StatusLed.SetState(Led.LedState.On);
                        //OnboardLed.Write(true);
                        TRACE("HandleMessage", "OnboardLed turned on.");
                    }
                    else if (Content.Equals("TurnOff"))
                    {
                        StatusLed.SetState(Led.LedState.Off);
                        //OnboardLed.Write(false);
                        TRACE("HandleMessage", "OnboardLed turned off.");
                    }
                    break;

                case "ACRemote":
                    // --- AC REMOTE ---------------------------- //
                    if (Content.IndexOf("SetAcTemp") >= 0)
                    {
                        try
                        {
                            int val = ExtractParameter(Content);
                            RemoteControl.SetTemperatureTo(val);
                            TRACE("HandleMessage", "AC Temp set to {" + val.ToString() + "degC}");
                        }
                        catch
                        {
                            TRACE("HandleMessage", "Could not extract parameter in message: " + Content);
                        }

                    }
                    else if (Content.Equals("TurnAcOff"))
                    {
                        RemoteControl.PushOff();
                        TRACE("HandleMessage","AC turned off.");
                    }
                    else if (Content.IndexOf("SetFanState=") == 0)
                    {
                        int val = ExtractParameter(Content);
                        RemoteControl.SetFanTo((ACRemote.ACFanState)val);
                    }
                    break;

                case "WifiModule":
                    // --- WIFI MODULE -------------------------- //
                    if (Content.Equals("Reset GainSpan"))
                    {
                        StatusLed.SetState(Led.LedState.BlinkFast);
                        GainSpanReset();
                    }

                    break;

                case "Debug": // message addressed to PC
                    DebugUart.Write(Content);
                    break;

                default:
                    throw new Exception("Unrecognized mail subject");
            }
        }

        private static Int32 ExtractParameter(string text)
        {
            string[] lst = text.Split('=');
            try
            {
                Int32 val = Convert.ToInt32(lst[1]);
                return val;
            }
            catch (Exception e)
            {
                Debug.Print("Exception: " + e.Message);
                Debug.Print(" >> " + text);
                throw new ArgumentException();
            }
        }

        private static double ExtractDouble(string text)
        {
            string[] lst = text.Split('=');
            try
            {
                double val = Convert.ToDouble(lst[1]);
                return val;
            }
            catch (Exception e)
            {
                Debug.Print("Exception: " + e.Message);
                Debug.Print(" >> " + text);
                throw new ArgumentException();
            }
        }
        
        private static void PostMessage(MailUser user, String subject, string msg)
        {
            TRACE("PostMessage","[" + ThreadName + " Posts " + MailBox.GetMailUserName(user) + ": " + MailBox.Limit(msg, 8) + "...]");
            try
            {
                MailBox.Post(user, subject + ';' + msg);
            }
            catch (Exception e)
            {
                TRACE("PostMessage", "EXCEPTION HANDELED: could not post message. " + e.Message);
            }
        }
        #endregion

        #region [--- Debug Functions ------------------------------------]
        private static void TRACE(string func, string msg)
        {
            if (!DEBUG_MODE) return;
            TimeSpan ts = Microsoft.SPOT.Hardware.Utility.GetMachineTime();
            Debug.Print("[" + ts.ToString() + "] [Program." + func + "()] " + msg);
        }
        
        private static void DebugTerminal()
        {
            if (DebugUart.QueueLength == 0) return;
            string reply = "OK.";

            while (DebugUart.QueueLength > 0)
            {
                string msg = DebugUart.Pop();
                TRACE("DebugTerminal", "Got DEBUG message: " + msg);
                
                // Handle GainSpan Messages
                if (msg.IndexOf("GS#") == 0)
                {
                    PostMessage(MailUser.WifiThread, "Debug", msg.Substring(3));
                }
                // Handle ACRemode Messages
                else if (msg.IndexOf("AC#") == 0)
                {
                    reply = HandleAcCommand(msg.Substring(3));
                }
                // Handle MainTask Messages
                else if (msg.IndexOf("MT#") == 0)
                {
                    reply = HandleMainTaskCommand(msg.Substring(3));
                }
                // Handle Dev. Messages
                else if (msg.IndexOf("DV#") == 0)
                {
                    reply = HandleDevelopmentCommand(msg.Substring(3));
                }
                // No addressing
                else
                {
                    reply = "Unknown message.";
                }
                DebugUart.Write(reply);
            }

        }

        private static string SetBoolParameter(ref bool parameter, string msg)
        {
            string reply = "OK.";
            try
            {
                int par = ExtractParameter(msg);
                if (par == 1)
                {
                    parameter = true;
                }
                else if (par == 0)
                {
                    parameter = false;
                }
                else
                {
                    reply = "ERROR: Bad argument.";
                }
            }
            catch (Exception e)
            {
                reply = "ERROR: " + e.Message;
            }
            return reply;
        }

        private static string HandleAcCommand(string cmd)
        {
            string reply = "OK.";

            if (cmd.IndexOf("TurnOff") == 0)
            {
                RemoteControl.PushOff();
            }
            else if (cmd.IndexOf("SetTemp") == 0)
            {
                try
                {
                    int par = ExtractParameter(cmd);
                    if ((par > 29) || (par < 10)) throw new ArgumentException("Argument out of range");
                    RemoteControl.SetTemperatureTo(par);
                }
                catch (Exception e)
                {
                    reply = "ERROR: " + e.Message;
                }
            }
            else if (cmd.IndexOf("SetFan") == 0)
            {
                try
                {
                    int par = ExtractParameter(cmd);
                    if ((par < 0) || (par > 3)) throw new ArgumentException("Argument out of range");
                    RemoteControl.SetFanTo((ACRemote.ACFanState)par);
                }
                catch (Exception e)
                {
                    reply = "ERROR: " + e.Message;
                }
            }
            // TESTING //
            else if (cmd.IndexOf("TempInc") == 0)
            {
                RemoteControl.TempIncrement();
            }
            else if (cmd.IndexOf("TempDec") == 0)
            {
                RemoteControl.TempDecrement();
            }
            else
            {
                reply = "Unknown command.";
            }

            return reply;
        }

        private static string HandleMainTaskCommand(string cmd)
        {

            string reply = "OK";
            
            #region Wifi and Server Communications
            if (cmd.IndexOf("CommCheck") == 0)
            {
                reply = "Comm Ok.";
            }
            else if (cmd.IndexOf("GsAssociate") == 0)
            {
                PostMessage(MailUser.WifiThread, "Control", "Associate");
            }
            else if (cmd.IndexOf("ServerGet") == 0)
            {
                PostMessage(MailUser.WifiThread, "Get", "extget");
            }
            else if (cmd.IndexOf("ServerPost") == 0)
            {
                PostMessage(MailUser.WifiThread, "Post", "extpost/" + TempSensor.StringValue());
            }
            else if (cmd.IndexOf("WifiStatus") == 0)
            {
                if (Wifi.isConnected())
                {
                    reply = "Associated.";
                }
                else
                {
                    reply = "Unassociated.";
                }
            }
            else if (cmd.IndexOf("GsReset") == 0)
            {
                ResetSystem();
            }
            else if (cmd.IndexOf("OnlineUpdate") == 0)
            {
                reply = SetBoolParameter(ref OnlineUpdate, cmd);
            }
            else if (cmd.IndexOf("SetDebugMode") == 0)
            {
                try
                {
                    int par = ExtractParameter(cmd);
                    if ((par < 0) || (par > 1)) throw new ArgumentException("Bad argument");

                    RemoteControl.SetDebugMode(par == 1);
                    Wifi.SetDebugMode(par == 1);
                }
                catch (Exception e)
                {
                    reply = "ERROR: " + e.Message;
                }
            }

            #endregion

            #region Runtime Commands
            else if (cmd.IndexOf("GetVersion") == 0)
            {
                reply = "Version: " + VERSION;
            }
            else if (cmd.IndexOf("GetFreeMem") == 0)
            {
                DebugUart.Write("Free Mem: " + Debug.GC(true) + " bytes");
            }
            else if (cmd.IndexOf("DebugMode") == 0)
            {
                reply = SetBoolParameter(ref DEBUG_MODE, cmd);
            }
            else if (cmd.IndexOf("Echo") == 0)
            {
                reply = cmd;
            }
            else
            {
                reply = "Unknown command.";
            }
            #endregion

            return reply;
        }

        private static string HandleDevelopmentCommand(string cmd)
        {
            string reply = "OK.";

            if (cmd.IndexOf("GetOnTime") == 0)
            {
                reply = "Class not operated.";
                //reply = ACControl.GetOnTime();
            }
            else if (cmd.IndexOf("LogTemperatures") == 0)
            {
                reply = SetBoolParameter(ref LogTemperatures, cmd);
            }
            else if (cmd.IndexOf("SetAlpha=") == 0)
            {
                try
                {
                    double alpha = ExtractDouble(cmd);
                    if (!TempSensor.SetAlpha(alpha))
                    {
                        reply = "ERROR: could not set Alpha to " + alpha.ToString();
                    }
                }
                catch (Exception e)
                {
                    reply = "Exception: " + e.Message;
                }
            }
            else if (cmd.IndexOf("MailBoxStatus") == 0)
            {
                reply = "MainThread {" + MailBox.MsgCount(MailUser.MainThread) + "}. WifiThread {" + MailBox.MsgCount(MailUser.WifiThread) + "}";
            }
            else
            {
                reply = "Unknown message.";
            }

            return reply;
        }

        private static void SaveTempToLog()
        {
            string sensor_1 = TempSensor.StringValue(3);

            TimeSpan ts = Microsoft.SPOT.Hardware.Utility.GetMachineTime();
            double elapsed_time = (double)(ts.Ticks / (double)TimeSpan.TicksPerSecond);
            string time_stamp = elapsed_time.ToString();
            int i = time_stamp.IndexOf('.');
            time_stamp = time_stamp.Substring(0, i + 4);
            
            string txt = time_stamp + ", " + sensor_1;
            DebugUart.Write(txt);
        }
        #endregion
    }

    class Defines
    {
        public const string SSID = "MySSID";
        public const string PASSPHRASE = "KeyPhrase";
        public const string SYSIP = "10.0.0.10";
        
        // Development Env.
        /*
        public const string HOST_IP = "10.0.0.5";
        public const string HOST_PORT = "5001";
        public const Int32 UPDATE_PERIOD = 33; // Seconds
        public const Int32 GET_PERIOD = 12; // Seconds
        */
        // Production Env.
        
        public const string HOST_IP = "gitzi.pythonanywhere.com";
        public const string HOST_PORT = "80";
        public const Int32 UPDATE_PERIOD = 301; // Seconds
        public const Int32 GET_PERIOD = 22; // Seconds
        
        public const Int32 ONE_SECOND = 1000;  //ms
        public const Int32 GAINSPAN_RESET_MS = 1000; //ms

        public const Int32 QUICK_ASSOCIATION_TIME = GAINSPAN_RESET_MS + 5 * ONE_SECOND;
        public const Int32 ASSOCIATION_TIMEOUT = 60 * ONE_SECOND;
        public const Int32 ASSOCIATION_MAX_ATTEMPTS = 7;

        public const uint MIN_FREE_MEM = 65536;
    }
}
