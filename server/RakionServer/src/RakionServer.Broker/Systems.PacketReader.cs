using System;
using System.IO;
using System.Text;

namespace BrokenServer
{
	public partial class Systems
	{
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
	}
}
