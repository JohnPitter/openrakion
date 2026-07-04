using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// AVATAR do bot como ENTIDADE NPC de colisão real (0x307 CreateNpc + 0x30b move), o mecanismo PRÓPRIO
    /// da engine p/ os monstros — FORA do muro do type-7 do jogador. A entidade criada é uma <c>CEntity</c>
    /// de mundo com caixa de colisão (acertável → faísca nativa), modelo+física pela classe NPC humanoide
    /// (<see cref="BotMovement.NpcClassBot"/> = um CEnemyBase do Rakion: anda/colide sozinho, ≠ Player-fantasma).
    /// RE byte-a-byte em engine.dll (handler 0x307 @0x3610d80a, 0x30b @0x3610dd6c). Substitui o boneco-fantasma.
    /// </summary>
    public sealed partial class BotManager
    {
        /// <summary>Spawn do bot como NPC: define a chave (owner=seat do bot, sub=0 — 1 NPC por owner evita
        /// colisão de tabela), garante o socket e emite o 0x8307 CreateNpc (reliable) UMA vez. O TIME de combate
        /// da creature é herdado do dono (owner=seat → facção inimiga do humano) — ver <see cref="EncodeCreateNpcBody"/>.</summary>
        private void SpawnBotAsNpc(Domain.Field f, PlayerRec rec, BotPlayer bot)
        {
            if (!bot.NpcKeyAssigned)   // chave atribuída UMA vez: create e move SEMPRE casam (não muda no re-spawn)
            {
                bot.NpcOwner = (byte)rec.Slot;                   // owner = seat do bot (como o dono real da Cell):
                                                                 // a creature herda o TIME do dono → facção inimiga do humano.
                                                                 // seat (0..11) fica em bounds (owner*9 ≤ 99 < tabela ~182).
                bot.NpcSub = 0;                                   // 1 NPC por owner -> sem risco de colisão de sub
                bot.NpcKeyAssigned = true;
            }
            EnsureBotSocket(bot);
            EmitBotNpcDatagram(bot, BotMovement.BuildCreateNpcDatagram(bot, NpcClassOf(bot), bot.UdpSeq++, (byte)rec.Slot));
            Log.Ok("bot", "field {0}: '{1}' NPC spawn 0x307 classe 0x{2:X4} (owner {3} sub {4}, rec.Slot {5})",
                f.Id, bot.Name, NpcClassOf(bot), bot.NpcOwner, bot.NpcSub, rec.Slot);
        }

        /// <summary>Class id do avatar do bot: o override do <c>/addbot &lt;classe&gt;</c> se houver, senão o default.</summary>
        private static ushort NpcClassOf(BotPlayer bot) => bot.NpcClassId != 0 ? bot.NpcClassId : BotMovement.NpcClassBot;

        /// <summary>Garante um socket UDP dedicado p/ o bot enviar os datagramas NPC ao servidor (que os
        /// relaya ao host). No modo NPC NÃO há handshake de peer — o gate era do 0x30a do jogador; o NPC tem
        /// chave própria na tabela do host. Só o socket de envio (porta única, como o peer).</summary>
        private void EnsureBotSocket(BotPlayer bot)
        {
            if (bot.UdpSocket != null) return;
            int port = Interlocked.Increment(ref _botPortSeq);
            var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            sock.Bind(new IPEndPoint(IPAddress.Loopback, port));
            bot.UdpSocket = sock;
            bot.BotEndpoint = new IPEndPoint(IPAddress.Loopback, port);
            Log.Ok("bot", "socket UDP NPC porta {0} (bot '{1}')", port, bot.Name);
        }

        /// <summary>Move o NPC via 0x30b a cada tick. O create (0x307) é emitido UMA vez no spawn (SpawnBotAsNpc):
        /// re-enviar um create de entidade que JÁ existe gera "unknown error" no host e a RESETA/destrói — foi o
        /// que quebrou o render (o Golem nascia e sumia). Sem keepalive; no loopback a perda é ~0. O respawn re-cria.
        /// <paramref name="now"/> = Environment.TickCount64.</summary>
        private void EmitBotNpcMove(PlayerRec rec, BotPlayer bot, long now)
        {
            byte[] mv = BotMovement.BuildNpcMoveDatagram(bot, bot.UdpSeq++);
            EmitBotNpcDatagram(bot, mv);
            if (bot.UdpSeq % 20 < 2)   // sonda throttled: chave + posição que o 0x30b carrega (diagnóstico de translado)
                Log.Ok("bot", "NPC move owner {0} sub {1} (rec.Slot {2}) -> pos=({3:F1},{4:F1},{5:F1}) yaw {6:F0}",
                    bot.NpcOwner, bot.NpcSub, rec.Slot, bot.X, bot.Y, bot.Z, bot.Yaw);
        }

        /// <summary>Envia um datagrama NPC pelo socket do bot ao socket de gameplay do servidor (loopback),
        /// que o relaya ao host — a MESMA via do 0x30a (UdpGameplay relaya 0x307/0x30b).</summary>
        private void EmitBotNpcDatagram(BotPlayer bot, byte[] pkt)
        {
            var sock = bot.UdpSocket;
            if (sock == null) return;
            var serverEp = new IPEndPoint(IPAddress.Loopback, _gameplayPort());
            try { sock.SendTo(pkt, serverEp); } catch { }
        }
    }
}
