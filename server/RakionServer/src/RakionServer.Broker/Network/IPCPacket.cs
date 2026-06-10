using System;
using System.IO;
using System.Text;

namespace BrokenServer.Network
{
	// Token: 0x02000003 RID: 3
	public class IPCPacket : IDisposable
	{
		// Token: 0x06000001 RID: 1 RVA: 0x00002090 File Offset: 0x00000290
		public IPCPacket()
		{
			this.ms = new MemoryStream();
			this.bw = new BinaryWriter(this.ms);
		}

		// Token: 0x06000002 RID: 2 RVA: 0x000027AC File Offset: 0x000009AC
		~IPCPacket()
		{
			this.Dispose();
		}

		// Token: 0x06000003 RID: 3 RVA: 0x000020B4 File Offset: 0x000002B4
		public void Dispose()
		{
			if (this.ms != null)
			{
				this.bw.Close();
				this.ms.Close();
				this.bw = null;
				this.ms = null;
			}
		}

		// Token: 0x06000004 RID: 4 RVA: 0x000020E2 File Offset: 0x000002E2
		public void WriteByte(int aByte)
		{
			this.bw.Write((byte)aByte);
		}

		// Token: 0x06000005 RID: 5 RVA: 0x000020F1 File Offset: 0x000002F1
		public void WriteWord(int aWord)
		{
			this.bw.Write((ushort)aWord);
		}

		// Token: 0x06000006 RID: 6 RVA: 0x00002100 File Offset: 0x00000300
		public void WriteString(string aString)
		{
			this.WriteByte(aString.Length);
			this.bw.Write(Encoding.ASCII.GetBytes(aString));
		}

		// Token: 0x06000007 RID: 7 RVA: 0x00002124 File Offset: 0x00000324
		public void WriteBytes(byte[] aBytes)
		{
			this.bw.Write(aBytes);
		}

		// Token: 0x06000008 RID: 8 RVA: 0x000027D8 File Offset: 0x000009D8
		public void AddCRC()
		{
			long position = this.ms.Position;
			this.ms.Position = 0L;
			this.bw.Flush();
			byte b = Servers.BCRC(this.ms.ToArray());
			this.ms.Position = position;
			this.WriteByte((int)b);
		}

		// Token: 0x06000009 RID: 9 RVA: 0x00002834 File Offset: 0x00000A34
		public byte[] GetBytes()
		{
			long position = this.ms.Position;
			this.ms.Position = 0L;
			this.bw.Flush();
			byte[] array = this.ms.ToArray();
			this.ms.Position = position;
			return array;
		}

		// Token: 0x04000006 RID: 6
		private MemoryStream ms;

		// Token: 0x04000007 RID: 7
		private BinaryWriter bw;
	}
}
