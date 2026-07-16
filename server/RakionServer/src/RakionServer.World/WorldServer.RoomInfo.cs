using System;
using RakionServer.World.Domain;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public bool TryGetRoomInfo(int fieldId, out FieldRoomInfoSnapshot snapshot)
        {
            int upperBound = Math.Clamp(_cfg.MaxField, 1, ushort.MaxValue + 1);
            if (fieldId < 0 || fieldId >= upperBound)
            {
                snapshot = null!;
                return false;
            }

            Field? field = GetField(fieldId);
            if (field == null)
            {
                snapshot = FieldRoomInfoSnapshot.Empty((ushort)fieldId);
                return true;
            }

            lock (field.SyncRoot)
                snapshot = FieldRoomInfoSnapshot.From(field);
            return true;
        }
    }
}
