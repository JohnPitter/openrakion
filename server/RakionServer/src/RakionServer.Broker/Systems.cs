using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using BrokenServer.Global;
using RakionServer.Common;

namespace BrokenServer
{
	// Token: 0x02000007 RID: 7
	public class Systems
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

		// Token: 0x06000022 RID: 34 RVA: 0x000021B5 File Offset: 0x000003B5
		public static void CheckServerExpired(int seconds)
		{
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
				Systems.PacketReader packetReader = new Systems.PacketReader(systems.PacketInformation.buffer);
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
			Systems.PacketWriter packetWriter = new Systems.PacketWriter();
			packetWriter.Create(257);
			int num = Systems.GSList.Count<KeyValuePair<int, Systems.SRX_Serverinfo>>();
			packetWriter.Byte((byte)num);
			foreach (KeyValuePair<int, Systems.SRX_Serverinfo> keyValuePair in Systems.GSList)
			{
				if (keyValuePair.Value.status == 1)
				{
					string[] array;
					if (!keyValuePair.Value.lan_wan)
					{
						array = keyValuePair.Value.ip.Split(new char[] { '.' });
					}
					else
					{
						array = keyValuePair.Value.wan.Split(new char[] { '.' });
					}
					byte b = Convert.ToByte(int.Parse(array[0]));
					byte b2 = Convert.ToByte(int.Parse(array[1]));
					byte b3 = Convert.ToByte(int.Parse(array[2]));
					byte b4 = Convert.ToByte(int.Parse(array[3]));
					packetWriter.Byte(b);
					packetWriter.Byte(b2);
					packetWriter.Byte(b3);
					packetWriter.Byte(b4);
					// Layout EXATO que o client espera (igual ao CarlosX original):
					//   [IP 4][port 2 swapped/BE][game pair][user pair]
					// A PORTA vem LOGO APOS o IP (nao no fim!). Mover ela pro fim
					// fazia o client ler a porta de outra posicao (=0) -> "world server
					// failed" sem nem tentar conectar.
					// Layout EXATO do broker original (capturado byte-a-byte):
					//   [IP 4][port swapped 2][usedSala][maxSalas][usedSlots][maxSlots]
					byte[] pb = Program.GetBytesUInt16(keyValuePair.Value.port);
					packetWriter.Byte(pb[1]);
					packetWriter.Byte(pb[0]);
					packetWriter.Word(keyValuePair.Value.usedSala);
					packetWriter.Word(keyValuePair.Value.maxSalas);
					packetWriter.Word(keyValuePair.Value.usedSlots);
					packetWriter.Word(keyValuePair.Value.maxSlots);
				}
				else
				{
					packetWriter.Word(0);
					LogDebug.Show("[SERVER] Server-Offline: {0}", keyValuePair.Value.name);
				}
			}
			return packetWriter.GetBytes();
		}

		// Token: 0x0400000F RID: 15
		public static Dictionary<int, Systems.SRX_Serverinfo> GSList = new Dictionary<int, Systems.SRX_Serverinfo>();

		// Token: 0x04000010 RID: 16
		public static string DownloadServer = "";

		// Token: 0x04000011 RID: 17
		public static short DownloadPort = 15000;

		// Token: 0x04000012 RID: 18
		internal Systems.Client client;

		// Token: 0x04000013 RID: 19
		internal Systems.Decode PacketInformation;

		// Token: 0x04000014 RID: 20
		private static short User_Current;

		// Token: 0x04000015 RID: 21
		public static int MAX_BUFFER = 8192;

		// Token: 0x02000008 RID: 8
		public class SRX_Serverinfo
		{
			// Token: 0x04000016 RID: 22
			public ushort id = 1;

			// Token: 0x04000017 RID: 23
			public ushort port = 40708;

			// Token: 0x04000018 RID: 24
			public ushort ipcport = 40708;

			// Token: 0x04000019 RID: 25
			public ushort maxSlots = 500;

			// Token: 0x0400001A RID: 26
			public ushort usedSlots;

			// Token: 0x0400001B RID: 27
			public ushort maxSalas = 2000;

