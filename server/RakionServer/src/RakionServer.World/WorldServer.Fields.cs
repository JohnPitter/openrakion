using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Registro e ciclo de vida de FIELDS/ROOMS do world (partial de <see cref="WorldServer"/>): a coleção
    /// de partidas/salas (this+0xe4/0xdc), criação/lookup, entrada da sessão no field e saída (W.O. + descarte
    /// de bots). Estado em memória (não persiste no DB). Concern separado do bootstrap e do ciclo de sessão.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>Fields/partidas (this+0xe4) e rooms/chat (this+0xdc).</summary>
        public readonly List<Domain.Field> Fields = new();
        public readonly List<Domain.Room> Rooms = new() { new Domain.Room(0) };

        public Domain.Field? GetField(int id) => id < 0 ? null : Fields.Find(f => f.Id == id);
        public Domain.Room? GetRoom(int id) => id < 0 ? null : Rooms.Find(r => r.Id == id);

        private int _nextFieldId;

        /// <summary>
        /// Aloca um field/sala (espelha a varredura de this+0xe4 por slot livre no
        /// RoomCreate FUN_00423580). Cria a entrada no dominio e devolve o Field.
        /// </summary>
        public Domain.Field CreateField(string name, byte mapId, byte mode, ushort capacity, ClientSession master)
        {
            lock (Fields)
            {
                var f = new Domain.Field(_nextFieldId++)
                {
                    Name = name,
                    Mode = mode,
                    MapId = mapId,
                    // capacity e ushort (ate ~0x4ba=1210 nas salas ranqueadas); o cast cru truncava >255.
                    // Clamp: 0 -> default 8; acima de 255 satura em byte.MaxValue (sem wrap).
                    MaxPlayers = capacity == 0 ? (byte)8 : (byte)System.Math.Min((int)capacity, byte.MaxValue),
                    Master = master,
                    MasterSlot = master.Slot,
                    State = 1, // ocupado (field+8 != 0)
                };
                f.Add(master);
                Fields.Add(f);
                // FUN_0040b7b0: vincula o master ao field (estado "em field")
                master.FieldId = f.Id;
                master.InField = true;
                master.FieldSecondary = true;
                master.Status = Domain.UserStatus.InField;
                Log.Info("field", "[{0}] criou field {1} '{2}' (map={3} mode={4} cap={5})",
                    master.Slot, f.Id, name, mapId, mode, f.MaxPlayers);
                return f;
            }
        }

        /// <summary>
        /// Garante que a sessao tem um Field ativo com seat alocado (ponte da cadeia de entrada
        /// 0x3b/0x4b — proven-working — para o modelo de partida real). Solo: cria um field
        /// dedicado; multi: reusa o field ja associado (FieldId). Marca State=2 (em jogo) p/ o
        /// motor rodar e seta os campos de seat do user (FUN_0040b7b0).
        /// </summary>
        /// <summary>Se true, um cliente que entra no stage se JUNTA ao field de OUTRO humano já em jogo (em vez de
        /// criar um field SOLO). É o multiplayer "por atalho" — sem game list/salas: o 1º entra no stage, o 2º cai
        /// no MESMO field. Necessário p/ 2 clientes juntos (captura do blob da Cell + PvP real). Ambos devem estar
        /// no MESMO mapa (criar sala com o mesmo mapa) senão dessincroniza.</summary>
        public static bool AutoJoinSameField = false;   // desligado: o certo é game list + join (não atalho)

        /// <summary>Acha um field EM JOGO (State==2) com outro humano jogando p/ o <paramref name="s"/> se juntar.</summary>
        private Domain.Field? FindJoinableField(ClientSession s)
        {
            if (!AutoJoinSameField) return null;
            lock (Fields)
            {
                foreach (var f in Fields)
                {
                    if (f.Id == s.FieldId || f.State != 2) continue;
                    foreach (var r in f.Slots)
                        if (r.Session != null && r.Session != s && !r.IsBot && r.Playing)
                        {
                            Log.Ok("field", "[{0}] JOIN automático no field {1} (host seat {2}) — multiplayer sem salas",
                                s.Slot, f.Id, r.Slot);
                            return f;
                        }
                }
            }
            return null;
        }

        public Domain.Field EnsureFieldForSession(ClientSession s)
        {
            // JOIN tem PRIORIDADE sobre o field-da-sala próprio: o cliente já teve FieldId setado ao criar a sala
            // (GetOrCreateRoomField), então GetField pegaria o dele e nunca juntaria. FindJoinableField primeiro =
            // 2º cliente cai no field do 1º (multiplayer sem game list).
            Domain.Field f = FindJoinableField(s)
                ?? GetField(s.FieldId)
                ?? CreateField(s.CharName.Length > 0 ? s.CharName : $"field{s.Slot}", mapId: 0, mode: 0, capacity: 8, master: s);
            if (f.Id != s.FieldId) s.FieldId = f.Id;   // re-vincula ao field juntado
            f.State = 2; // field+8 = 2 (em jogo)
            int seat = f.AssignSeat(s);
            if (seat >= 0) { s.FieldSeat = (byte)seat; s.FieldObjectIndex = (ushort)seat; }
            if (f.MasterSlot < 0 || f.MasterSlot >= 0x14) f.MasterSlot = seat;
            return f;
        }

        /// <summary>
        /// Field da SALA pré-partida do host. O 0x3b só guarda os params (PendingRoom*); o Field nasce sob
        /// demanda — e o /addbot precisa dele AINDA na sala (Status=2), antes do stage. Reusa o Field já
        /// vinculado (FieldId) ou cria um dos params do host, mas SEM tocar no Status de lobby (a sala
        /// pré-partida é Status=2; o stage promove a 3 depois reusando este mesmo Field via
        /// <see cref="EnsureFieldForSession"/>). Devolve null se o host não criou sala (sem PendingRoom*),
        /// p/ não materializar field-fantasma no lobby.
        /// </summary>
        public Domain.Field? GetOrCreateRoomField(ClientSession host)
        {
            lock (Fields)
            {
                var existing = Fields.Find(f => f.Id == host.FieldId);
                if (existing != null) return existing;
                if (host.PendingRoomMap == 0 && string.IsNullOrEmpty(host.PendingRoomName)) return null;
                var f = new Domain.Field(_nextFieldId++)
                {
                    Name = host.PendingRoomName,
                    Mode = host.PendingRoomMode,
                    MapId = host.PendingRoomMap,
                    // Capacidade da SALA (captura: max=12 na Gravity — o 0x3b não a carrega; o original a deriva
                    // do mapa). 12 = 6v6, os slots além disso vão TRANCADOS (05) no roster do 0x37.
                    MaxPlayers = 12,
                    MinLevel = host.PendingRoomMinLevel,
                    MaxLevel = host.PendingRoomMaxLevel,
                    FragLimit = host.PendingRoomFrag,
                    Master = host,
                    State = 1,                  // ocupado (pré-partida) — não 2 (em jogo)
                    // CRÍTICO: o tick liquida `State==1 && !Settled` (= partida ACABADA -> DiscardBots). A sala
                    // pré-partida é State=1 igual a um match encerrado e seria liquidada na hora (bots somem em
                    // ~50ms). Marcando Settled=true o tick a ignora; ResetMatch (engage 0x43) zera p/ false ao
                    // começar a partida, restaurando a liquidação pós-match normal.
                    Settled = true,
                };
                Fields.Add(f);
                f.Add(host);                                  // lista Players (count/broadcast)
                int seat = f.AssignSeat(host);                // ASSENTO real (Slots): FindRec/Team/MasterSlot dependem disto
                if (seat >= 0) { host.FieldSeat = (byte)seat; host.FieldObjectIndex = (ushort)seat; f.MasterSlot = seat; }
                host.FieldId = f.Id;           // vincula; NÃO toca em Status/InField (sala pré-partida = Status=2)
                Log.Info("field", "[{0}] field da sala criado sob demanda (id={1} map={2} mode={3}) p/ roster/bot",
                    host.Slot, f.Id, host.PendingRoomMap, host.PendingRoomMode);
                return f;
            }
        }

        /// <summary>Remove o usuario do field; se ficar vazio, libera o field.</summary>
        public void LeaveField(ClientSession s)
        {
            var f = GetField(s.FieldId);
            if (f == null) return;
            bool wasInPvpMatch = f.State == 2 && f.Mode != 0;   // saiu no meio de uma partida PvP
            f.Remove(s);
            s.FieldId = -1;
            // ABANDONO no meio do stage: o adversário saiu e deixou um time VAZIO (mas ainda há humano) -> o time que
            // ficou VENCE o game (regra: sair = derrota). Fim de match + volta ao lobby (0x44), como no original.
            if (wasInPvpMatch && f.Count > 0 && !f.ObjectiveDecided)
            {
                int occ0 = f.CountOccupiedTeam(0), occ1 = f.CountOccupiedTeam(1);
                if (occ0 == 0 || occ1 == 0)
                {
                    byte winner = occ0 == 0 ? (byte)1 : (byte)0;
                    f.EndRoundObjective(winner);                 // credita o round + broadcasta o 0x4a de vitória
                    f.EndMatch(2);
                    f.BroadcastLobby(f.BuildMatchEnd(2));         // 0x44 -> devolve o vencedor ao lobby
                    Log.Ok("field", "field {0}: adversário abandonou o stage -> time {1} VENCE o game (volta ao lobby)", f.Id, winner);
                }
            }
            // O último HUMANO saiu (Count = só humanos; bots não entram em Players): descarta os bots
            // e libera o field. Bots nunca mantêm uma sala viva — sem humano, a sala morre.
            if (f.Count == 0)
            {
                int bots = f.BotCount;
                Bots.DiscardBots(f);
                lock (Fields) Fields.Remove(f);
                Log.Info("field", "field {0} '{1}' liberado (sem humanos; {2} bot(s) descartado(s))",
                    f.Id, f.Name, bots);
            }
        }
    }
}
