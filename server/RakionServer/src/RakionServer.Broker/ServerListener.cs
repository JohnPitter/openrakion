using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace BrokenServer
{
	// Token: 0x02000026 RID: 38
	public class ServerListener
	{
		// Token: 0x060000FD RID: 253 RVA: 0x000050D8 File Offset: 0x000032D8
		public static void StartListening()
		{
			IPEndPoint ipendPoint = new IPEndPoint(IPAddress.Parse("192.168.1.5"), 40706);
			Socket socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
			try
			{
				socket.Bind(ipendPoint);
				socket.Listen(100);
				for (;;)
				{
					ServerListener.allDone.Reset();
					LogConsole.Show("Waiting for a connection...");
					socket.BeginAccept(new AsyncCallback(ServerListener.AcceptCallback), socket);
					ServerListener.allDone.WaitOne();
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine(ex.ToString());
			}
			Console.WriteLine("\nPress ENTER to continue...");
			Console.Read();
		}

		// Token: 0x060000FE RID: 254 RVA: 0x00005178 File Offset: 0x00003378
		public static void AcceptCallback(IAsyncResult ar)
		{
			ServerListener.allDone.Set();
			Socket socket = (Socket)ar.AsyncState;
			Socket socket2 = socket.EndAccept(ar);
			StateObject stateObject = new StateObject();
			stateObject.workSocket = socket2;
			socket2.BeginReceive(stateObject.buffer, 0, 1024, SocketFlags.None, new AsyncCallback(ServerListener.ReadCallback), stateObject);
		}

		// Token: 0x060000FF RID: 255 RVA: 0x000051D4 File Offset: 0x000033D4
		public static void ReadCallback(IAsyncResult ar)
		{
			string text = string.Empty;
			StateObject stateObject = (StateObject)ar.AsyncState;
			Socket workSocket = stateObject.workSocket;
			int num = workSocket.EndReceive(ar);
			if (num > 0)
			{
				stateObject.sb.Append(Encoding.ASCII.GetString(stateObject.buffer, 0, num));
				text = stateObject.sb.ToString();
				if (text.IndexOf("<EOF>") > -1)
				{
					Console.WriteLine("Read {0} bytes from socket. \n Data : {1}", text.Length, text);
					ServerListener.Send(workSocket, text);
					return;
				}
				workSocket.BeginReceive(stateObject.buffer, 0, 1024, SocketFlags.None, new AsyncCallback(ServerListener.ReadCallback), stateObject);
			}
		}

		// Token: 0x06000100 RID: 256 RVA: 0x00005280 File Offset: 0x00003480
		private static void Send(Socket handler, string data)
		{
			byte[] bytes = Encoding.ASCII.GetBytes(data);
			handler.BeginSend(bytes, 0, bytes.Length, SocketFlags.None, new AsyncCallback(ServerListener.SendCallback), handler);
		}

		// Token: 0x06000101 RID: 257 RVA: 0x000052B4 File Offset: 0x000034B4
		private static void SendCallback(IAsyncResult ar)
		{
			try
			{
				Socket socket = (Socket)ar.AsyncState;
				int num = socket.EndSend(ar);
				LogConsole.Show("Sent {0} bytes to client.", num);
				socket.Shutdown(SocketShutdown.Both);
				socket.Close();
			}
			catch (Exception ex)
			{
				LogConsole.Show(ex.ToString());
			}
		}

		// Token: 0x04000060 RID: 96
		public static ManualResetEvent allDone = new ManualResetEvent(false);
	}
}
