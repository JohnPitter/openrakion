using RakionServer.Common;

namespace RakionServer.World.Network
{
    public static partial class WorldHandlers
    {
        private static void Op_VerifyTutorialStage(HandlerContext ctx)
        {
            var u = ctx.User;
            // Guard: SubStatus (*(user+0x146c)) deve ser '4' (0x34). Senao => disconnect 0xb9.
            if (u.SubStatus != 0x34)
            {
                u.Disconnect(0xb9);
                return;
            }

            // FUN_0040abe0(user, &local_14, &local_c): obtem um par de valores do estado do usuario.
            // local_14[0] e comparado contra a tabela global DAT_00456034..0x456044 (4 ints).
            // TODO FUN_0040abe0: extrair (id, aux) do estado do slot.
            int stageId = u.FieldId; // efeito minimo: usa o id de field/stage atual como local_14[0]

            // Tabela permitida de stages (DAT_00456034, 4 entradas u32). Valores reais do .data nao
            // estao neste corpo; modelado como conjunto cujo conteudo deve vir da config.
            // TODO DAT_00456034: carregar os 4 ids validos.
            int[] allowedStages = ctx.World.Config?.AllowedTutorialStages ?? System.Array.Empty<int>();

            bool ok = false;
            foreach (var id in allowedStages)
            {
                if (stageId == id) { ok = true; break; }
            }

            if (!ok)
            {
                // Nenhum match na tabela => disconnect 0xba.
                u.Disconnect(0xba);
                return;
            }
            // Match encontrado: sucesso, nenhum pacote de resposta (goto LAB_00428410).
        }

        private static void Op_VerifyClientHash(HandlerContext ctx)
        {
            var u = ctx.User;
            // *(user+0x237c) = modo/estado do slot. Se for '\x04' ou '\x05', pula toda a verificacao.
            byte mode = u.VerifyMode; // = *(user+0x237c)
            if (mode == 0x04 || mode == 0x05)
                return;

            // Caso contrario: precisa estar no field; senao => disconnect 0xbb.
            if (!u.InField)
            {
                u.Disconnect(0xbb);
                // OBS: o exe NAO retorna aqui; continua e ainda compara o hash abaixo.
            }

            // Copia 0x21 bytes (8 dwords + 1 byte terminador) do payload => string recebida.
            if (!ctx.P.CanRead(0x21))
            {
                ctx.World.AntiCheat.OnProtocolViolation(u.Slot, u.UserId, Security.ViolationKind.MalformedFrame, "verify-hash curto");
                u.Disconnect(0xbc);
                return;
            }
            string received = ctx.P.CString(0x21);

            // Escolhe o hash de referencia: se mode == '\x01' usa user+0x14d (MD5_2), senao user+300 (MD5_1).
            // (this+0x14d e this+0x12c sao os dois hashes MD5 salvos em opcode 0x0b.)
            string expected = (mode == 0x01) ? u.Md5Hash2 : u.Md5Hash1;

            // OpenGuard: atestacao de integridade do binario. Modo observacao apenas audita; com
            // EnforceClientHash liga o kick (DISC 0xbc, fiel ao exe). Sem hash de referencia = no-op.
            var dec = ctx.World.AntiCheat.OnClientHash(u.Slot, u.UserId, received, expected, present: received.Length > 0);
            if (dec.Kick)
                u.Disconnect(Protocol.DiscReason.ClientHash);
        }

        private static void Op_ServerInfoDump(HandlerContext ctx)
        {
            // FUN_0041be60: sem guard de estado. Monta um struct "lobby" grande (zerado, 0x1010 bytes)
            // com subtype 0x77 e copia blocos de dados globais do servidor a partir de offsets fixos
            // do objeto WorldServer (this+4, this+8, this+0x24, this+0x4c).
            // Layout da resposta (SendLobby; len enviado = 0x4c = 76):
            //   [u16 subtype=0x77]            (local_1010)
            //   [int  this+0x04]              (local_100e)            -> 4 bytes
            //   [7 x int  this+0x08..0x23]    (local_100a[7])         -> 28 bytes
            //   [10 x int this+0x24..0x4b]    (local_fee[10])         -> 40 bytes
            //   [u16 this+0x4c]               (*(u16)puVar3=*(u16)puVar2) -> 2 bytes
            //   total payload (apos subtype) = 4 + 28 + 40 + 2 = 74; +2 subtype = 76 (0x4c). OK.
            // SendLobby ja escreve o subtype (= primeiro u16 do struct), entao o payload abaixo
            // contem APENAS os campos APOS o subtype.
            var w = ctx.World;

            using var pw = new PacketWriter();
            // this+0x04: int global do servidor.
            // TODO FUN_0041be60: estes campos sao estado global do WorldServer em offsets fixos
            // (this+0x04, +0x08..+0x4c). Nao ha funcao auxiliar; modelo como blocos zerados de mesmo
            // tamanho/tipo, preservando exatamente a estrutura/len (0x4c) da resposta original.
            pw.WriteInt32(0);                 // local_100e  = *(this+0x04)
            for (int i = 0; i < 7; i++)       // local_100a[7] = *(this+0x08 .. +0x23)
                pw.WriteInt32(0);
            for (int i = 0; i < 10; i++)      // local_fee[10] = *(this+0x24 .. +0x4b)
                pw.WriteInt32(0);
            pw.WriteWord(0);                  // *(u16)(this+0x4c)

            ctx.User.SendLobby(BuildLobby(0x77, pw.ToArray()));
        }

        // Helper local para prefixar o subtype no canal lobby, conforme convencao (payload comeca com [u16 subtype]).
        private static byte[] BuildLobby(ushort subtype, byte[] body)
        {
            using var w = new PacketWriter();
            w.WriteWord(subtype);
            w.WriteBytes(body);
            return w.ToArray();
        }

        private static void Op_FieldStateQuery(HandlerContext ctx)
        {
            // FUN_0041bde0: sem guard de estado. Le dois inteiros do objeto de sessao do usuario
            // (user+0x1460 e user+0x14d0) e responde no canal "plano" (SendMessage) com subtype 0x2c.
            // Struct local (FUN_0041b940 => SendMessage, len enviado = 0xc = 12):
            //   local_1004 = *(user+0x1488)   -> sessionId/seq (preenchido pelo SendMessage em C#)
            //   local_1002 = 0x2c             -> subtype
            //   local_1000 = *(user+0x1460)   -> int (estado de field; em ClientSession ~ InField raw)
            //   local_ffc  = *(user+0x14d0)   -> int
            //   total = seq(2)+subtype(2)+4+4 = 12 (0xc). OK.
            // Em C#, SendMessage ja escreve seq+subtype; passamos APENAS os bytes APOS o subtype.
            var u = ctx.User;

            // *(user+0x1460): ponteiro/handle do field corrente (0 = nenhum). Exposto como InField em
            // ClientSession (raw em user+0x1460). Reaproveito FieldId para o valor inteiro disponivel.
            int fieldRaw = u.FieldId;                 // *(user+0x1460) (golden source p/ estado de field)
            int extra = 0;                            // *(user+0x14d0): campo de sessao em offset fixo nao mapeado
            // TODO FUN_0041bde0: user+0x14d0 nao tem getter conhecido em ClientSession; mantido 0 para
            // preservar o tamanho/estrutura (dois int32) da resposta.

            using var w = new PacketWriter();
            w.WriteInt32(fieldRaw);   // local_1000 = *(user+0x1460)
            w.WriteInt32(extra);      // local_ffc  = *(user+0x14d0)
            u.SendMessage(0x2c, w.ToArray());
        }
    }
}