			// Token: 0x0400001C RID: 28
			public ushort usedSala;

			// Token: 0x0400001D RID: 29
			public string name = "BrokenServer";

			// Token: 0x0400001E RID: 30
			public string ip = "192.168.1.5";

			// Token: 0x0400001F RID: 31
			public string wan = "192.168.1.5";

			// Token: 0x04000020 RID: 32
			public string extip = "1";

			// Token: 0x04000021 RID: 33
			public string code = "";

			// Token: 0x04000022 RID: 34
			public bool lan_wan;

			// Token: 0x04000023 RID: 35
			public byte status;

			// Token: 0x04000024 RID: 36
			public int Version;

			// Token: 0x04000025 RID: 37
			public DateTime lastPing = DateTime.MinValue;
		}

		// Token: 0x02000009 RID: 9
		public class Decode
		{
			// Token: 0x17000001 RID: 1
			// (get) Token: 0x06000029 RID: 41 RVA: 0x000021F0 File Offset: 0x000003F0
			public ushort opcode
			{
				get
				{
					return this.OPCODE;
				}
			}

			// Token: 0x17000002 RID: 2
			// (get) Token: 0x0600002A RID: 42 RVA: 0x000021F8 File Offset: 0x000003F8
			public byte[] buffer
			{
				get
				{
					return this.BUFFER;
				}
			}

			// Token: 0x17000003 RID: 3
			// (get) Token: 0x0600002B RID: 43 RVA: 0x00002200 File Offset: 0x00000400
			public Socket Client
			{
				get
				{
					return this.socket;
				}
			}

			// Token: 0x17000004 RID: 4
			// (get) Token: 0x0600002C RID: 44 RVA: 0x00002208 File Offset: 0x00000408
			public object Networking
			{
				get
				{
					return this.NET;
				}
			}

			// Token: 0x17000005 RID: 5
			// (get) Token: 0x0600002D RID: 45 RVA: 0x00002210 File Offset: 0x00000410
			public object Packet
			{
				get
				{
					return this.packet;
				}
			}

			// Token: 0x0600002E RID: 46 RVA: 0x00003690 File Offset: 0x00001890
			public Decode(byte[] buffer)
			{
				try
				{
					this.ms = new MemoryStream(buffer);
					this.br = new BinaryReader(this.ms);
					this.dataSize = (ushort)this.br.ReadInt16();
					this.br.Close();
					this.ms.Close();
					this.br.Dispose();
					this.ms.Dispose();
				}
				catch (Exception)
				{
				}
			}

			// Token: 0x0600002F RID: 47 RVA: 0x00003714 File Offset: 0x00001914
			public Decode(Socket wSock, byte[] buffer, Systems.Client net, object packetf)
			{
				try
				{
					this.packet = packetf;
					this.ms = new MemoryStream(buffer);
					this.br = new BinaryReader(this.ms);
					this.dataSize = (ushort)this.br.ReadInt16();
					byte[] array = new byte[(int)this.dataSize];
					Array.Copy(buffer, 4, array, 0, (int)this.dataSize);
					this.BUFFER = array;
					this.OPCODE = this.br.ReadUInt16();
					this.socket = wSock;
					this.NET = net;
				}
				catch (Exception)
				{
				}
			}

			// Token: 0x06000030 RID: 48 RVA: 0x000037B4 File Offset: 0x000019B4
			public static string StringToPack(byte[] buff)
			{
				string text = null;
				foreach (byte b in buff)
				{
					text += b.ToString("X2");
				}
				return text;
			}

			// Token: 0x04000026 RID: 38
			private ushort OPCODE;

			// Token: 0x04000027 RID: 39
			private byte[] BUFFER;

			// Token: 0x04000028 RID: 40
			private Socket socket;

			// Token: 0x04000029 RID: 41
			private object NET;

			// Token: 0x0400002A RID: 42
			private object packet;

			// Token: 0x0400002B RID: 43
			public ushort dataSize;

			// Token: 0x0400002C RID: 44
			private MemoryStream ms;

