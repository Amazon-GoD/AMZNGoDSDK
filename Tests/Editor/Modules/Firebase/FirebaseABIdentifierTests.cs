using System;
using NUnit.Framework;

namespace AMZNGoDSDK.Runtime.Tests
{
    /// <summary>
    /// FirebaseModule.ValidateABIdentifier: A/B test ids and group names must be non-blank and
    /// at most 100 characters (Firebase user-property value limit).
    /// </summary>
    public class FirebaseABIdentifierTests
    {
        private const int MaxLength = 100;

        [TestCase("test")]
        [TestCase("a")]
        [TestCase("adjust_enable")]
        [TestCase("true")]
        public void ValidateABIdentifier_ValidValue_DoesNotThrow(string value)
        {
            Assert.DoesNotThrow(() => FirebaseModule.ValidateABIdentifier(value, "testId"));
        }

        [Test]
        public void ValidateABIdentifier_ExactlyMaxLength_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => FirebaseModule.ValidateABIdentifier(new string('x', MaxLength), "testId"));
        }

        [Test]
        public void ValidateABIdentifier_OverMaxLength_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() =>
                FirebaseModule.ValidateABIdentifier(new string('x', MaxLength + 1), "testId"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("\t\n")]
        public void ValidateABIdentifier_BlankValue_ThrowsArgumentException(string value)
        {
            Assert.Throws<ArgumentException>(() => FirebaseModule.ValidateABIdentifier(value, "testId"));
        }

        [Test]
        public void ValidateABIdentifier_Invalid_ReportsParameterName()
        {
            var exception = Assert.Throws<ArgumentException>(() =>
                FirebaseModule.ValidateABIdentifier("", "groupName"));

            Assert.AreEqual("groupName", exception.ParamName);
        }
    }
}
