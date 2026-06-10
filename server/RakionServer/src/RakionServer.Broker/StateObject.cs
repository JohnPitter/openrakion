using System;
using System.Net.Sockets;
using System.Text;

namespace BrokenServer
{
	// Token: 0x02000025 RID: 37
	public class StateObject
	{
		// Token: 0x0400005C RID: 92
		public const int BufferSize = 1024;

		// Token: 0x0400005D RID: 93
		public Socket workSocket;

		// Token: 0x0400005E RID: 94
		public byte[] buffer = new byte[1024];

		// Token: 0x0400005F RID: 95
		public StringBuilder sb = new StringBuilder();
	}
}
