using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BrokenServer.Global;
using RakionServer.Common;

namespace BrokenServer
{
	// Token: 0x02000007 RID: 7
	public partial class Systems
	{
		// Token: 0x06000021 RID: 33 RVA: 0x00002DF8 File Offset: 0x00000FF8
		public static Systems.SRX_Serverinfo GetServerByEndPoint(string ip, int port)
		{
			Systems.SRX_Serverinfo srx_Serverinfo = null;
			foreach (KeyValuePair<int, Systems.SRX_Serverinfo> keyValuePair in Systems.GSList)
			{
				if (keyValuePair.Value.ip == ip && (int)keyValuePair.Value.ipcport == port)
				{
					srx_Serverinfo = keyValuePair.Value;
				}
			}
			return srx_Serverinfo;
		}

		// Token: 0x06000023 RID: 35 RVA: 0x00002E74 File Offset: 0x00001074
		public static int LoadServers(string serverFile, ushort defaultPort)
		{
			int num;
			try
			{
				if (File.Exists(Path.Combine(Environment.CurrentDirectory, "Settings", serverFile)))
				{
					IniFile ini = new IniFile(Path.Combine(Environment.CurrentDirectory, "Settings", serverFile));
					string[] entryNames = ini.GetEntryNames("SERVERS");
					if (entryNames != null && entryNames.Length > 0)
					{
						foreach (string text in entryNames)
						{
							string value = ini.GetValue("SERVERS", text, "");
							Systems.SRX_Serverinfo srx_Serverinfo = new Systems.SRX_Serverinfo();
							srx_Serverinfo.id = Convert.ToUInt16(ini.GetValue(value, "id", 0));
							srx_Serverinfo.ip = ini.GetValue(value, "ip", "192.168.1.5");
							srx_Serverinfo.wan = ini.GetValue(value, "wan", "192.168.1.5");
							srx_Serverinfo.name = ini.GetValue(value, "name", value);
							srx_Serverinfo.port = Convert.ToUInt16(ini.GetValue(value, "port", (int)defaultPort));
							srx_Serverinfo.ipcport = Convert.ToUInt16(ini.GetValue(value, "ipcport", "40708"));
							srx_Serverinfo.code = ini.GetValue(value, "code", "");
							srx_Serverinfo.lan_wan = ini.GetValue(value, "lan_wan", "0") == "1";
							srx_Serverinfo.Version = Convert.ToInt32(ini.GetValue(value, "version", 0));
							if (!(srx_Serverinfo.ip == "") && srx_Serverinfo.port != 0 && srx_Serverinfo.id != 0 && srx_Serverinfo.ipcport != 0 && !Systems.GSList.ContainsKey((int)srx_Serverinfo.id))
							{
								Systems.GSList.Add((int)srx_Serverinfo.id, srx_Serverinfo);
							}
							else
							{
								LogDebug.Show(string.Concat(new string[] { "IPC: Error on Server ", value, " in ", serverFile, ": field missing or id already in use!" }));
							}
						}
					}
					if (Systems.GSList.Count<KeyValuePair<int, Systems.SRX_Serverinfo>>() > 0)
					{
						string text2 = "Server";
						if (Systems.GSList.Count > 1)
						{
							text2 = "Servers";
						}
						LogConsole.Show(string.Concat(new object[]
						{
							"Loaded ",
							Systems.GSList.Count<KeyValuePair<int, Systems.SRX_Serverinfo>>(),
							" ",
							text2,
							" from server settings"
						}));
					}
					else
					{
						Systems.SRX_Serverinfo srx_Serverinfo2 = new Systems.SRX_Serverinfo();
						srx_Serverinfo2.id = 1;
						srx_Serverinfo2.ip = "192.168.1.5";
						if (!Global.Network.multihomed)
						{
							srx_Serverinfo2.extip = Global.Network.LocalIP;
						}
						srx_Serverinfo2.name = "[SERVER] Default";
						srx_Serverinfo2.port = defaultPort;
						srx_Serverinfo2.ipcport = 40708;
						srx_Serverinfo2.code = "t";
						Systems.GSList.Add((int)srx_Serverinfo2.id, srx_Serverinfo2);
					}
					num = Systems.GSList.Count<KeyValuePair<int, Systems.SRX_Serverinfo>>();
				}
				else
				{
					Systems.SRX_Serverinfo srx_Serverinfo3 = new Systems.SRX_Serverinfo();
					srx_Serverinfo3.id = 1;
					srx_Serverinfo3.ip = "192.168.1.5";
					if (!Global.Network.multihomed)
					{
						srx_Serverinfo3.extip = Global.Network.LocalIP;
					}
					srx_Serverinfo3.name = "[SERVER " + Versions.appVersion + "]";
					srx_Serverinfo3.port = defaultPort;
					srx_Serverinfo3.ipcport = 40708;
					srx_Serverinfo3.code = "";
					Systems.GSList.Add((int)srx_Serverinfo3.id, srx_Serverinfo3);
					num = -1;
				}
			}
			catch (Exception ex)
			{
				LogConsole.Show("Error loading GameServer settings " + ex);
				num = -2;
			}
			return num;
		}

