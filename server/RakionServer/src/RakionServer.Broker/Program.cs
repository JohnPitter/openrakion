using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using BrokenServer.Definitions;
using BrokenServer.Global;
using BrokenServer.Network;

namespace BrokenServer
{
	// Token: 0x02000024 RID: 36
	internal class Program
	{
		// Token: 0x060000F1 RID: 241 RVA: 0x00004A0C File Offset: 0x00002C0C
		private static void Main(string[] args)
		{
			Program program = new Program();
			Bootlogo._Load();
			int num = 40706;
			int num2 = 40706;
			string text = "192.168.1.5";
			string text2 = "192.168.1.5";
			try
			{
				if (File.Exists(Environment.CurrentDirectory + "\\Settings\\Settings.ini"))
				{
					Systems.Ini ini = new Systems.Ini(Environment.CurrentDirectory + "\\Settings\\Settings.ini");
					num = Convert.ToInt32(ini.GetValue("Server", "port", 40706));
					text = ini.GetValue("Server", "ip", "192.168.1.5").ToString();
					num2 = Convert.ToInt32(ini.GetValue("IPC", "port", 40706));
					text2 = ini.GetValue("IPC", "ip", "192.168.1.5").ToString();
					Program.debug = ini.GetValue("CONSOLE", "debug", "0").ToString();
					LogConsole.Show("Has loaded your ip settings successfully");
				}
				else
				{
					LogConsole.Show("Settings.ini could not be found, using default setting");
				}
			}
			catch (Exception)
			{
				return;
			}
			if (args.Length > 0 && args[0] == "extip")
			{
				HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create("http://checkip.dyndns.org/");
				httpWebRequest.Method = "GET";
				WebResponse response = httpWebRequest.GetResponse();
				StreamReader streamReader = new StreamReader(response.GetResponseStream(), Encoding.UTF8);
				streamReader.ReadToEnd();
			}
			Global.Network.multihomed = false;
			if (Global.Network.LocalIP == "")
			{
				IPAddress[] hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
				foreach (IPAddress ipaddress in hostAddresses)
				{
					if (ipaddress.AddressFamily.Equals(AddressFamily.InterNetwork) && !ipaddress.Equals(IPAddress.Loopback))
					{
						if (Global.Network.LocalIP != "")
						{
							Global.Network.multihomed = true;
						}
						else
						{
							Global.Network.LocalIP = ipaddress.ToString();
						}
					}
				}
			}
			Systems.Server server = new Systems.Server();
			server.OnConnect += program._OnClientConnect;
			server.OnError += program._ServerError;
			Systems.Client.OnReceiveData += program._OnReceiveData;
			Systems.Client.OnDisconnect += program._OnClientDisconnect;
			try
			{
				server.Start(text, num);
			}
			catch (Exception ex)
			{
				LogConsole.Show("Starting Server error: {0}", ex);
			}
			Systems.LoadServers("GameServers.ini", 40708);
			Program.IPCServer = new Servers.IPCServer();
			Program.IPCServer.OnReceive += program.OnIPC;
			try
			{
				Program.IPCServer.Start(text2, num2);
				foreach (KeyValuePair<int, Systems.SRX_Serverinfo> keyValuePair in Systems.GSList)
				{
					byte[] array2 = Program.IPCServer.PacketRequestServerInfo(num2);
					Servers.IPCenCode(ref array2, keyValuePair.Value.code);
					Program.IPCServer.Send(keyValuePair.Value.ip, (int)keyValuePair.Value.ipcport, array2);
					array2 = null;
				}
			}
			catch (Exception ex2)
			{
				LogConsole.Show("Error start ICP: {0}", ex2);
			}
			LogConsole.Show("Ready for gameserver connection...");
			for (;;)
			{
				Thread.Sleep(100);
				foreach (KeyValuePair<int, Systems.SRX_Serverinfo> keyValuePair2 in Systems.GSList)
				{
					if (keyValuePair2.Value.status != 0 && keyValuePair2.Value.lastPing.AddMinutes(5.0) < DateTime.Now)
					{
						keyValuePair2.Value.status = 0;
						LogConsole.Show("Server: {0}:({1}) has timed out, status changed to check", keyValuePair2.Value.id, keyValuePair2.Value.name);
					}
				}
			}
		}

