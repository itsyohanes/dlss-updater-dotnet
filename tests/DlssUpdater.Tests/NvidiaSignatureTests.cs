using dlss_updater.Services;
using Xunit;

namespace DlssUpdater.Tests
{
    public class NvidiaSignatureTests
    {
        [Theory]
        [InlineData("CN=NVIDIA Corporation, OU=1005bk6, O=NVIDIA Corporation, L=Santa Clara, S=California, C=US", true)]
        [InlineData("CN=NVIDIA Corporation, O=NVIDIA Corporation", true)]
        // Only the organization named in the common name isn't enough.
        [InlineData("CN=NVIDIA Corporation, O=Someone Else", false)]
        [InlineData("CN=Someone Else, O=NVIDIA Corporation", false)]
        [InlineData("CN=NVIDIA Corporation Fake, O=NVIDIA Corporation Fake", false)]
        [InlineData("CN=Microsoft Corporation, O=Microsoft Corporation", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsNvidiaSigner_RequiresNvidiaAsNameAndOrganization(string subject, bool expected)
        {
            Assert.Equal(expected, NvidiaSignature.IsNvidiaSigner(subject));
        }

        [Fact]
        public void IsSignedByNvidia_RejectsAnUnsignedFile()
        {
            using (TempFolder folder = new TempFolder())
            {
                string path = folder.WriteFile("nvngx_dlss.dll", "not a real dll");

                Assert.False(NvidiaSignature.IsSignedByNvidia(path));
            }
        }

        [Fact]
        public void IsSignedByNvidia_RejectsAMissingFile()
        {
            Assert.False(NvidiaSignature.IsSignedByNvidia(@"Z:\does\not\exist\nvngx_dlss.dll"));
        }
    }
}