			// Token: 0x0400002D RID: 45
			private BinaryReader br;
		}

		// Token: 0x02000013 RID: 19
		public class Server
		{
			// Token: 0x14000006 RID: 6
			// (add) Token: 0x0600008F RID: 143 RVA: 0x00003FEC File Offset: 0x000021EC
			// (remove) Token: 0x06000090 RID: 144 RVA: 0x00004024 File Offset: 0x00002224
			public event Systems.Server.dConnect OnConnect;

			// Token: 0x14000007 RID: 7
			// (add) Token: 0x06000091 RID: 145 RVA: 0x0000405C File Offset: 0x0000225C
			// (remove) Token: 0x06000092 RID: 146 RVA: 0x00004094 File Offset: 0x00002294
			public event Systems.Server.dError OnError;

			// Token: 0x06000093 RID: 147 RVA: 0x000040CC File Offset: 0x000022CC
			public void Start(string ip, int PORT)
			{
				IPAddress ipaddress = IPAddress.Any;
				if (ip != "")
				{
					ipaddress = IPAddress.Parse(ip);
				}
				IPEndPoint ipendPoint = new IPEndPoint(ipaddress, PORT);
				this.serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
				try
				{
					this.serverSocket.Bind(ipendPoint);
					this.serverSocket.Listen(5);
					this.serverSocket.BeginAccept(new AsyncCallback(this.ClientConnect), null);
				}
				catch (SocketException ex)
				{
					if (ex.ErrorCode != 10049)
					{
						int errorCode = ex.ErrorCode;
					}
				}
				catch (Exception ex2)
				{
					this.OnError(ex2);
				}
			}

			// Token: 0x06000094 RID: 148 RVA: 0x00004180 File Offset: 0x00002380
			private void ClientConnect(IAsyncResult ar)
			{
				try
				{
					Socket socket = this.serverSocket.EndAccept(ar);
					socket.DontFragment = false;
					object obj = null;
					Systems.Client client = new Systems.Client();
					try
					{
						this.OnConnect(ref obj, client);
					}
					catch (Exception)
					{
					}
					client.Packets = obj;
					client.clientSocket = socket;
					this.serverSocket.BeginAccept(new AsyncCallback(this.ClientConnect), null);
					try
					{
						socket.BeginReceive(client.tmpbuf, 0, client.tmpbuf.Length, SocketFlags.None, new AsyncCallback(client.ReceiveData), socket);
					}
					catch (SocketException)
					{
					}
					catch (Exception)
					{
					}
				}
				catch (ObjectDisposedException)
				{
				}
				catch (Exception ex)
				{
					this.OnError(ex);
				}
			}

			// Token: 0x04000040 RID: 64
			private Socket serverSocket;

			// Token: 0x02000014 RID: 20
			// (Invoke) Token: 0x06000097 RID: 151
			public delegate void dReceive(Systems.Decode de);

			// Token: 0x02000015 RID: 21
			// (Invoke) Token: 0x0600009B RID: 155
			public delegate void dConnect(ref object de, Systems.Client net);

			// Token: 0x02000016 RID: 22
			// (Invoke) Token: 0x0600009F RID: 159
			public delegate void dError(Exception ex);

			// Token: 0x02000017 RID: 23
			// (Invoke) Token: 0x060000A3 RID: 163
			public delegate void dDisconnect(object o);
		}

		// Token: 0x02000018 RID: 24
		public class Client
		{
			// Token: 0x14000008 RID: 8
			// (add) Token: 0x060000A6 RID: 166 RVA: 0x00004264 File Offset: 0x00002464
			// (remove) Token: 0x060000A7 RID: 167 RVA: 0x00004298 File Offset: 0x00002498
			public static event Systems.Client.dReceive OnReceiveData;

			// Token: 0x14000009 RID: 9
			// (add) Token: 0x060000A8 RID: 168 RVA: 0x000042CC File Offset: 0x000024CC
			// (remove) Token: 0x060000A9 RID: 169 RVA: 0x00004300 File Offset: 0x00002500
			public static event Systems.Client.dDisconnect OnDisconnect;

