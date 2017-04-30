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
//~~DEBUG~~//
using System.Diagnostics;

namespace SecretProject2
{
    class WifiModule
    {
        #region [--- Variables ---]
        enum State { UnAssociated , Associated, Connected };
        private static State WifiState { get; set; }
        
        private static SerialGS serialGS;

        private static string SSID, PassPhrase, IP;
        private static string HostIP = "0.0.0.0", HostPort = "0", CID = "0";

        private static string ThreadName = MailBox.GetMailUserName(MailUser.WifiThread);

        private static Timer _timer;
        private static TimerCallback _timerCallback = new TimerCallback(OnTimerExpire);
        private static bool TimeoutExpired = false;

        private static int GSCommunicationFailCounter = 0;
        private static int ServerConnectionFailCounter = 0;
        private static int RejectedAttempts = 0;

        private static bool DebugMode;
        private static bool DebugEnvironment = false;
        private static int msg_counter = 0;
        #endregion

        #region [--- Constructor and Main ---]
        public WifiModule(string ssid, string passphrase, string sysIP, string hostIP, string hostPort,bool debug_mode = false,
                          string portName = SerialPorts.COM1, int baudRate = 115200, Parity parity = Parity.None, int dataBits = 8, StopBits stopBits = StopBits.One)
        {

            // Serial Port Configuration //
            serialGS = new SerialGS(portName, baudRate, parity, dataBits, stopBits, DebugMode);
            var SerialThread = new Thread(serialGS.Run);
            SerialThread.Start(); 
            serialGS.OpenSerialPort();

            // Network Parameters
            SSID = ssid;
            PassPhrase = passphrase;
            IP = sysIP;
            HostIP = hostIP;
            HostPort = hostPort;

            WifiState = State.UnAssociated;
            
            // Debug
            DebugMode = debug_mode;
            if (HostIP == "10.0.0.5")
            {
                DebugEnvironment = true;
            }
        }

        public void Run()
        {
            while (true)
            {
                ParseMessage();
                CheckAndHandleMails();
                CheckForErrors();
                Thread.Sleep(Const.POLLING_PERIOD);
            }
        }

        private static void CheckForErrors()
        {
            if (RejectedAttempts >= Const.MAX_REJECTED_ATTEMPTS)
            {
                Debug.Print(">> Number of rejected GET/POST events had reached maximum, resetting WifiState.");
                RejectedAttempts = 0;
                ChangeStateTo(State.Associated);
            }
            
            
            if ((ServerConnectionFailCounter >= Const.MAX_SERVER_CONNECTION_ATTEMPTS) ||
                (GSCommunicationFailCounter >= Const.MAX_GS_COMMUNICATION_ATTEMPTS))
            {
                Debug.Print(">> FailCounter had reached maximum, resetting {" + ServerConnectionFailCounter.ToString() + "/" + GSCommunicationFailCounter.ToString() + "}");
                HandleConnectionFail();
            }
        }
        
        #endregion

        #region [--- Inter-Task Communications ---]
        private static void CheckAndHandleMails()
        {
            int messages = MailBox.MsgCount(MailUser.WifiThread);

            for (int i = 0; i < messages; i++)
            {
                HandleMessage(MailBox.Get(MailUser.WifiThread));
            }
        }

