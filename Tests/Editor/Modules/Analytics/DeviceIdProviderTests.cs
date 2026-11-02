using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// Pure parts of DeviceIdProvider: SHA-256 hashing of the Fire ID and the usable-ID check.
    /// Nothing here touches PlayerPrefs or the resolve flow.
    /// </summary>
    public class DeviceIdProviderTests
    {
        private const string ZeroFireId = "00000000-0000-0000-0000-000000000000";

        // SHA-256("abc") and SHA-256("") from FIPS 180-2 test vectors.
        private const string Sha256Abc = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

        [Test]
        public void HashDeviceId_KnownValue_MatchesSha256Vector()
        {
            Assert.AreEqual(Sha256Abc, DeviceIdProvider.HashDeviceId("abc"));
        }

        [Test]
        public void HashDeviceId_IsTrimmedAndCaseInsensitive()
        {
            Assert.AreEqual(Sha256Abc, DeviceIdProvider.HashDeviceId("ABC"));
            Assert.AreEqual(Sha256Abc, DeviceIdProvider.HashDeviceId("  abc \n"));
            Assert.AreEqual(Sha256Abc, DeviceIdProvider.HashDeviceId("\tAbC"));
        }

        [Test]
        public void HashDeviceId_IsStableLowercaseHex64()
        {
            string first = DeviceIdProvider.HashDeviceId("0a1b2c3d-4e5f-6789-abcd-ef0123456789");
            string second = DeviceIdProvider.HashDeviceId("0a1b2c3d-4e5f-6789-abcd-ef0123456789");

            Assert.AreEqual(first, second);
            Assert.AreEqual(64, first.Length);
            Assert.AreEqual(first.ToLowerInvariant(), first);
            Assert.IsFalse(first.Contains("-"));
        }

        [Test]
        public void HashDeviceId_DifferentIds_ProduceDifferentHashes()
        {
            Assert.AreNotEqual(DeviceIdProvider.HashDeviceId("id-one"), DeviceIdProvider.HashDeviceId("id-two"));
        }

        [Test]
        public void HashDeviceId_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(DeviceIdProvider.HashDeviceId(null));
            Assert.IsNull(DeviceIdProvider.HashDeviceId(string.Empty));
        }

        // Regression 1.0.8: an all-zero Fire ID (child profile) and empty values must not count as an identity.
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\n")]
        [TestCase("00000000-0000-0000-0000-000000000000")]
        [TestCase("  00000000-0000-0000-0000-000000000000  ")]
        public void IsUsableId_RejectsEmptyAndZeroFireId(string rawId)
        {
            Assert.IsFalse(DeviceIdProvider.IsUsableId(rawId));
        }

        [TestCase("0a1b2c3d-4e5f-6789-abcd-ef0123456789")]
        [TestCase("  0a1b2c3d-4e5f-6789-abcd-ef0123456789 ")]
        [TestCase("00000000-0000-0000-0000-000000000001")]
        public void IsUsableId_AcceptsRealIds(string rawId)
        {
            Assert.IsTrue(DeviceIdProvider.IsUsableId(rawId));
        }

        [Test]
        public void IsUsableId_ZeroFireIdConstant_IsRejected()
        {
            Assert.IsFalse(DeviceIdProvider.IsUsableId(ZeroFireId));
        }
    }
}