			// Token: 0x17000014 RID: 20
			// (get) Token: 0x060000AA RID: 170 RVA: 0x000023DE File Offset: 0x000005DE
			// (set) Token: 0x060000AB RID: 171 RVA: 0x000023E6 File Offset: 0x000005E6
			public object Packets { get; set; }

			// Token: 0x060000AC RID: 172 RVA: 0x00004334 File Offset: 0x00002534
			public void ReceiveData(IAsyncResult ar)
			{
				Socket socket = (Socket)ar.AsyncState;
				try
				{
					if (socket.Connected)
					{
						int num = socket.EndReceive(ar);
						bool flag = true;
						if (num > 0)
						{
							if (num + this.bufCount > Systems.MAX_BUFFER)
							{
								flag = false;
								this.LocalDisconnect(socket);
							}
							else
							{
								Buffer.BlockCopy(this.tmpbuf, 0, this.buffer, this.bufCount, num);
								this.bufCount += num;
							}
						}
						else
						{
							flag = false;
							this.LocalDisconnect(socket);
						}
						while (flag)
						{
							flag = false;
							if (this.bufCount >= 4)
							{
								Systems.Decode decode = new Systems.Decode(this.buffer);
								if (this.bufCount >= (int)(decode.dataSize - 2))
								{
									decode = new Systems.Decode(socket, this.buffer, this, this.Packets);
									Systems.Client.OnReceiveData(decode);
									this.bufCount -= (int)decode.dataSize;
									if (this.bufCount > 0)
									{
										Buffer.BlockCopy(this.buffer, (int)(2 + decode.dataSize), this.buffer, 0, this.bufCount);
										flag = true;
									}
								}
							}
						}
						if (socket != null && socket.Connected)
						{
							socket.BeginReceive(this.tmpbuf, 0, this.tmpbuf.Length, SocketFlags.None, new AsyncCallback(this.ReceiveData), socket);
						}
					}
					else
					{
						this.LocalDisconnect(socket);
					}
				}
				catch (SocketException)
				{
					this.LocalDisconnect(socket);
				}
				catch (Exception)
				{
					this.LocalDisconnect(socket);
				}
			}

			// Token: 0x060000AD RID: 173 RVA: 0x000044CC File Offset: 0x000026CC
			public void Send(byte[] buff)
			{
				try
				{
					if (buff != null && buff.Length > 0 && this.clientSocket.Connected)
					{
						this.clientSocket.Send(buff);
					}
				}
				catch (Exception)
				{
				}
			}

			// Token: 0x060000AE RID: 174 RVA: 0x00004514 File Offset: 0x00002714
			private void LocalDisconnect(Socket s)
			{
				if (s != null)
				{
					try
					{
						if (Systems.Client.OnDisconnect != null)
						{
							Systems.Client.OnDisconnect(this.Packets);
						}
					}
					catch (Exception)
					{
					}
				}
			}

			// Token: 0x060000AF RID: 175 RVA: 0x000023EF File Offset: 0x000005EF
			public void Disconnect(Socket s)
			{
				if (s.Connected)
				{
					s.Shutdown(SocketShutdown.Both);
					s.Disconnect(true);
					s.Close();
				}
			}

			// Token: 0x04000043 RID: 67
			public Socket clientSocket;

			// Token: 0x04000044 RID: 68
			public int bufCount;

			// Token: 0x04000045 RID: 69
			public byte[] buffer = new byte[Systems.MAX_BUFFER];

			// Token: 0x04000046 RID: 70
			public byte[] tmpbuf = new byte[128];

			// Token: 0x04000047 RID: 71
			public int Version;

			// Token: 0x02000019 RID: 25
			// (Invoke) Token: 0x060000B2 RID: 178
			public delegate void dReceive(Systems.Decode de);

			// Token: 0x0200001A RID: 26
			// (Invoke) Token: 0x060000B6 RID: 182
			public delegate void dDisconnect(object o);
		}