        private static void HandleMessage(string msg)
        {
            TRACE("HandleMessage","[" + ThreadName + " handling: " + MailBox.Limit(msg,8) + "...]");

            string[] Mail = msg.Split(';');
            if (Mail.Length != 2) throw new Exception("Unrecognized messsage format");

            string Subject = Mail[0];
            string Content = Mail[1];

            switch (Subject)
            {
                case "Post":
                    if (WifiState != State.Associated)
                    {
                        TRACE("HandleMessage", " Rejecting POST request: In Session.");
                        RejectedAttempts++;
                        break;
                    }
                    Debug.Print("----------------------------[ POST: START ]----------------------------------");
                    HttpPost(Content);
                    RejectedAttempts = 0;
                    Debug.Print("----------------------------[ POST: END ]------------------------------------");
                    break;

                case "Get":
                    if (WifiState != State.Associated)
                    {
                        TRACE("HandleMessage", " Rejecting GET request: In Session.");
                        RejectedAttempts++;
                        break;
                    }

                    Debug.Print("----------------------------[ GET: START ]-----------------------------------");
                    HttpGet(Content);
                    RejectedAttempts = 0;
                    Debug.Print("----------------------------[ GET: END ]-------------------------------------");
                    break;

                case "Control":
                    if (Content == "Associate")
                    {
                        Debug.Print("----------------------------[ ASSOCIATE: START ]-----------------------------");
                        AssociateToNetwork();
                        Debug.Print("----------------------------[ ASSOCIATE: END ]-------------------------------");
                    }

                    else if (Content == "Reset")
                    {
                        HandleConnectionFail();
                    }
                    break;

                case "Debug":
                    string reply = SendToGS(Content);
                    PostMessage(MailUser.MainThread, "Debug", reply);
                    break;

                default:
                    throw new Exception("Unrecognized mail subject");
            }
        }

        private static void PostMessage(MailUser user, string subject, string msg)
        {
            TRACE("PostMessage","[" + ThreadName + " Posts " + MailBox.GetMailUserName(user) + ": " + MailBox.Limit(msg, 8) + "...]");
            try
            {
                MailBox.Post(user, subject + ';' + msg);
            }
            catch (Exception e)
            {
                TRACE("PostMessage", "HANDELED EXCEPTION: could not post message. " + e.Message);
            }
        }
        #endregion

        #region [--- Exported functions ---]
        // --- Immidiate Exported Functions ------------------------------------------- //
        public bool isConnected()
        {
            return (WifiState >= State.Associated);
        }

        public void SetDebugMode(bool mode)
        {
            DebugMode = mode;
            serialGS.SetDebugMode(mode);
            TRACE("SetDebugMode", "Debug mode set.");
        }
        // --- Exported (through MailBox) functions ----------------------------------- //
        private static bool AssociateToNetwork()
        {
            if (WifiState != State.UnAssociated)
            {
                Debug.Print(" -- Already Associated -- ");
                return true;
            }

            // Start Association process with a 'clean' message buffer,
            // specifically a Boot Message.
            ParseMessage();

            Debug.Print(" -- Starting Association Sequence -- ");
            PostMessage(MailUser.MainThread, "OnboardLed", "TurnOn");

            if (AssociationProcessSucceeded())
            {
                Debug.Print(" -- End Association: Success --");
                PostMessage(MailUser.MainThread, "OnboardLed", "TurnOff");
                ChangeStateTo(State.Associated, true);
                return true;
            }

            Debug.Print(" -- End Association: Failed --");
            PostMessage(MailUser.MainThread, "OnboardLed", "TurnOff");
            return false;
        }
        
