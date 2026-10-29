using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>Формат Console ID: ^[FP]\d{6}-(0|[1-9]\d{0,2})$ — с ASCII-цифрами и без хвостового \n.</summary>
    public class ConsoleIdTests
    {
        [TestCase("F000042-0")]
        [TestCase("P000042-1")]
        [TestCase("F000001-999")]
        [TestCase("P999999-10")]
        [TestCase("F000000-0")]
        public void IsValid_AcceptsIssuedFormat(string id)
        {
            Assert.IsTrue(Runtime.ConsoleId.IsValid(id), id);
        }

        [TestCase("")]
        [TestCase("f000042-0")]
        [TestCase("p000042-1")]
        [TestCase("X000042-0")]
        [TestCase("F00042-0")]
        [TestCase("F0000042-0")]
        [TestCase("F000042")]
        [TestCase("F000042-")]
        [TestCase("F000042_0")]
        [TestCase("F000042-00")]
        [TestCase("F000042-01")]
        [TestCase("F000042-1000")]
        [TestCase(" F000042-0")]
        [TestCase("F000042-0 ")]
        [TestCase("F000042-0\n")]
        [TestCase("F000042-0 Рабочее имя")]
        [TestCase("F00004٢-0")] // арабско-индийская цифра: \d в .NET её пропустил бы
        [TestCase("F000042-１")] // полноширинная 1
        public void IsValid_RejectsEverythingElse(string id)
        {
            Assert.IsFalse(Runtime.ConsoleId.IsValid(id), id);
        }

        [Test]
        public void IsValid_RejectsNull()
        {
            Assert.IsFalse(Runtime.ConsoleId.IsValid(null));
        }

        [Test]
        public void MatchesAppType_LetterFollowsListingType()
        {
            Assert.IsTrue(Runtime.ConsoleId.MatchesAppType("F000042-0", Runtime.AnalyticsAppType.Free));
            Assert.IsTrue(Runtime.ConsoleId.MatchesAppType("P000042-1", Runtime.AnalyticsAppType.Paid));
            Assert.IsFalse(Runtime.ConsoleId.MatchesAppType("F000042-0", Runtime.AnalyticsAppType.Paid));
            Assert.IsFalse(Runtime.ConsoleId.MatchesAppType("P000042-1", Runtime.AnalyticsAppType.Free));
            Assert.IsFalse(Runtime.ConsoleId.MatchesAppType("F000042-0", Runtime.AnalyticsAppType.None));
            Assert.IsFalse(Runtime.ConsoleId.MatchesAppType("f000042-0", Runtime.AnalyticsAppType.Free));
        }

        [Test]
        public void LetterFor_MapsAppType()
        {
            Assert.AreEqual('F', Runtime.ConsoleId.LetterFor(Runtime.AnalyticsAppType.Free));
            Assert.AreEqual('P', Runtime.ConsoleId.LetterFor(Runtime.AnalyticsAppType.Paid));
            Assert.AreEqual('\0', Runtime.ConsoleId.LetterFor(Runtime.AnalyticsAppType.None));
        }
    }
}
