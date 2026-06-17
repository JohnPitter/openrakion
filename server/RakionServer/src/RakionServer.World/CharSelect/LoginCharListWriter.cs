using System;
using System.Buffers.Binary;
using System.Text;

namespace RakionServer.World.CharSelect
{
    /// <summary>
    /// Serializa o frame <b>0x0C</b> (lista de chars do char-select) a partir do domínio (<see cref="CharList"/>).
    /// Formato decodificado via captura-diff (memória 0c-login-frame-format): header fixo de 65B, N char-records
    /// de NOME VARIÁVEL — os campos pós-nome começam em <c>fieldsStart = record + 5 + len(nome) + 1</c> — e um
    /// trailer iniciado por <c>[03 00]</c> (o cliente lê records até <c>marker[1] == 0</c>).
    /// Síntese de raiz: substitui o replay do <c>oracle_0c.bin</c>.
    /// </summary>
    public static class LoginCharListWriter
    {
        private const int HeaderSize = 65;
        private const int NamePrefix = 5;     // marker(2) + pad(3) antes do nome
        private const int FieldsSize = 359;   // bloco de campos pós-nome (+ \0 do nome)
        private const int TrailerSize = 20;   // [03 00] + padding (ignorado pelo cliente após marker[1]=0)
        private const int AccountNameMax = 2; // @41 EXATAMENTE 2 chars: o cliente lê o resto do frame relativo a este campo (calibração)

        // offsets dos campos relativos a fieldsStart
        private const int Win = 0, Lose = 4, Draw = 8, Class = 22, Level = 23, Exp = 24, LevelPoint = 28;
        private const int Stats = 30, Equip = 50, Quickslot = 76, Enhance = 88, Ranks = 260;

        public static byte[] Build(CharList list)
        {
            int total = HeaderSize + TrailerSize;
            foreach (var c in list.Chars) total += RecordSize(c.Name);
            var buf = new byte[total];

            WriteHeader(buf, list);
            int off = HeaderSize;
            foreach (var c in list.Chars) off = WriteRecord(buf, off, c);
            buf[off] = 0x03;   // trailer: [03 00] — marker[1]=0 sinaliza fim da lista
            return buf;
        }

        private static int RecordSize(string name) => NamePrefix + NameLen(name) + 1 + FieldsSize;

        private static int NameLen(string name) => Encoding.ASCII.GetByteCount(name ?? "");

        private static void WriteHeader(byte[] buf, CharList list)
        {
            buf[0] = 0x0c;
            buf[3] = 0x01;                                                  // result = sucesso
            // @7-8 = userid (u16); @9-10 = valor de sessão (1-char, do original). Zerar isto trava o char-select.
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(7), (ushort)list.UserId);
            buf[9] = 0x2e; buf[10] = 0x04;
            if (list.SessionHandle is { Length: >= 4 })
                Array.Copy(list.SessionHandle, 0, buf, 13, 4);             // @13 session handle
            WriteString(buf, 41, list.AccountName, AccountNameMax);        // @41 account name (campo fixo)
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(48), list.PowerLevelPoint);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(56), list.Gold);
            buf[64] = list.SlotCount;                                     // @64 = nº de slots da conta (cria slots criáveis)
        }

        private static int WriteRecord(byte[] buf, int rec, CharSummary c)
        {
            int nameLen = WriteString(buf, rec + NamePrefix, c.Name, int.MaxValue);
            buf[rec] = (byte)(2 * c.Slot + 1);                            // marker[0]
            buf[rec + 1] = (byte)(c.Slot + 1);                            // marker[1] (>=1 => char; 0 => trailer)

            int f = rec + NamePrefix + nameLen + 1;                       // fieldsStart (após o nome\0)
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(f + Win), c.Win);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(f + Lose), c.Lose);
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(f + Draw), c.Draw);
            buf[f + Class] = c.Class;
            buf[f + Level] = c.Level;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(f + Exp), c.Exp);
            BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(f + LevelPoint), c.LevelPoint);
            WriteU16(buf, f + Stats, c.Stats, 10);
            WriteU16(buf, f + Equip, c.Equip, 7);
            WriteU16(buf, f + Quickslot, c.Quickslot, 6);
            WriteU8(buf, f + Enhance, c.Enhance, 7);
            WriteU8(buf, f + Ranks, c.StageRanks, Math.Min(c.StageRanks.Length, FieldsSize - Ranks));
            return rec + RecordSize(c.Name);
        }

        private static void WriteU16(byte[] buf, int at, ushort[] src, int count)
        {
            for (int i = 0; i < count && i < src.Length; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(at + i * 2), src[i]);
        }

        private static void WriteU8(byte[] buf, int at, byte[] src, int count)
        {
            for (int i = 0; i < count && i < src.Length; i++)
                buf[at + i] = src[i];
        }

        /// <summary>Escreve um nome ASCII null-terminado (o \0 e o padding já são zero no buffer). Retorna os bytes do nome.</summary>
        private static int WriteString(byte[] buf, int at, string name, int maxChars)
        {
            byte[] b = Encoding.ASCII.GetBytes(name ?? "");
            int n = Math.Min(b.Length, maxChars);
            Array.Copy(b, 0, buf, at, n);
            return n;
        }
    }
}
