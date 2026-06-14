using System;
using System.IO;

namespace BrokenServer
{
	public partial class Systems
	{
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
