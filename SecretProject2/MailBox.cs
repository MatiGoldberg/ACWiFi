using System;
using Microsoft.SPOT;
using System.Collections;

namespace SecretProject2
{
    enum MailUser { MainThread, WifiThread };

    class MailBox
    {
        private static Queue MainThreadMailbox  =   new Queue();
        private static Queue WifiThreadMailbox  =   new Queue();

        public static void Post(MailUser recepient, string message)
        {
            switch (recepient)
            {
                case MailUser.MainThread:
                    lock (MainThreadMailbox)
                    {
                        MainThreadMailbox.Enqueue((object)message);
                    }
                    break;

                case MailUser.WifiThread:
                    lock (WifiThreadMailbox)
                    {
                        WifiThreadMailbox.Enqueue((object)message);
                    }
                    break;

                default:
                    break;
            }
        }

        public static string Get(MailUser user)
        {
            switch (user)
            {
                case MailUser.MainThread:
                    if (MainThreadMailbox.Count > 0)
                    {
                        return (string)MainThreadMailbox.Dequeue();
                    }
                    else 
                        return "No Messages";

                case MailUser.WifiThread:
                    if (WifiThreadMailbox.Count > 0)
                    {
                        return (string)WifiThreadMailbox.Dequeue();
                    }
                    else
                        return "No Messages";

                default:
                    throw new Exception("Unrecognized mail user");
            }
        }

        public static int MsgCount(MailUser user)
        {
            switch (user)
            {
                case MailUser.MainThread:
                    return MainThreadMailbox.Count;

                case MailUser.WifiThread:
                    return WifiThreadMailbox.Count;
                
                default:
                    throw new Exception("Unrecognized mail user");
            }
        }
        
        public static string GetMailUserName(MailUser user)
        {
            string[] Names = {"MainThread", "WifiThread"};
            return Names[(int)user];
        }

        public static string Limit(string msg, int max_chars)
        {
            if (msg.Length > max_chars)
                return msg.Substring(0, max_chars);
            return msg;
        }
    }
}