		// Token: 0x0200001B RID: 27
		public class PacketReader
		{
			// Token: 0x060000B9 RID: 185 RVA: 0x00002435 File Offset: 0x00000635
			public PacketReader(byte[] data)
			{
				this.ms = new MemoryStream(data);
				this.br = new BinaryReader(this.ms);
			}

			// Token: 0x060000BA RID: 186 RVA: 0x0000245A File Offset: 0x0000065A
			public byte Byte()
			{
				return this.br.ReadByte();
			}

			// Token: 0x060000BB RID: 187 RVA: 0x00002467 File Offset: 0x00000667
			public ushort UInt16()
			{
				return this.br.ReadUInt16();
			}

			// Token: 0x060000BC RID: 188 RVA: 0x00002474 File Offset: 0x00000674
			public uint UInt32()
			{
				return this.br.ReadUInt32();
			}

			// Token: 0x060000BD RID: 189 RVA: 0x00002481 File Offset: 0x00000681
			public ulong UInt64()
			{
				return this.br.ReadUInt64();
			}

			// Token: 0x060000BE RID: 190 RVA: 0x0000248E File Offset: 0x0000068E
			public short Int16()
			{
				return this.br.ReadInt16();
			}

			// Token: 0x060000BF RID: 191 RVA: 0x0000249B File Offset: 0x0000069B
			public int Int32()
			{
				return this.br.ReadInt32();
			}

			// Token: 0x060000C0 RID: 192 RVA: 0x000024A8 File Offset: 0x000006A8
			public long Int64()
			{
				return this.br.ReadInt64();
			}

			// Token: 0x060000C1 RID: 193 RVA: 0x000024B5 File Offset: 0x000006B5
			public float Single()
			{
				return this.br.ReadSingle();
			}

			// Token: 0x060000C2 RID: 194 RVA: 0x00004550 File Offset: 0x00002750
			public string String(int len)
			{
				StringBuilder stringBuilder = new StringBuilder();
				char[] array = this.br.ReadChars(len);
				foreach (char c in array)
				{
					stringBuilder.Append(c.ToString());
				}
				return stringBuilder.ToString();
			}

			// Token: 0x060000C3 RID: 195 RVA: 0x000045A0 File Offset: 0x000027A0
			public string Text()
			{
				short num = this.Int16();
				return this.String((int)num);
			}

			// Token: 0x060000C4 RID: 196 RVA: 0x000045BC File Offset: 0x000027BC
			public void Skip(int HowMany)
			{
				for (int i = 1; i <= HowMany; i++)
				{
					this.br.ReadByte();
				}
			}

			// Token: 0x060000C5 RID: 197 RVA: 0x000024C2 File Offset: 0x000006C2
			public void Close()
			{
				this.br.Dispose();
				this.ms.Dispose();
				this.br.Close();
				this.ms.Close();
				GC.Collect(GC.GetGeneration(this));
			}

			// Token: 0x04000049 RID: 73
			private MemoryStream ms;

			// Token: 0x0400004A RID: 74
			private BinaryReader br;
		}

		// Token: 0x0200001C RID: 28
		public class PacketWriter
		{
			// Token: 0x060000C6 RID: 198 RVA: 0x000024FB File Offset: 0x000006FB
			public PacketWriter()
			{
			}

			// Token: 0x060000C7 RID: 199 RVA: 0x0000250E File Offset: 0x0000070E
			public void AddBuffer(byte[] buffer)
			{
				this.bw.Write(buffer);
			}

			// Token: 0x060000C8 RID: 200 RVA: 0x000045E4 File Offset: 0x000027E4
			public PacketWriter(bool b)
			{
				if (b)
				{
					this.bw = null;
					this.ms = null;
					this.ms = new MemoryStream();
					this.bw = new BinaryWriter(this.ms);
					this.bw.Write(0);
				}
			}

			// Token: 0x060000C9 RID: 201 RVA: 0x0000251C File Offset: 0x0000071C
			public int Length()
			{
				return (int)(this.ms.Position - 6L);
			}

