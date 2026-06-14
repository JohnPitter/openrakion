using System;
using System.Net;
using System.Net.Sockets;

namespace BrokenServer
{
	public partial class Systems
	{
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
	}
}
