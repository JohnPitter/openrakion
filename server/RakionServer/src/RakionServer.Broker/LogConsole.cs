using System;

namespace BrokenServer
{
	// Token: 0x02000021 RID: 33
	internal class LogConsole
	{
		// Token: 0x060000E2 RID: 226 RVA: 0x0000267C File Offset: 0x0000087C
		public static void Show(string lg1)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("[SERVER]: ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(lg1 + "\n");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		// Token: 0x060000E3 RID: 227 RVA: 0x000026AC File Offset: 0x000008AC
		public static void Show(string lg1, object arg0)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("[SERVER]: ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(lg1, arg0);
			Console.Write("\n");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		// Token: 0x060000E4 RID: 228 RVA: 0x000026DD File Offset: 0x000008DD
		public static void Show(string lg1, object arg0, object arg1)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("[SERVER]: ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(lg1, arg0, arg1);
			Console.Write("\n");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		// Token: 0x060000E5 RID: 229 RVA: 0x0000270F File Offset: 0x0000090F
		public static void Show(string lg1, object arg0, object arg1, object arg2)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("[SERVER]: ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(lg1, arg0, arg1, arg2);
			Console.Write("\n");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		// Token: 0x060000E6 RID: 230 RVA: 0x0000474C File Offset: 0x0000294C
		public static void Show(string lg1, object arg0, object arg1, object arg2, object arg3)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("[SERVER]: ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(lg1, new object[] { arg0, arg1, arg2, arg3 });
			Console.Write("\n");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		// Token: 0x060000E7 RID: 231 RVA: 0x000047A0 File Offset: 0x000029A0
		public static void Show(string lg1, object arg0, object arg1, object arg2, object arg3, object arg4)
		{
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("[SERVER]: ");
			Console.ForegroundColor = ConsoleColor.Green;
			Console.Write(lg1, new object[] { arg0, arg1, arg2, arg3, arg4 });
			Console.Write("\n");
			Console.ForegroundColor = ConsoleColor.Gray;
		}
	}
}