		// Token: 0x060000F2 RID: 242 RVA: 0x00004E24 File Offset: 0x00003024
		public void OnIPC(Socket aSocket, EndPoint ep, byte[] data)
		{
			try
			{
				if (data.Length >= 6)
				{
					UTF8Encoding utf8Encoding = new UTF8Encoding();
					utf8Encoding.GetString(data);
					string[] array = ep.ToString().Split(new char[] { ':' });
					Systems.SRX_Serverinfo serverByEndPoint = Systems.GetServerByEndPoint(array[0], (int)ushort.Parse(array[1]));
					if (serverByEndPoint != null)
					{
						Systems.PacketReader packetReader = new Systems.PacketReader(data);
						short num = packetReader.Int16();
						if (data.Length >= 6)
						{
							short num2 = num;
							if (num2 != 257)
							{
								if (num2 != 1025)
								{
									LogDebug.Show("[IPC] unknown command recevied {0:x}", num);
								}
								else
								{
									packetReader.Skip(4);
									int num3 = (int)packetReader.Byte();
									int num4 = packetReader.Int32();
									LogDebug.Show("[IPC] Recv Serv-Con SERVER: {0} USER: {1}", num3, num4);
								}
							}
							else
							{
								packetReader.Skip(4);
								int num5 = (int)packetReader.Byte();
								short num6 = packetReader.Int16();
								short num7 = packetReader.Int16();
								short num8 = packetReader.Int16();
								short num9 = packetReader.Int16();
								serverByEndPoint.maxSlots = (ushort)num8;
								serverByEndPoint.usedSlots = (ushort)num9;
								serverByEndPoint.maxSalas = (ushort)num6;
								serverByEndPoint.usedSala = (ushort)num7;
								LogDebug.Show("[IPC] Recv Serv-Info from GameServer {0} MAXUSER={1}, CUR={2}, MAXSALAS={3}, CUR={4}", num5, num8, num9, num6, num7);
								if (serverByEndPoint.status == 0)
								{
									LogConsole.Show("Server: {0} change to online", serverByEndPoint.name);
								}
								serverByEndPoint.status = 1;
								serverByEndPoint.lastPing = DateTime.Now;
							}
						}
						else
						{
							LogDebug.Show("[IPC] data to short");
						}
					}
					else
					{
						LogDebug.Show("[IPC] can't find the GameServer {0}:{1}", ((IPEndPoint)ep).Address.ToString(), array[1]);
					}
				}
				else
				{
					LogDebug.Show("[IPC] packet to short from {0}", ep.ToString());
				}
			}
			catch (Exception ex)
			{
				LogDebug.Show("[IPC.OnIPC] {0}", ex);
			}
		}

		// Token: 0x060000F3 RID: 243 RVA: 0x00002742 File Offset: 0x00000942
		public void _OnReceiveData(Systems.Decode de)
		{
			Systems.oPCode(de);
		}

		// Token: 0x060000F4 RID: 244 RVA: 0x0000274A File Offset: 0x0000094A
		public void _OnClientConnect(ref object de, Systems.Client net)
		{
			de = new Systems(net);
		}

		// Token: 0x060000F5 RID: 245 RVA: 0x00005008 File Offset: 0x00003208
		public void _OnClientDisconnect(object o)
		{
			try
			{
				Systems systems = (Systems)o;
				systems.client.clientSocket.Close();
			}
			catch
			{
			}
		}

		// Token: 0x060000F6 RID: 246 RVA: 0x000021B5 File Offset: 0x000003B5
		private void _ServerError(Exception ex)
		{
		}

		// Token: 0x060000F7 RID: 247 RVA: 0x00005044 File Offset: 0x00003244
		public static string HexStr(byte[] data)
		{
			char[] array = new char[]
			{
				'0', '1', '2', '3', '4', '5', '6', '7', '8', '9',
				'A', 'B', 'C', 'D', 'E', 'F'
			};
			int i = 0;
			int num = 2;
			int num2 = data.Length;
			char[] array2 = new char[num2 * 2 + 2];
			array2[0] = '0';
			array2[1] = 'x';
			while (i < num2)
			{
				byte b = data[i++];
				array2[num++] = array[(int)(b / 16)];
				array2[num++] = array[(int)(b % 16)];
			}
			return new string(array2, 0, array2.Length);
		}

		// Token: 0x060000F8 RID: 248 RVA: 0x000050C0 File Offset: 0x000032C0
		public static byte[] GetBytesUInt16(ushort argument)
		{
			return BitConverter.GetBytes(argument);
		}

		// Token: 0x04000056 RID: 86
		private const string formatter = "{0,10}{1,13}";

		// Token: 0x04000057 RID: 87
		public static Servers.IPCServer IPCServer;

		// Token: 0x04000058 RID: 88
		public static Dictionary<ushort, IPCItem> IPCResultList = new Dictionary<ushort, IPCItem>();

		// Token: 0x04000059 RID: 89
		public static ushort IPCNewId = 0;

		// Token: 0x0400005A RID: 90
		public static int IPCPort = 40706;

		// Token: 0x0400005B RID: 91
		public static string debug = "0";
	}
}
