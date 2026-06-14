using System;
using System.Net.Sockets;

namespace BrokenServer
{
	public partial class Systems
	{
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
	}
}
