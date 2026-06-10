using System;

namespace BrokenServer
{
	// Token: 0x02000022 RID: 34
	internal class LogDebug
	{
		// Token: 0x060000E9 RID: 233 RVA: 0x000047FC File Offset: 0x000029FC
		public static void Show(string lg1)
		{
			if (Program.debug == "1")
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("[DEBUG]");
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write(lg1 + "\n");
				Console.ForegroundColor = ConsoleColor.Gray;
			}
		}

		// Token: 0x060000EA RID: 234 RVA: 0x00004848 File Offset: 0x00002A48
		public static void Show(string lg1, object arg0)
		{
			if (Program.debug == "1")
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("[DEBUG]");
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write(lg1, arg0);
				Console.Write("\n");
				Console.ForegroundColor = ConsoleColor.Gray;
			}
		}

		// Token: 0x060000EB RID: 235 RVA: 0x00004898 File Offset: 0x00002A98
		public static void Show(string lg1, object arg0, object arg1)
		{
			if (Program.debug == "1")
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("[DEBUG]");
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write(lg1, arg0, arg1);
				Console.Write("\n");
				Console.ForegroundColor = ConsoleColor.Gray;
			}
		}

		// Token: 0x060000EC RID: 236 RVA: 0x000048E8 File Offset: 0x00002AE8
		public static void Show(string lg1, object arg0, object arg1, object arg2)
		{
			if (Program.debug == "1")
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("[DEBUG]");
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write(lg1, arg0, arg1, arg2);
				Console.Write("\n");
				Console.ForegroundColor = ConsoleColor.Gray;
			}
		}

		// Token: 0x060000ED RID: 237 RVA: 0x00004938 File Offset: 0x00002B38
		public static void Show(string lg1, object arg0, object arg1, object arg2, object arg3)
		{
			if (Program.debug == "1")
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("[DEBUG]");
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write(lg1, new object[] { arg0, arg1, arg2, arg3 });
				Console.Write("\n");
				Console.ForegroundColor = ConsoleColor.Gray;
			}
		}

		// Token: 0x060000EE RID: 238 RVA: 0x000049A0 File Offset: 0x00002BA0
		public static void Show(string lg1, object arg0, object arg1, object arg2, object arg3, object arg4)
		{
			if (Program.debug == "1")
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("[DEBUG]");
				Console.ForegroundColor = ConsoleColor.White;
				Console.Write(lg1, new object[] { arg0, arg1, arg2, arg3, arg4 });
				Console.Write("\n");
				Console.ForegroundColor = ConsoleColor.Gray;
			}
		}
	}
}
