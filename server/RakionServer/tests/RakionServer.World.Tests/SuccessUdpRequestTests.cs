using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    public sealed class SuccessUdpRequestTests
    {
        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)1)]
        [InlineData((byte)255)]
        public void ParsesBuilderResultAndTransportTail(byte result)
        {
            byte[] payload = new byte[8];
            payload[0] = result;
            payload[1] = 0xdf;
            payload[2] = 0x19;
            payload[4] = 0x68;
            payload[5] = 0xdd;
            payload[6] = 0x19;

            Assert.True(SuccessUdpRequest.TryParse(payload, out SuccessUdpRequest request));
            Assert.Equal(result, request.Result);
        }

        [Fact]
        public void AcceptsExactLogicalBody() =>
            Assert.True(SuccessUdpRequest.TryParse(new byte[] { 1 }, out _));

        [Fact]
        public void RejectsMissingResult() =>
            Assert.False(SuccessUdpRequest.TryParse(new byte[0], out _));
    }
}