        private static bool AssociationProcessSucceeded()
        {
            if (!SendAT(Const.PING))
            {
                Debug.Print(">> No ping from GS, aborting.");
                return false;
            }
            Debug.Print(">> Ping from GS module received.");

            if(!SendAT(Const.NETWORK_SURVEY,SSID,5))
            {
                Debug.Print(">> Could not find network, aborting.");
                return false;
            }
            Debug.Print(">> Network found.");

            if (!SendAT(Const.HTTP_CONFIGURATION_3))
            {
                Debug.Print(">> Could not set HTTP header (connection), aborting.");
                return false;
            }
            Debug.Print(">> HTTP Header (connection) set.");

            if (!SendAT(Const.HTTP_CONFIGURATION_11))
            {
                Debug.Print(">> Could not set HTTP header (host), aborting.");
                return false;
            }
            Debug.Print(">> HTTP Header (host) set.");

            if (!SendAT(Const.HTTP_CONFIGURATION_20))
            {
                Debug.Print(">> Could not set HTTP header (20), aborting.");
                return false;
            }
            Debug.Print(">> HTTP Header (20) set.");

            if (!SendAT(Const.SET_IP + IP + "," + Const.SUBNET_MASK + "," + Const.DEFAULT_GATEWAY))
            {
                Debug.Print(">> Could not set IP, aborting.");
                return false;
            }
            Debug.Print(">> IP address set.");

            if (!SendAT(Const.SET_MODE_TO_INFRASTRUCTURE))   // Infrastructure
            {
                Debug.Print(">> Could not set mode, aborting.");
                return false;
            }
            Debug.Print(">> Mode set to Infrastructure.");

            if (!SendAT(Const.SET_SECURITY_MODE_TO_WPA)) // WPA
            {
                Debug.Print(">> Could not set security mode, aborting.");
                return false;
            }
            Debug.Print(">> Security mode set to WPA.");

            if (!SendAT(Const.SET_PASSPHRASE + SSID + "," + PassPhrase, null, 3, Const.PASSPHRASE_TIMEOUT_MS))
            {
                Debug.Print(">> Could not set passphrase, aborting.");
                return false;
            }
            Debug.Print(">> Passphrase set.");

            /* - Not used in WPA-PSK2
            if (!SendAT(Const.SET_AUTHENTICATION_TO_SHARED)) // Shared
            {
                Debug.Print(">> Could not set authentication mode, aborting.");
                return false;
            }
            */ 
            Debug.Print(">> Authentication mode set to 'Shared'.");

            if (!SendAT(Const.ASSOCIATE_TO + SSID + ",,",null,12)) 
            {
                Debug.Print(">> Could not associate to network, aborting.");
                return false;
            }
            Debug.Print(">> Associated to {" + SSID + "}");

            if (!DebugEnvironment)
            {
                if (!SendAT("AT+NDHCP=1"))
                {
                    Debug.Print(">> Cound not enable DHCP.");
                    return false;
                }
                Debug.Print(">> DHCP enabled.");
            }

            return true;
        }

        private static bool HttpPost(string msg, int retries = Const.RETRIES)
        {
            if (!ClearToCreateHttpConnection())
            {
                TRACE("HttpGet", "Cannot create http connection");
                ServerConnectionFailCounter++;
                TRACE("HttpGet", "ServerConnectionFailCounter = {" + ServerConnectionFailCounter.ToString() + "}");
                return false;
            }
            
            SetGreenLed(true);
            Debug.Print(" -- Starting HTTP/POST Session -- ");

            for (int i = 0; i < retries; i++)
            {
                string response = CreateHttpSession(msg + "/" + msg_counter.ToString());
                TRACE("HttpPost", "{" + i.ToString() + "} Got response: [" + response + "]");

                if (response == "Posted.")
                {
                    Debug.Print(" -- HTTP/POST Session End: Success -- ");
                    SetGreenLed(false);
                    return true;
                }

                if (i < retries-1)
                {
                    Debug.Print(" -- HTTP/POST Session End: Retrying --");
                    Thread.Sleep(Const.POLLING_PERIOD);
                }
            }

            Debug.Print(" -- HTTP/POST Session End: Failed --");
            SetGreenLed(false);
            return false;
        }

        private static bool HttpGet(string msg, int retries = Const.RETRIES)
        {
            if (!ClearToCreateHttpConnection())
            {
                TRACE("HttpGet", "Cannot create http connection");
                ServerConnectionFailCounter++;
                TRACE("HttpGet", "ServerConnectionFailCounter = {" + ServerConnectionFailCounter.ToString() + "}");
                return false;
            }
            
            SetYellowLed(true);
            Debug.Print(" -- Starting HTTP/GET Session -- ");

            for (int i = 0; i < retries; i++)
            {
                string response = CreateHttpSession(msg + "/" + msg_counter.ToString());
                TRACE("HttpGet", "{" + i.ToString() + "} Got response: [" + response + "]");

                if (response != null)
                {
                    Debug.Print(">> Parsing message [" + response + "]");

                    try
                    {
                        bool new_command = ParseHttpGet(response);

                        if (new_command)
                        {
                            Debug.Print(">> Sending Ack to server.");
                            HttpPost("getack");
                        }
                        Debug.Print(" -- HTTP/GET Session End: Success -- ");
                        SetYellowLed(false);
                        serialGS.SetDebugMode(false);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Debug.Print(">> Cannot parse response.");
                        TRACE("HttpGet", "HANDELED Exception");
                        TRACE("HttpGet", "Unable to parse HTTP response {" + e.Message + "}");
                    }
                }
                // meanwhile, there could be some important messages...
                ParseMessage();

                if (i < retries - 1)
                {
                    Debug.Print(" -- HTTP/GET Session End: Retrying --");
                    Thread.Sleep(Const.POLLING_PERIOD);
                }
            }

            Debug.Print(" -- HTTP/GET Session End: Failed --");
            SetYellowLed(false);
            return false;
        }

