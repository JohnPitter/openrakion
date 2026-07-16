using RakionServer.Common;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void WriteRoomPlayerRecord(PacketWriter writer, bool usesTunneling)
        {
            writer.WriteCString(CharName)
                .WriteCString(BuddyName)
                .WriteByte(usesTunneling ? 1 : 0)
                .WriteInt32(GroupId);
            WriteRoomEndpoint(writer)
                .WriteByte(CharClass)
                .WriteByte(CharLevel)
                .WriteByte(0);

            for (int slot = 0; slot < _potionSlot.Length; slot++)
                writer.WriteWord(_potionSlot[slot]);
            for (int slot = 0; slot < _potionLevel.Length; slot++)
                writer.WriteByte(_potionLevel[slot]);
        }

        private PacketWriter WriteRoomEndpoint(PacketWriter writer)
        {
            var observed = UdpObservedEndpoint;
            var advertised = UdpAdvertisedEndpoint ?? observed;
            NetworkEndpointCodec.WritePort(writer, observed?.Port ?? 0);
            writer.WriteBytes(observed?.Address.MapToIPv4().GetAddressBytes() ?? new byte[4]);
            NetworkEndpointCodec.WritePort(writer, advertised?.Port ?? 0);
            return writer;
        }
    }
}
