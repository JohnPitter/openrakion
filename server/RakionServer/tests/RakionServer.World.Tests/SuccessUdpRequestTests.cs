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
        public void ParsesBuilderResultAndCipherPadding(byte result)
        {
            byte[] payload = new byte[8];
            payload[0] = result;

            Assert.True(SuccessUdpRequest.TryParse(payload, out SuccessUdpRequest request));
            Assert.Equal(result, request.Result);
        }

        [Fact]
        public void AcceptsExactLogicalBody() =>
            Assert.True(SuccessUdpRequest.TryParse(new byte[] { 1 }, out _));

        [Theory]
        [InlineData(new byte[0])]
        [InlineData(new byte[] { 0, 1 })]
        public void RejectsMissingResultOrNonZeroPadding(byte[] payload) =>
            Assert.False(SuccessUdpRequest.TryParse(payload, out _));
    }
}
