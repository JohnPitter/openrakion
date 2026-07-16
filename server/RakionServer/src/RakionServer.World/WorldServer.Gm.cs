using System;
using RakionServer.World.Domain;

namespace RakionServer.World
{
    public sealed partial class WorldServer
    {
        public GmFieldEntrySnapshot QueryGmFieldEntry(ushort fieldId)
        {
            int upperBound = Math.Clamp(_cfg.MaxField, 1, ushort.MaxValue + 1);
            if (fieldId >= upperBound)
                return new GmFieldEntrySnapshot(GmFieldEntryStatus.OutOfRange, fieldId);

            Field? field;
            lock (Fields) field = Fields.Find(candidate => candidate.Id == fieldId);
            if (field == null)
                return new GmFieldEntrySnapshot(GmFieldEntryStatus.Free, fieldId);

            lock (field.SyncRoot)
            {
                if (field.State == 0)
                    return new GmFieldEntrySnapshot(GmFieldEntryStatus.Free, fieldId);
                return new GmFieldEntrySnapshot(
                    GmFieldEntryStatus.Success,
                    fieldId,
                    field.Name,
                    field.CreatorCharacterName);
            }
        }
    }
}