        private static bool ClearToCreateHttpConnection()
        {
            if (WifiState == State.Associated) return true;

            TRACE("ClearToCreateHttpConnection", "Waiting for previos connection drop...");
            SetTimer(Const.HTTP_TIMEOUT_MS, true);
            while ((WifiState != State.Associated) && (!TimeoutExpired) )
            {
                ParseMessage();
                Thread.Sleep(Const.POLLING_PERIOD);
            }

            if (TimeoutExpired)
            {
                //CloseHttpSession();
                return false;
            }
            else
            {
                CancelTimer();
            }
            return true;

        }
        
        private static string CreateHttpSession(string msg)
        {
            
            // if (WifiState != State.Connected)...?
            
            Debug.Print(">> HTTP Session {#" + msg_counter + "}");
            msg_counter++;

            string response = SendATAndGetResponse("AT+HTTPOPEN=" + HostIP + "," + HostPort + ",0");
            if (response == null)
            {
                Debug.Print(">> Could not connect to server.");
                return null;
            }
            
            try
            {
                CID = GetCidFrom(response);
                Debug.Print(">> Connected to server [" + CID + "]");
            }
            catch (Exception e)
            {
                TRACE("CreateHttpSession", "HANDELED EXCEPTION: " + e.Message);
                Debug.Print(">> Possibe server connection problem.");
                ServerConnectionFailCounter++;
                TRACE("CreateHttpSession", "{ServerConnectionFailCounter = " + ServerConnectionFailCounter.ToString() + "}");
                return null;
            }

            ChangeStateTo(State.Connected, true);

            // See if meanwhile connection rejected by server
            if (ConnectionRejectedByServer()) return null;

            Debug.Print(">> Sending HTTP request [" + msg + "]");
            response = SendATAndGetResponse("AT+HTTPSEND=" + CID + ",1,2,/" + msg, 1, Const.HTTP_TIMEOUT_MS);
            if (response == null)
            {
                Debug.Print(">> Could not get HTTP reply (null).");
                CloseHttpSession();
                ServerConnectionFailCounter++;
                TRACE("CreateHttpSession", "{ServerConnectionFailCounter=" + ServerConnectionFailCounter.ToString() + "}");
                return null;
            }
            
            TRACE("CreateHttpSession","Got Response: [" + response + "]");

            if (response.IndexOf(Const.HTTP_OK_MSG) < 0)
            {
                Debug.Print(">> Could not post message.");
                TRACE("CreateHttpSession", "{ServerConnectionFailCounter=" + ServerConnectionFailCounter.ToString() + "}");
                ServerConnectionFailCounter++;
                return null;
            }
            Debug.Print(">> Got valid reply.");

            // make sure connection was closed by the server
            if (!ConnectionRejectedByServer())
            {
                Debug.Print(">> Server did not close connection, possible server error.");
                CloseHttpSession();
                ServerConnectionFailCounter++;
                TRACE("CreateHttpSession", "{ServerConnectionFailCounter=" + ServerConnectionFailCounter.ToString() + "}");
                //return null;
            }
            ServerConnectionFailCounter = 0;

            return response.Substring(response.IndexOf(Const.HTTP_OK_MSG) + Const.HTTP_OK_MSG.Length);
        }

        private static bool ConnectionRejectedByServer()
        {
            Thread.Sleep(2*Const.POLLING_PERIOD);
            ParseMessage();
            if (WifiState != State.Connected)
            {
                Debug.Print(">> Connection rejected/closed by server");
                return true;
            }

            Debug.Print(">> Connection still valid.");
            return false;
        }

