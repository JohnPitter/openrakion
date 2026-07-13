using System;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Domínio do messenger (add buddy, 0x19): resolve o nick → conta dona e persiste a amizade RECÍPROCA.
    /// Serviço AUTO-CONTIDO — depende só do <see cref="WorldDatabase"/> (persistência isolada, sem estado global).
    /// O AddBuddy do cliente é MUDO (não emite SVC_ADD_BUDDY), então a amizade é persistida aqui, não no Buddy.
    /// </summary>
    public sealed class BuddyService
    {
        private readonly WorldDatabase _db;

        public BuddyService(WorldDatabase db) => _db = db;

        /// <summary>Resolve o nick → conta dona e, se existe e não é o próprio jogador, persiste a amizade
        /// recíproca (buddylist nos 2 sentidos). Devolve (status, accountId do dono) p/ o handler serializar a
        /// resposta — status 0=ok, 2=char não existe (lang 598).</summary>
        public async Task<(byte Status, string Account)> ResolveAndAddBuddyAsync(ClientSession s, string targetNick)
        {
            string? targetAccount = await _db.GetCharOwnerByNickAsync(targetNick);
            if (targetAccount == null) return ((byte)2, "");                 // char não existe (lang 598)
            if (!string.Equals(targetAccount, s.UserId, StringComparison.OrdinalIgnoreCase))
            {
                await _db.AddBuddyReciprocalAsync(s.UserId, targetAccount);
                Log.Ok("buddy", "[{0}] amizade '{1}' <-> '{2}' (nick '{3}') persistida", s.Slot, s.UserId, targetAccount, targetNick);
            }
            return ((byte)0, targetAccount);
        }
    }
}