			// Token: 0x060000CA RID: 202 RVA: 0x00002534 File Offset: 0x00000734
			public void Byte(byte data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000CB RID: 203 RVA: 0x00002542 File Offset: 0x00000742
			public void Create(ushort opcode)
			{
				this.bw = null;
				this.ms = null;
				this.ms = new MemoryStream();
				this.bw = new BinaryWriter(this.ms);
				// placeholder do SIZE = u16 (2 bytes), NAO int (4). O broker original
				// envia [size u16][opcode u16][payload] (header de 4 bytes). Escrever int
				// aqui adicionava 2 bytes 00 00 extras que deslocavam todo o payload e
				// faziam o client ler IP/porta do offset errado -> "World connection failed".
				this.bw.Write((ushort)0);
				this.Word(opcode);
			}

			// Token: 0x060000CC RID: 204 RVA: 0x00002581 File Offset: 0x00000781
			public void Word(ushort data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000CD RID: 205 RVA: 0x0000258F File Offset: 0x0000078F
			public void Word(short data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000CE RID: 206 RVA: 0x0000258F File Offset: 0x0000078F
			public void Word(short data, bool test = false)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000CF RID: 207 RVA: 0x0000258F File Offset: 0x0000078F
			public void WordInt(short data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D0 RID: 208 RVA: 0x0000259D File Offset: 0x0000079D
			public void DWord(uint data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D1 RID: 209 RVA: 0x000025AB File Offset: 0x000007AB
			public void DWord(int data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D2 RID: 210 RVA: 0x000025AB File Offset: 0x000007AB
			public void DWordInt(int data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D3 RID: 211 RVA: 0x000025B9 File Offset: 0x000007B9
			public void LWord(ulong data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D4 RID: 212 RVA: 0x000025C7 File Offset: 0x000007C7
			public void LWord(long data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D5 RID: 213 RVA: 0x000025D5 File Offset: 0x000007D5
			public void Float(float data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D6 RID: 214 RVA: 0x000025D5 File Offset: 0x000007D5
			public void FloatFour(float data)
			{
				this.bw.Write(data);
			}

			// Token: 0x060000D7 RID: 215 RVA: 0x000025E3 File Offset: 0x000007E3
			public void Text(string data)
			{
				this.Word((short)data.Length);
				this.String(data);
			}

			// Token: 0x060000D8 RID: 216 RVA: 0x000025F9 File Offset: 0x000007F9
			public void Bool(bool b)
			{
				this.bw.Write(b);
			}

			// Token: 0x060000D9 RID: 217 RVA: 0x0000463C File Offset: 0x0000283C
			public void String(string data)
			{
				char[] array = new char[data.Length];
				for (int i = 0; i < data.Length; i++)
				{
					array[i] = Convert.ToChar(data.Substring(i, 1));
					this.bw.Write(array[i]);
				}
			}

			// Token: 0x060000DA RID: 218 RVA: 0x00004684 File Offset: 0x00002884
			public void UString(string data)
			{
				char[] array = new char[data.Length];
				for (int i = 0; i < data.Length; i++)
				{
					array[i] = Convert.ToChar(data.Substring(i, 1));
					this.bw.Write(array[i]);
					this.bw.Write(0);
				}
			}

			// Token: 0x060000DB RID: 219 RVA: 0x0000463C File Offset: 0x0000283C
			public void HexString(string data)
			{
				char[] array = new char[data.Length];
				for (int i = 0; i < data.Length; i++)
				{
					array[i] = Convert.ToChar(data.Substring(i, 1));
					this.bw.Write(array[i]);
				}
			}

			// Token: 0x060000DC RID: 220 RVA: 0x000046D8 File Offset: 0x000028D8
			public byte[] GetBytes()
			{
				byte[] array = new byte[1];
				ushort num = (ushort)this.ms.Position;
				this.ms.Position = 0L;
				this.bw.Write(num);
				this.bw.Flush();
				this.bw.Close();
				byte[] array2 = this.ms.ToArray();
				this.ms.Close();
				return array2;
			}

			// Token: 0x0400004B RID: 75
			private MemoryStream ms = new MemoryStream();

			// Token: 0x0400004C RID: 76
			private BinaryWriter bw;
		}
	}
}