        private static void CloseHttpSession()
        {
            if (ConnectionRejectedByServer()) return;

            if (!DebugMode)
            {
                DebugMode = true;
                serialGS.SetDebugMode(true);
            }

            TRACE("CloseHttpSession", "Closing connection: " + CID);
            if (SendAT("AT+HTTPCLOSE=" + CID))
            {
                Debug.Print(">> Connection Closed");
                ChangeStateTo(State.Associated);
            }
            else
            {
                Debug.Print(">> Unable to close session.");
            }
        }

        private static bool ParseHttpGet(string response)
        {
            if (response == null) throw new ArgumentException();

            string[] msg_parts = response.Split('#');
            if (msg_parts.Length != 4)
            {
                TRACE("ParseHttpGet", "ERROR: cannot parse msg [" + response + "]");
                throw new ArgumentException();
            }

            try
            {
                Int16 isNew = Convert.ToInt16(msg_parts[0]);
                string Command = msg_parts[1];
                string DestTemp = msg_parts[2];
                string FanState = msg_parts[3];

                if (isNew == 1)
                {
                    TRACE("ParseHttpGet", "New Message: " + response);
                    switch (Command)
                    {
                        case "ON":
                            PostMessage(MailUser.MainThread, "ACRemote", "SetFanState=" + FanState);
                            PostMessage(MailUser.MainThread, "ACRemote", "SetAcTemp=" + DestTemp);
                            break;

                        case "OFF":
                            PostMessage(MailUser.MainThread, "ACRemote", "TurnAcOff");
                            break;
                    }

                    return true;
                }
                else
                {
                    TRACE("ParseHttpGet", "Recurrent message.");
                    return false;
                }
            }
            catch (Exception e)
            {
                Debug.Print("Exception caught in [GainSpan.ParseHttpGet()]: " + e.Message);
                throw e;
            }
        }

        private static string GetCidFrom(string response)
        {
            string Cid = null;
            
            if (response.IndexOf("IP:") == 0)
            {
                //reply = IP:50.19.109.98nr0rnOKrn
                string[] response_part = response.Split('\r');
                Cid = response_part[1];
            }
            else if (DebugEnvironment)
            {
                //reply = 0
                Cid = response;
            }
            else
            {
                throw new ArgumentException("Unknown response structure");
            }

            // isNumeric(hex)
            int cid = Convert.ToInt32(Cid, 16);

            if (cid > 0)
            {
                TRACE("GetCidFrom", "ERROR: CID = {" + cid.ToString() + "}. Setting fail counter to MAX");
                ServerConnectionFailCounter = Const.MAX_SERVER_CONNECTION_ATTEMPTS;
            }

            return Cid;
        }
        #endregion

        #region [--- GPIO ---]
        private static void SetGreenLed(bool on)
        {
            string text = "AT+DGPIO=31,";
            
            if (on)
                text += "1";
            else
                text += "0";

            if (!SendAT(text))
            {
                TRACE("SetGreenLed",">> Could not set led.");
                return;
            }
            TRACE("SetGreenLed", ">> Led Set.");
        }

        private static void SetYellowLed(bool on)
        {
            string text = "AT+DGPIO=30,";

            if (on)
                text += "1";
            else
                text += "0";

            if (!SendAT(text))
            {
                TRACE("SetYellowLed", ">> Could not set led.");
                return;
            }
            TRACE("SetYellowLed", ">> Led Set.");
        }
        #endregion

