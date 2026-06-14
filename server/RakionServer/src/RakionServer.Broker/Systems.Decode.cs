using System;
using System.IO;
using System.Net.Sockets;

namespace BrokenServer
{
	public partial class Systems
	{
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
	}
}