		// Token: 0x06000024 RID: 36 RVA: 0x000021B7 File Offset: 0x000003B7
		public Systems(Systems.Client de)
		{
			this.client = de;
		}

		// Token: 0x06000025 RID: 37 RVA: 0x00003234 File Offset: 0x00001434
		public static void oPCode(Systems.Decode decode)
		{
			try
			{
				Systems systems = (Systems)decode.Packet;
				systems.PacketInformation = decode;
				PacketReader packetReader = new PacketReader(systems.PacketInformation.buffer);
				LogDebug.Show("Opcode: {0}", decode.opcode);
				ushort opcode = decode.opcode;
				if (opcode != 0)
				{
					if (opcode != 257)
					{
						if (opcode != 52703)
						{
							LogConsole.Show("Default Opcode: {0:X}", decode.opcode);
						}
						else
						{
							ushort num = packetReader.UInt16();
							if (num == 12288)
							{
								LogDebug.Show("Launch: SV_AUTH_LOGIN_2");
								byte[] array = new byte[] { 8, 0, 211, 235, 1, 48, 0, 0 };
								string text = Program.HexStr(array);
								LogDebug.Show("S-> data: " + text);
								systems.client.Send(array);
							}
						}
					}
					else
					{
						byte[] array2 = Systems.ServerListPacket(systems.client.Version);
						string text2 = Program.HexStr(array2);
						LogDebug.Show("S-> data: " + text2);
						systems.client.Send(array2);
					}
				}
				else
				{
					ushort num2 = packetReader.UInt16();
					if (num2 == 4880)
					{
						LogDebug.Show("Launch: SV_AUTH_LOGIN");
						byte[] array3 = new byte[] { 8, 0, 235, 203, 18, 19, 0, 0 };
						string text3 = Program.HexStr(array3);
						LogDebug.Show("S-> data: " + text3);
						systems.client.Send(array3);
					}
					else if (num2 == 20480)
					{
						LogDebug.Show("Launch: SV_AUTH_LOGIN_3");
					}
					else if (num2 == 16384)
					{
						LogDebug.Show("Launch: SV_AUTH_LOGIN_4");
						byte[] array4 = new byte[]
						{
							12, 0, 175, 27, 1, 64, 0, 0, 28, 0,
							0, 0
						};
						string text4 = Program.HexStr(array4);
						LogDebug.Show("S-> data: " + text4);
						systems.client.Send(array4);
					}
				}
			}
			catch (Exception)
			{
			}
		}

		// Token: 0x06000026 RID: 38 RVA: 0x0000342C File Offset: 0x0000162C
		public static byte[] ServerListPacket(int cliVersion)
		{
			using var packetWriter = new Systems.PacketWriter(257);
			Systems.SRX_Serverinfo[] online = Systems.GSList.Values
				.Where(server => server.status == 1)
				.ToArray();
			packetWriter.WriteByte(checked((byte)online.Length));
			foreach (Systems.SRX_Serverinfo server in online)
			{
					string[] array;
					if (!server.lan_wan)
					{
						array = server.ip.Split(new char[] { '.' });
					}
					else
					{
						array = server.wan.Split(new char[] { '.' });
					}
					byte b = Convert.ToByte(int.Parse(array[0]));
					byte b2 = Convert.ToByte(int.Parse(array[1]));
					byte b3 = Convert.ToByte(int.Parse(array[2]));
					byte b4 = Convert.ToByte(int.Parse(array[3]));
					packetWriter.WriteByte(b);
					packetWriter.WriteByte(b2);
					packetWriter.WriteByte(b3);
					packetWriter.WriteByte(b4);
					// Layout EXATO que o client espera (igual ao CarlosX original):
					//   [IP 4][port 2 swapped/BE][game pair][user pair]
					// A PORTA vem LOGO APOS o IP (nao no fim!). Mover ela pro fim
					// fazia o client ler a porta de outra posicao (=0) -> "world server
					// failed" sem nem tentar conectar.
					// Layout EXATO do broker original (capturado byte-a-byte):
					//   [IP 4][port swapped 2][usedSala][maxSalas][usedSlots][maxSlots]
					byte[] pb = Program.GetBytesUInt16(server.port);
					packetWriter.WriteByte(pb[1]);
					packetWriter.WriteByte(pb[0]);
					packetWriter.WriteUInt16(server.usedSala);
					packetWriter.WriteUInt16(server.maxSalas);
					packetWriter.WriteUInt16(server.usedSlots);
					packetWriter.WriteUInt16(server.maxSlots);
			}
			return packetWriter.ToArray();
		}

		// Token: 0x0400000F RID: 15
		public static Dictionary<int, Systems.SRX_Serverinfo> GSList = new Dictionary<int, Systems.SRX_Serverinfo>();

		// Token: 0x04000012 RID: 18
		internal Systems.Client client;

		// Token: 0x04000013 RID: 19
		internal Systems.Decode PacketInformation;

		// Token: 0x04000014 RID: 20
		private static short User_Current;

		// Token: 0x04000015 RID: 21
		public static int MAX_BUFFER = 8192;
	}
}