        #region [--- GS Command Interface ---]
        private static bool SendAT(string command, string response = null, Int32 retries = Const.RETRIES, Int32 timeout = Const.REPLY_TIMEOUT_MS)
        {
            Debug.Assert(ValidAt(command), "Not a valid AT command");

            while (retries > 0)
            {
                retries--;

                SetTimer(timeout);

                serialGS.Write(command, DebugMode);

                // wait for reply
                while ((serialGS.WaitingForReply()) && (!TimeoutExpired)) { };

                if (TimeoutExpired)
                {
                    TRACE("SendAT","{" + command + "} Request timed out.");
                    GSCommunicationFailCounter++;
                    TRACE("SendAT","{GSCommunicationFailCounter = " + GSCommunicationFailCounter.ToString() + "}");
                    continue;
                }
                CancelTimer();

                // A Good reply includes: command, response string (if exists) and OK string.
                string[] replies = serialGS.ReadAll();
                int i = 0;

                foreach (string reply in replies)
                {
                    TRACE("SendAT", ">> Checking reply: [" + PrintSpecialChars(reply) + "]");
                    // (1) find the right response
                    if (reply.IndexOf(command) != 0)
                    {
                        i++;
                        continue;
                    }
                    serialGS.Pop(i, DebugMode);

                    // (2) check if it's valid
                    if ((response != null) && (reply.IndexOf(response) < 0))
                    {
                        break;
                    }

                    if (reply.IndexOf(Const.OK_STR) < 0)
                    {
                        break;
                    }
                    TRACE("SendAT", ">> Accepted.");
                    return true;
                }
                // (3) if no reply was found in buffer, unlock it.
                serialGS.Pop(-1, DebugMode); 
                Thread.Sleep(timeout);
            }
            // failed all retries
            return false;
        }

        private static string SendATAndGetResponse(string command, Int32 retries = Const.RETRIES, Int32 timeout = Const.REPLY_TIMEOUT_MS)
        {
            Debug.Assert(ValidAt(command), "Not a valid AT command");

            while (retries > 0)
            {
                retries--;

                SetTimer(timeout);
                TRACE("SendATAndGetResponse", "Sending: [" + command + "]");
                serialGS.Write(command, DebugMode);

                // wait for reply
                while ((serialGS.WaitingForReply()) && (!TimeoutExpired)) { };

                if (TimeoutExpired)
                {
                    TRACE("SendATAndGetResponse", "ERROR: Request timed out. {" + timeout.ToString() + "ms}");
                    GSCommunicationFailCounter++;
                    TRACE("SendAtAndGetResponse", "{GSCommunicationFailCounter = " + GSCommunicationFailCounter.ToString() + "}");
                    return null; // it's either that or throw an exception. ParseMessage should deal with a late response
                }
                else
                {
                    CancelTimer();
                }

                string[] replies = serialGS.ReadAll();
                int i = 0;

                foreach (string reply in replies)
                {
                    TRACE("SendATAndGetResponse", "Checking reply: [" + PrintSpecialChars(reply) + "]");
                    // (1) find the right response
                    if (reply.IndexOf(command.Split('=')[0]) < 0)
                    {
                        i++;
                        continue;
                    }
                    serialGS.Pop(i, DebugMode);

                    // (2) check if response is valid
                    if (reply.IndexOf(Const.OK_STR) < 0)
                    {
                        TRACE("SendATAndGetResponse", "ERROR: OK not found.");
                        break;
                    }

                    // (3) try to exctract response
                    Int32 msg_start = reply.IndexOf(command.Split('=')[0]) + command.Length + 3; // "\r\r\n"
                    Int32 msg_end = reply.Length - Const.OK_STR.Length;

                    if (msg_end < msg_start)
                    {
                        TRACE("SentATAndGetResponse", "Could not extract response");
                        break;
                    }

                    TRACE("SendATAndGetResponse", "reply OK. returning [" + PrintSpecialChars(reply.Substring(msg_start, msg_end - msg_start)) + "]");
                    return reply.Substring(msg_start, msg_end - msg_start);
                }
                // (4) if command reply not found in the entire message buffer, unlock it.
                serialGS.Pop(-1, DebugMode);
                Thread.Sleep(timeout);
            }
            // failed all retries
            return null;            
        }

