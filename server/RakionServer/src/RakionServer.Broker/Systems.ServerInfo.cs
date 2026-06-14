using System;

namespace BrokenServer
{
	public partial class Systems
	{
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
	}
}
