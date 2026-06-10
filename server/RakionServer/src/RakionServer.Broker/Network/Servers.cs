using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BrokenServer.Network
{
	// Token: 0x02000004 RID: 4
	public class Servers
	{
		// Token: 0x0600000A RID: 10 RVA: 0x00002132 File Offset: 0x00000332
		public static byte BCRC(byte[] aBytes)
		{
			return Servers.BCRC(aBytes, aBytes.Length);
		}

		// Token: 0x0600000B RID: 11 RVA: 0x00002888 File Offset: 0x00000A88
		public static byte BCRC(byte[] aBytes, int aLen)
		{
			byte b = 0;
			for (int i = 0; i < aLen; i++)
			{
				b ^= aBytes[i];
			}
			return b;
		}

		// Token: 0x0600000C RID: 12 RVA: 0x0000213D File Offset: 0x0000033D
		public static void IPCdeCode(ref byte[] data, string code, int aLen)
		{
			if (code != "")
			{
				Servers.IPCenCode(ref data, code, aLen);
			}
		}

		// Token: 0x0600000D RID: 13 RVA: 0x00002154 File Offset: 0x00000354
		public static void IPCdeCode(ref byte[] data, string code)
		{
			Servers.IPCdeCode(ref data, code, data.Length);
		}

		// Token: 0x0600000E RID: 14 RVA: 0x000028AC File Offset: 0x00000AAC
		public static void IPCenCode(ref byte[] data, string code, int aLen)
		{
			if (code != "")
			{
				int num = 0;
				byte b = (byte)(data[0] ^ data[1] ^ data[2]);
				byte[] bytes = new ASCIIEncoding().GetBytes(code);
				for (int i = 3; i < aLen; i++)
				{
					data[i] = (byte)((int)(data[i] ^ bytes[num++] ^ 150) ^ i ^ (int)b);
					if (num >= bytes.Length)
					{
						num = 0;
					}
				}
			}
		}

		// Token: 0x0600000F RID: 15 RVA: 0x00002161 File Offset: 0x00000361
		public static void IPCenCode(ref byte[] data, string code)
		{
			Servers.IPCenCode(ref data, code, data.Length);
		}

		// Token: 0x04000008 RID: 8
		public static int IPCPort;

		// Token: 0x02000005 RID: 5
		public class IPCServer
		{
			// Token: 0x14000001 RID: 1
			// (add) Token: 0x06000011 RID: 17 RVA: 0x00002914 File Offset: 0x00000B14
			// (remove) Token: 0x06000012 RID: 18 RVA: 0x0000294C File Offset: 0x00000B4C
			public event Servers.IPCServer.dOnReceive OnReceive;

			// Token: 0x06000014 RID: 20 RVA: 0x00002984 File Offset: 0x00000B84
			public void Start(string listenIP, int PORT)
			{
				Servers.IPCPort = PORT;
				IPAddress ipaddress = IPAddress.Any;
				if (listenIP != "")
				{
					ipaddress = IPAddress.Parse(listenIP);
				}
				IPEndPoint ipendPoint = new IPEndPoint(ipaddress, PORT);
				IPEndPoint ipendPoint2 = new IPEndPoint(IPAddress.Any, 0);
				EndPoint endPoint = ipendPoint2;
				try
				{
					this.theServer = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
					this.theServer.Bind(ipendPoint);
					try
					{
						this.theServer.BeginReceiveFrom(this.buf, 0, this.buf.Length, SocketFlags.None, ref endPoint, new AsyncCallback(this.UdpReceiveCallback), this.theServer);
					}
					catch (Exception)
					{
						this.theServer.Close();
					}
				}
				catch (SocketException ex)
				{
					int errorCode = ex.ErrorCode;
				}
				catch (Exception)
				{
				}
				try
				{
					this.sendSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
				}
				catch (Exception)
				{
					this.sendSocket = null;
				}
			}

			// Token: 0x06000015 RID: 21 RVA: 0x00002A84 File Offset: 0x00000C84
			public void UdpReceiveCallback(IAsyncResult ar)
			{
				Socket socket = (Socket)ar.AsyncState;
				IPEndPoint ipendPoint = new IPEndPoint(IPAddress.Any, 0);
				EndPoint endPoint = ipendPoint;
				try
				{
					int num = socket.EndReceiveFrom(ar, ref endPoint);
					if (num > 0)
					{
						try
						{
							if (this.OnReceive != null)
							{
								byte[] array = new byte[num];
								Buffer.BlockCopy(this.buf, 0, array, 0, num);
								try
								{
									this.OnReceive(socket, endPoint, array);
								}
								catch (Exception)
								{
								}
							}
						}
						catch (Exception)
						{
						}
						socket.BeginReceiveFrom(this.buf, 0, this.buf.Length, SocketFlags.None, ref endPoint, new AsyncCallback(this.UdpReceiveCallback), socket);
					}
					else
					{
						this.theServer.Close();
					}
				}
				catch (SocketException ex)
				{
					if (ex.ErrorCode == 10054)
					{
						socket.BeginReceiveFrom(this.buf, 0, this.buf.Length, SocketFlags.None, ref endPoint, new AsyncCallback(this.UdpReceiveCallback), socket);
					}
				}
				catch (Exception)
				{
					this.theServer.Close();
				}
			}

			// Token: 0x06000016 RID: 22 RVA: 0x00002BA4 File Offset: 0x00000DA4
			public void Send(string destIP, int destPort, byte[] data)
			{
				try
				{
					IPAddress ipaddress = IPAddress.Parse(destIP);
					IPEndPoint ipendPoint = new IPEndPoint(ipaddress, destPort);
					this.sendSocket.SendTo(data, ipendPoint);
				}
				catch (Exception)
				{
				}
			}

			// Token: 0x06000017 RID: 23 RVA: 0x00002BE8 File Offset: 0x00000DE8
			public byte[] PacketRequestServerInfo(int iPort)
			{
				byte[] bytes;
				using (IPCPacket ipcpacket = new IPCPacket())
				{
					ipcpacket.WriteWord(iPort);
					ipcpacket.WriteByte(this.rnd.Next(1, 250));
					ipcpacket.WriteByte(0);
					ipcpacket.WriteWord(0);
					ipcpacket.AddCRC();
					bytes = ipcpacket.GetBytes();
				}
				return bytes;
			}

			// Token: 0x06000018 RID: 24 RVA: 0x00002C54 File Offset: 0x00000E54
			public byte[] PacketResponseServerInfo(int iPort, byte bStatus, ushort iMaxSlots, ushort iUsedSlots, ushort iVersion)
			{
				byte[] bytes;
				using (IPCPacket ipcpacket = new IPCPacket())
				{
					ipcpacket.WriteWord(iPort);
					ipcpacket.WriteByte(this.rnd.Next(1, 250));
					ipcpacket.WriteByte(2);
					ipcpacket.WriteWord(5);
					ipcpacket.WriteByte((int)bStatus);
					ipcpacket.WriteWord((int)iMaxSlots);
					ipcpacket.WriteWord((int)iUsedSlots);
					ipcpacket.WriteWord((int)iVersion);
					ipcpacket.AddCRC();
					bytes = ipcpacket.GetBytes();
				}
				return bytes;
			}

			// Token: 0x06000019 RID: 25 RVA: 0x00002CDC File Offset: 0x00000EDC
			public byte[] PacketRequestLogin(int iPort, string sUserID, string sPassword, ushort IPCid)
			{
				int num = sUserID.Length + sPassword.Length + 4;
				byte[] bytes;
				using (IPCPacket ipcpacket = new IPCPacket())
				{
					ipcpacket.WriteWord(iPort);
					ipcpacket.WriteByte(this.rnd.Next(1, 250));
					ipcpacket.WriteByte(1);
					ipcpacket.WriteWord(num);
					ipcpacket.WriteString(sUserID);
					ipcpacket.WriteString(sPassword);
					ipcpacket.WriteWord((int)IPCid);
					ipcpacket.AddCRC();
					bytes = ipcpacket.GetBytes();
				}
				return bytes;
			}

			// Token: 0x0600001A RID: 26 RVA: 0x00002199 File Offset: 0x00000399
			public byte[] PacketResponseLogin(int iPort, ushort wResult, ushort wID)
			{
				return this.PacketResponseLogin(iPort, wResult, wID, "");
			}

			// Token: 0x0600001B RID: 27 RVA: 0x00002D6C File Offset: 0x00000F6C
			public byte[] PacketResponseLogin(int iPort, ushort wResult, ushort wID, string sBanReason)
			{
				int num = sBanReason.Length + 5;
				byte[] bytes;
				using (IPCPacket ipcpacket = new IPCPacket())
				{
					ipcpacket.WriteWord(iPort);
					ipcpacket.WriteByte(this.rnd.Next(1, 250));
					ipcpacket.WriteByte(3);
					ipcpacket.WriteWord(num);
					ipcpacket.WriteWord((int)wID);
					ipcpacket.WriteWord((int)wResult);
					ipcpacket.WriteString(sBanReason);
					ipcpacket.AddCRC();
					bytes = ipcpacket.GetBytes();
				}
				return bytes;
			}

			// Token: 0x04000009 RID: 9
			public static int MAX_BUFFER = 8192;

			// Token: 0x0400000A RID: 10
			private Random rnd = new Random();

			// Token: 0x0400000B RID: 11
			public Socket theServer;

			// Token: 0x0400000C RID: 12
			private Socket sendSocket;

			// Token: 0x0400000D RID: 13
			public byte[] buf = new byte[Servers.IPCServer.MAX_BUFFER];

			// Token: 0x02000006 RID: 6
			// (Invoke) Token: 0x0600001E RID: 30
			public delegate void dOnReceive(Socket aServerSocket, EndPoint remoteEndPoint, byte[] data);
		}
	}
}