        private static string SendToGS(string command, Int32 retries = Const.RETRIES, Int32 timeout = Const.REPLY_TIMEOUT_MS)
        {

            while (retries > 0)
            {
                retries--;

                SetTimer(timeout);
                TRACE("SendToGS", "Sending: [" + command + "]");
                serialGS.Write(command, DebugMode);

                // wait for reply
                while ((serialGS.WaitingForReply()) && (!TimeoutExpired)) { };

                if (TimeoutExpired)
                {
                    TRACE("SendToGS", "ERROR: Request timed out. {" + timeout.ToString() + "ms}");
                    GSCommunicationFailCounter++;
                    TRACE("SendToGS", "{GSCommunicationFailCounter = " + GSCommunicationFailCounter.ToString() + "}");
                    return "[ERROR: Reply timed out.]";
                }
                else
                {
                    CancelTimer();
                }

                string[] replies = serialGS.ReadAll(); // Locks Buffer
                int i = 0;

                foreach (string reply in replies)
                {
                    if (reply.IndexOf(command.Split('=')[0]) < 0)
                    {
                        i++;
                        continue;
                    }
                    serialGS.Pop(i, DebugMode); // Unlocks buffer

                    TRACE("SendToGS", "Found reply: [" + reply + "]");
                    return reply;
                }
                // if command reply not found in the entire message buffer, unlock it.
                serialGS.Pop(-1, DebugMode);
                Thread.Sleep(timeout);
            }
            // failed all retries
            return "[ERROR: Could not get reply.]";
        }

        private static bool ValidAt(string cmd)
        {
            if (cmd.IndexOf("AT") == 0)
                return true;
            return false;
        }

        private static void SetTimer(Int32 timeout_ms, bool verbose = false)
        {
            if (verbose)
                TRACE("SetTimer","** Setting Timer {" + timeout_ms + "} **");

            TimeoutExpired = false;
            _timer = new Timer(_timerCallback, null, timeout_ms, Timeout.Infinite);
            // timer starts automatically //
        }

        private static void CancelTimer(bool verbose = false)
        {
            if (verbose)
                TRACE("CancelTimer","** Killing Timer **");
            
            _timer.Dispose();
        }

        private static void OnTimerExpire(object StateObj)
        {
            //Debug.Print("** Timer Expired **");
            TimeoutExpired = true;
            CancelTimer();
        }
        #endregion

        #region [--- State Machine Functions ---]
        private static void ChangeStateTo(State NewState, bool verbose = false)
        {
            if (WifiState == NewState) return; 

            if (verbose)
                TRACE("ChangeStateTo","* Wifi State Change: " + GetStateName(WifiState) + " --> " + GetStateName(NewState) + " *");
            WifiState = NewState;
        }

        private static string GetStateName(State state)
        {
            string[] StateNames = { "UnAssociated", "Associated", "Connected" };
            if ((int)state > (int)State.Connected)
            {
                return "Unknown";
            }
            else
            {
                return StateNames[(int)state];
            }
        }

        private static void HandleConnectionFail()
        {
            TRACE("HandleConnectionFail", "* Server/GS connection Failed, resetting system *");
            RestartClass();
            PostMessage(MailUser.MainThread, "WifiModule", "Reset GainSpan");
        }

        private static void RestartClass()
        {
            TRACE("ResetClass", "Resetting class variables");
            ServerConnectionFailCounter = 0;
            GSCommunicationFailCounter = 0;
            WifiState = State.UnAssociated;
            TimeoutExpired = false;
            //serialGS.RestartClass();
        }
        #endregion

        #region [--- HTTP Server Communication Functions ---]
        private static void SendTcp(string line)
        {
            if (WifiState != State.Connected ) throw new Exception("Cannot send data when there is no link!");

            serialGS.Write(Const.ESC + "S" + CID + line + Const.ESC + "E");
            TRACE("SendTcp","TXd{" + CID + "}>> " + line);
        }

