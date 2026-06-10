using System;
using BrokenServer.Global;

namespace BrokenServer.Definitions
{
	// Token: 0x0200001D RID: 29
	internal class Bootlogo
	{
		// Token: 0x060000DD RID: 221 RVA: 0x00002607 File Offset: 0x00000807
		public static void _Load()
		{
			Console.ForegroundColor = ConsoleColor.Magenta;
			Console.Title = "BrokenServer " + Versions.appVersion;
			Console.WriteLine("=============================BrokenServer=============================");
			Console.ForegroundColor = ConsoleColor.Gray;
		}
	}
}
