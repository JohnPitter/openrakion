using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Subsistema de BOTS reconstruído sobre o motor de partida do golden. Regra de negócio pura de
    /// roster/lifecycle: só o HOST adiciona, só ANTES da partida, no time OPOSTO ao host; o bot é um
    /// peer sintético (<see cref="BotPlayer"/>) que entra no roster (0x38) como um jogador, é movido
    /// pela IA (<see cref="BotSteering"/>) e tem o movimento sintetizado no fio (0x030A, fase de rede).
    ///
    /// Teto do RE (respeitado): entrega oponente FUNCIONAL (roster, movimento, dano/morte server-side)
    /// e publica a confirmação necessária para a camada visual da DLL. Nenhum
    /// pacote do bot fala direto com o cliente — só via o socket do <see cref="UdpGameplay"/>.
    /// Ciclo efêmero: bots somem no fim do match ou quando o último humano sai.
    /// </summary>
    public sealed partial class BotManager
    {
        private static readonly string[] BotNames =
            { "Rok", "Karion", "Ares", "Vyl", "Drak", "Nyx", "Zair", "Brutus", "Kael", "Thorn" };

        private int _nameSeq;
        private readonly object _lifecycleFileLock = new();
        private readonly string _lifecyclePath;
        private const string DefaultLifecyclePath = @"C:\temp\bot_lifecycle.txt";

        public BotManager() : this(DefaultLifecyclePath)
        {
        }

        internal BotManager(string lifecyclePath)
        {
            _lifecyclePath = lifecyclePath;
        }

        public readonly record struct AddBotResult(bool Ok, string Message, int Seat, BotPlayer? Bot);

        public AddBotResult ReserveBot(
            Field field, ClientSession host, BotDifficulty difficulty)
        {
            lock (field.SyncRoot)
            {
                PlayerRec? hostRec = field.FindRec(host);
                if (hostRec == null)
                    return new AddBotResult(false, "voce nao esta na sala", -1, null);
                if (field.MasterSlot != hostRec.Slot)
                    return new AddBotResult(false, "so o dono da sala adiciona bot", -1, null);
                if (field.Phase == MatchPhase.Playing || field.State == 2)
                    return new AddBotResult(false, "partida em andamento", -1, null);
                if (field.Mode == 0)
                    return new AddBotResult(false, "bots so em sala competitiva (PvP)", -1, null);

                byte team = (byte)(hostRec.Team ^ 1);
                byte level = host.CharLevel == 0 ? (byte)1 : host.CharLevel;
                byte charClass = host.CharClass == 0 ? (byte)1 : host.CharClass;
                var bot = CreateBot(team, level, charClass, difficulty);
                int seat = field.AddBot(bot, team);
                if (seat < 0)
                    return new AddBotResult(false, "time cheio", -1, null);
                field.Slots[seat].State = 0;
                return new AddBotResult(true, "bot reservado", seat, bot);
            }
        }

        public bool ConfirmReservation(
            Field field, ClientSession host, AddBotResult reservation)
        {
            if (!reservation.Ok || reservation.Bot == null)
                return false;
            lock (field.SyncRoot)
            {
                PlayerRec? record = field.RecAt((byte)reservation.Seat);
                if (record?.Bot != reservation.Bot ||
                    field.FindRec(host) == null ||
                    field.Phase == MatchPhase.Playing ||
                    field.State == 2)
                    return false;
                reservation.Bot.AttachEngine();
                record.State = 2;
                record.WeaponState = 1;
                record.Dead = false;
                Log.Ok("bot", "[{0}] bot nativo '{1}' -> field {2} seat {3}",
                    host.Slot, reservation.Bot.Name, field.Id, reservation.Seat);
                field.BroadcastLobby(RoomRosterFrames.PlayerJoined(record));
                return true;
            }
        }

        public void RollbackReservation(Field field, AddBotResult reservation)
        {
            if (!reservation.Ok || reservation.Bot == null)
                return;
            lock (field.SyncRoot)
            {
                PlayerRec? record = field.RecAt((byte)reservation.Seat);
                if (record?.Bot == reservation.Bot)
                    field.RemoveBot((byte)reservation.Seat);
            }
        }

        /// <summary>
        /// Adiciona um bot ao field do <paramref name="host"/> no time oposto. Só o dono da sala, e só
        /// antes do início da partida. Sincroniza o roster do cliente humano com um member-join (0x38).
        /// </summary>
        public AddBotResult AddBotToField(Field field, ClientSession host, BotDifficulty difficulty)
        {
            AddBotResult reservation = ReserveBot(field, host, difficulty);
            if (!reservation.Ok)
                return reservation;
            if (ConfirmReservation(field, host, reservation))
                return reservation with { Message = "bot adicionado" };
            RollbackReservation(field, reservation);
            return new AddBotResult(false, "reserva do bot expirou", -1, null);
        }

        private BotPlayer CreateBot(
            byte team, byte level, byte charClass, BotDifficulty difficulty)
        {
            var bot = new BotPlayer
            {
                Name = NextName(),
                Level = level,
                CharClass = charClass,
                Team = team,
                Difficulty = difficulty,
                Profile = BotProfile.For(difficulty),
            };
            bot.InitHealth(level);
            return bot;
        }

        /// <summary>Remove um bot pelo seat e avisa o roster (0x3a member-leave sintético).</summary>
        public bool RemoveBot(Field field, byte seat)
        {
            lock (field.SyncRoot)
            {
                if (!field.RemoveBot(seat)) return false;
                field.BroadcastLobby(BuildLeave(seat));
                PublishBotLifecycles(field);
                return true;
            }
        }

        /// <summary>Publica o spawn dos peers sintéticos quando o match entra no field.</summary>
        public void PublishMatchSpawns(Field field)
        {
            lock (field.SyncRoot)
            {
                foreach (PlayerRec record in field.BotSlots)
                {
                    record.State = 4;
                    record.Dead = false;
                    field.BroadcastField(0x45,
                        FieldLifecycleFrames.Spawn((byte)record.Slot));
                }
            }
        }

        /// <summary>Sincroniza os bots com o cliente que acabou de concluir o próprio spawn.</summary>
        public void SendMatchSpawnsTo(ClientSession session, Field field)
        {
            lock (field.SyncRoot)
            {
                foreach (PlayerRec record in field.BotSlots)
                {
                    record.State = 4;
                    record.Dead = false;
                    session.SendMessage(0x45,
                        FieldLifecycleFrames.Spawn((byte)record.Slot));
                }
            }
        }

        /// <summary>
        /// Replica o snapshot inicial de entrada do humano para criar os peers sintéticos no engine.
        /// O seat fica no envelope 0x4B; o blob contém apenas o estado inicial do avatar.
        /// </summary>
        public void SendInitialStateTo(ClientSession session, Field field, byte[] stateBlob)
        {
            lock (field.SyncRoot)
            {
                foreach (PlayerRec record in field.BotSlots)
                {
                    using var writer = new PacketWriter();
                    writer.WriteByte((byte)record.Slot)
                        .WriteWord((ushort)stateBlob.Length)
                        .WriteBytes(stateBlob);
                    session.SendMessage(0x4b, writer.ToArray());
                }
            }
        }

        /// <summary>Limpa todos os bots do field (fim de match / último humano saiu).</summary>
        public int RemoveAllBots(Field field)
        {
            lock (field.SyncRoot)
            {
                int removed = 0;
                foreach (PlayerRec rec in field.Slots)
                {
                    if (rec.Bot == null) continue;
                    byte seat = (byte)rec.Slot;
                    if (field.RemoveBot(seat)) { field.BroadcastLobby(BuildLeave(seat)); removed++; }
                }
                if (removed > 0) Log.Info("bot", "field {0}: {1} bot(s) removido(s)", field.Id, removed);
                if (removed > 0) PublishBotLifecycles(field);
                return removed;
            }
        }

        public void PublishBotLifecycles(Field field)
        {
            var content = new StringBuilder();
            var clientPorts = new HashSet<int>();
            lock (field.SyncRoot)
            {
                foreach (PlayerRec record in field.BotSlots)
                {
                    BotPlayer bot = record.Bot!;
                    content.Append(record.Slot).Append(' ')
                        .Append(field.Id).Append(' ')
                        .Append(bot.LifecycleSequence).Append(' ')
                        .Append(bot.Alive ? 0 : 1).Append(' ')
                        .Append(bot.DamageSequence).Append(' ')
                        .Append(bot.LastAttackerSeat).Append(' ')
                        .Append(bot.LastAttackerHitSequence).Append(' ')
                        .Append(bot.IsMoving ? 1 : 0).AppendLine();
                }
                foreach (PlayerRec record in field.Slots)
                    if (record.Session?.UdpEndpoint is { } endpoint)
                        clientPorts.Add(endpoint.Port);
            }

            lock (_lifecycleFileLock)
            {
                foreach (int port in clientPorts)
                    PublishLifecycleSnapshot(ClientLifecyclePath(_lifecyclePath, port), content.ToString());
            }
        }

        internal static string ClientLifecyclePath(string basePath, int port)
        {
            string? directory = Path.GetDirectoryName(basePath);
            string name = Path.GetFileNameWithoutExtension(basePath);
            string extension = Path.GetExtension(basePath);
            return Path.Combine(directory ?? "", $"{name}_{port}{extension}");
        }

        private static void PublishLifecycleSnapshot(string path, string content)
        {
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    string? directory = Path.GetDirectoryName(path);
                    if (directory != null) Directory.CreateDirectory(directory);
                    string temporary = path + ".tmp";
                    File.WriteAllText(temporary, content, Encoding.ASCII);
                    File.Move(temporary, path, true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    if (attempt == 3)
                        Log.Warn("bot", "falha ao publicar lifecycle '{0}': {1}", path, ex.Message);
                    else
                        Thread.Sleep(3);
                }
            }
        }

        private static byte[] BuildLeave(byte seat)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3a).WriteByte(seat);   // member-leave: libera o slot no cliente
            return w.ToArray();
        }

        private string NextName()
        {
            int sequence = Interlocked.Increment(ref _nameSeq) - 1;
            string baseName = BotNames[sequence % BotNames.Length];
            int cycle = sequence / BotNames.Length;
            return cycle == 0 ? baseName : baseName + (cycle + 1);
        }
    }
}