        private static void ParseMessage()
        {
            // The point of this function would be to recognize socket failures or association drops //
            if (!serialGS.MessagePending()) return;

            Debug.Print("----------------------------[ PARSE MSG: START ]-----------------------------");
            string[] messages = serialGS.ReadAndEraseAll();

            foreach (string text in messages)
            {
                TRACE("ParseMessage", "Handling: " + PrintSpecialChars(text));
                
                if (text.IndexOf("DISASSOCIATED") >= 0)
                {
                    ChangeStateTo(State.UnAssociated, true);
                    TRACE("ParseMessage","* System Unassociated *");
                    continue;
                }

                if (text.IndexOf("SOCKET FAILURE") >= 0)
                {
                    ChangeStateTo(State.Associated, true);
                    TRACE("ParseMessage","* Socket Failed *");
                    continue;
                }

                if (text.IndexOf("DISCONNECT") >= 0)
                {
                    ChangeStateTo(State.Associated, true);
                    TRACE("ParseMessage", "* Disconnected from server {" + CID + "} *");
                    continue;
                }

                if (text.IndexOf("ERROR: INVALID CID") >= 0)
                {
                    ChangeStateTo(State.Associated, true);
                    TRACE("ParseMessage", "* Unrecognized disconnection from server {" + CID + "} *");
                    continue;
                }

                // Got here due to timeout, possibly opened a connection //
                if (text.IndexOf("AT+HTTPOPEN") >= 0)
                {
                    int i = text.IndexOf(Const.OK_STR);

                    if (i > 0)
                    {
                        // why not GetCidFrom()?
                        CID = text.Substring(i - 1, 1);
                        CloseHttpSession();
                        TRACE("ParseMessage", "Handling HTTPOPEN response: closing connection {" + CID + "}");
                    }
                    else
                    {
                        TRACE("ParseMessage", "Handling HTTPOPEN response: connection was not created by server");
                    }
                    continue;
                }

                // Gainspan module had been resseted
                if (text.IndexOf("UnExpected Warm Boot") >= 0)
                {
                    Debug.Print(">> GS Unexpected Boot.");
                    RestartClass();
                }

                RejectMessage(text);

            }
            Debug.Print("----------------------------[ PARSE MSG: END ]-------------------------------");
        }

        private static void RejectMessage(string msg)
        {
            TRACE("RejectMessage","Rejecting: " + PrintSpecialChars(msg));
        }
        #endregion

        #region [--- Debugging ---]
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

        private static void TRACE(string func, string msg)
        {
            if (!DebugMode) return;
            TimeSpan ts = Microsoft.SPOT.Hardware.Utility.GetMachineTime();
            Debug.Print("[" + ts.ToString()  + "] [GainSpan."+ func + "()] " + msg);
        }

        #endregion
    }

    class Const
    {
        public const string PING = "AT";
        public const string NETWORK_SURVEY = "AT+WS";
        public const string HTTP_CONFIGURATION_3 = "AT+HTTPCONF=3,close";
        public const string HTTP_CONFIGURATION_11 = "AT+HTTPCONF=11,gitzi.pythonanywhere.com";
        public const string HTTP_CONFIGURATION_20 = "AT+HTTPCONF=20,User-Agent: Mozilla/5.0 (Windows; U; Windows NT 5.1; en-US;rv:1.9.1.9) Gecko/20100315 Firefox/3.5.9";
        public const string SET_IP = "AT+NSET=";
        public const string SUBNET_MASK = "255.255.255.0";
        public const string DEFAULT_GATEWAY = "10.0.0.138";
        public const string SET_MODE_TO_INFRASTRUCTURE = "AT+WM=0";
        public const string SET_SECURITY_MODE_TO_WPA = "AT+WSEC=8";
        public const string SET_PASSPHRASE = "AT+WPAPSK=";
        public const string SET_AUTHENTICATION_TO_SHARED = "AT+WAUTH=2";
        public const string ASSOCIATE_TO = "AT+WA=";
        
        public const char ESC = '\x1b';
        public const string OK_STR = "\r\nOK\r\n";
        public const string HTTP_OK_MSG = "200 OK\r\n";
        
        public const int POLLING_PERIOD = 500;
        public const int REPLY_TIMEOUT_MS = 5000;
        public const int PASSPHRASE_TIMEOUT_MS = 10000;
        public const int HTTP_TIMEOUT_MS = 10000;
        public const int RETRIES = 3;
        public const int MAX_SERVER_CONNECTION_ATTEMPTS = 3;
        public const int MAX_GS_COMMUNICATION_ATTEMPTS = 3;
        public const int MAX_REJECTED_ATTEMPTS = 12;
    }
}
