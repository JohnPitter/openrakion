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
using RakionServer.Common;

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
				string settingsPath = Path.Combine(Environment.CurrentDirectory, "Settings", "Settings.ini");
				if (File.Exists(settingsPath))
				{
					IniFile ini = new IniFile(settingsPath);
					num = ini.GetValue("Server", "port", 40706);
					text = ini.GetValue("Server", "ip", "192.168.1.5");
					num2 = ini.GetValue("IPC", "port", 40706);
					text2 = ini.GetValue("IPC", "ip", "192.168.1.5");
					Program.debug = ini.GetValue("CONSOLE", "debug", "0");
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
					if (BrokerLeasePolicy.IsExpired(keyValuePair2.Value, DateTime.UtcNow))
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
			if (ep is not IPEndPoint remote)
			{
				LogDebug.Show("[IPC] endpoint inválido");
				return;
			}
			Systems.SRX_Serverinfo server = Systems.GetServerByEndPoint(
				remote.Address.ToString(), remote.Port);
			if (server == null)
			{
				LogDebug.Show("[IPC] origem não configurada {0}", remote);
				return;
			}
			BrokerIpcParseResult parsed = BrokerIpcParser.ReadServerInfo(data, server.code);
			if (!parsed.Success)
			{
				LogDebug.Show("[IPC] pacote recusado de {0}: {1}", remote, parsed.Error);
				return;
			}
			BrokerServerInfo info = parsed.Info;
			if (info.ServerId != server.id)
			{
				LogDebug.Show("[IPC] server id {0} não corresponde a {1}",
					info.ServerId, server.id);
				return;
			}

			server.maxSlots = info.MaxUsers;
			server.usedSlots = info.UsedUsers;
			server.maxSalas = info.MaxRooms;
			server.usedSala = info.UsedRooms;
			if (server.status == 0)
				LogConsole.Show("Server: {0} change to online", server.name);
			server.status = 1;
			server.lastPing = DateTime.UtcNow;
			LogDebug.Show("[IPC] ServerInfo id={0} users={1}/{2} salas={3}/{4}",
				info.ServerId, info.UsedUsers, info.MaxUsers, info.UsedRooms, info.MaxRooms);
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
		// Token: 0x0400005B RID: 91
		public static string debug = "0";
	}
}
