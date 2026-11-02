using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// Firebase settings: Remote Config key rules (<see cref="Runtime.FirebaseSettingData"/>), A/B test validation
    /// and the generated ABTestConstants code (<see cref="FirebaseABTestConstantsGenerator"/>). A bad test entry that
    /// slips through validation becomes a generated file that does not compile, so validation is the real guard.
    /// </summary>
    public class FirebaseABTestsTests
    {
        private static Runtime.ABTestEntry Test(string name, string id, string defaultGroup, params string[] groups) =>
            new Runtime.ABTestEntry
            {
                TestName = name,
                TestId = id,
                DefaultGroup = defaultGroup,
                GroupNames = new List<string>(groups)
            };

        private static FirebaseSettingData Settings(params Runtime.ABTestEntry[] tests) => new FirebaseSettingData
        {
            Enabled = true,
            ABTests = new List<Runtime.ABTestEntry>(tests)
        };

        private static string Validate(FirebaseSettingData settings)
        {
            bool valid = FirebaseABTestConstantsGenerator.Validate(settings, out string error);
            Assert.AreEqual(valid, error == null, "Validate must return an error exactly when it fails.");
            return error;
        }

        [TestCase("a")]
        [TestCase("_flag")]
        [TestCase("adjust_enable")]
        [TestCase("Key_2")]
        public void RemoteConfigKey_Valid(string key)
        {
            Assert.IsTrue(Runtime.FirebaseSettingData.IsValidRemoteConfigKey(key), key);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("1key")]
        [TestCase("key-name")]
        [TestCase("key name")]
        [TestCase("ключ")]
        public void RemoteConfigKey_Invalid(string key)
        {
            Assert.IsFalse(Runtime.FirebaseSettingData.IsValidRemoteConfigKey(key), key ?? "null");
        }

        [Test]
        public void RemoteConfigKey_LengthLimit100()
        {
            Assert.IsTrue(Runtime.FirebaseSettingData.IsValidRemoteConfigKey(new string('a', 100)));
            Assert.IsFalse(Runtime.FirebaseSettingData.IsValidRemoteConfigKey(new string('a', 101)));
        }

        // 1.0.8: a custom Adjust Remote Config key; anything unusable falls back to the built-in key.
        [Test]
        public void NormalizeAdjustKey_TrimsOrFallsBack()
        {
            Assert.AreEqual("my_key", Runtime.FirebaseSettingData.NormalizeAdjustEnableRemoteConfigKey("  my_key "));
            Assert.AreEqual(Runtime.FirebaseSettingData.AdjustEnableTestId, Runtime.FirebaseSettingData.NormalizeAdjustEnableRemoteConfigKey(null));
            Assert.AreEqual(Runtime.FirebaseSettingData.AdjustEnableTestId, Runtime.FirebaseSettingData.NormalizeAdjustEnableRemoteConfigKey("   "));
            Assert.AreEqual(Runtime.FirebaseSettingData.AdjustEnableTestId, Runtime.FirebaseSettingData.NormalizeAdjustEnableRemoteConfigKey("bad-key"));
        }

        [Test]
        public void DefaultGroup_ExplicitOrFirstGroup()
        {
            Assert.AreEqual("b", Test("T", "t", "b", "a", "b").GetDefaultGroup());
            Assert.AreEqual("a", Test("T", "t", null, "a", "b").GetDefaultGroup());
            Assert.AreEqual("a", Test("T", "t", "", "a", "b").GetDefaultGroup());
            Assert.IsNull(Test("T", "t", null).GetDefaultGroup());
            Assert.IsNull(new Runtime.ABTestEntry { DefaultGroup = null, GroupNames = null }.GetDefaultGroup());
        }

        [Test]
        public void Validate_NoTests_Passes()
        {
            Assert.IsNull(Validate(new FirebaseSettingData()));
            Assert.IsNull(Validate(Settings(Test("Paywall", "paywall", "control", "control", "variant"))));
        }

        [Test]
        public void Validate_InvalidAdjustKey_Fails()
        {
            var settings = Settings();
            settings.AdjustEnableRemoteConfigKey = "1-bad";

            Assert.That(Validate(settings), Does.Contain("Adjust Remote Config Key"));
        }

        [TestCase("Assets/Game/Generated/ABTestConstants.cs")]
        [TestCase("Assets\\Game\\ABTestConstants.cs")]
        public void Validate_ConstantsPath_Valid(string path)
        {
            var settings = Settings();
            settings.ABTestConstantsPath = path;

            Assert.IsNull(Validate(settings));
        }

        [TestCase("Packages/com.game/AB.cs")]
        [TestCase("Assets/Game/AB.txt")]
        [TestCase("Assets/Editor/AB.cs")]
        [TestCase("Assets/Game/editor/AB.cs")]
        [TestCase("Assets/.hidden/AB.cs")]
        [TestCase("Assets/Hidden~/AB.cs")]
        [TestCase("Assets/../AB.cs")]
        [TestCase("Assets//AB.cs")]
        [TestCase("Assets/ Game/AB.cs")]
        public void Validate_ConstantsPath_Invalid(string path)
        {
            var settings = Settings();
            settings.ABTestConstantsPath = path;

            Assert.That(Validate(settings), Does.Contain("Файл констант"), path);
        }

        [Test]
        public void Validate_EmptyConstantsPath_UsesDefault()
        {
            var settings = Settings();
            settings.ABTestConstantsPath = "  ";

            Assert.AreEqual(Runtime.FirebaseSettingData.DefaultABTestConstantsPath, FirebaseABTestConstantsGenerator.OutputPath(settings));
            Assert.IsNull(Validate(settings));
        }

        [TestCase("1Test")]
        [TestCase("Test Name")]
        [TestCase("")]
        [TestCase("TestIds")]
        [TestCase("TestDefaultGroups")]
        public void Validate_BadTestName_Fails(string name)
        {
            Assert.That(Validate(Settings(Test(name, "test_id", "a", "a"))), Does.Contain("Test Name"));
        }

        [Test]
        public void Validate_DuplicateNameOrId_Fails()
        {
            Assert.That(Validate(Settings(Test("Paywall", "a", "x", "x"), Test("Paywall", "b", "x", "x"))),
                Does.Contain("Повторяется Test Name 'Paywall'"));
            Assert.That(Validate(Settings(Test("A", "same_id", "x", "x"), Test("B", "same_id", "x", "x"))),
                Does.Contain("Повторяется Test ID 'same_id'"));
        }

        [Test]
        public void Validate_BadTestId_Fails()
        {
            Assert.That(Validate(Settings(Test("A", "bad-id", "x", "x"))), Does.Contain("Test ID"));
            Assert.That(Validate(Settings(Test("A", new string('i', 101), "x", "x"))), Does.Contain("Test ID"));
        }

        [Test]
        public void Validate_GroupRules()
        {
            Assert.That(Validate(Settings(Test("A", "a", null))), Does.Contain("хотя бы одну группу"));
            Assert.That(Validate(Settings(Test("A", "a", "x", "x", "x"))), Does.Contain("повторяется группа 'x'"));
            Assert.That(Validate(Settings(Test("A", "a", "x", "x", "bad group"))), Does.Contain("группа 'bad group'"));
            // A group named like the generated class would make "class AGroupNames { const AGroupNames }" — CS0542.
            Assert.That(Validate(Settings(Test("A", "a", "x", "x", "AGroupNames"))), Does.Contain("AGroupNames"));
            Assert.That(Validate(Settings(Test("A", "a", "missing", "x", "y"))), Does.Contain("Default Group"));
            Assert.That(Validate(Settings(new Runtime.ABTestEntry { TestName = "A", TestId = "a", GroupNames = null })),
                Does.Contain("хотя бы одну группу"));
        }

        [Test]
        public void Validate_NullEntry_Fails()
        {
            Assert.That(Validate(Settings((Runtime.ABTestEntry)null)), Does.Contain("отсутствует описание теста"));
        }

        [Test]
        public void Validate_BuiltInAdjustTest_RequiresTrueFalseWithDefaultTrue()
        {
            string key = Runtime.FirebaseSettingData.AdjustEnableTestId;

            Assert.IsNull(Validate(Settings(Test("AdjustEnable", key, "true", "true", "false"))));
            Assert.That(Validate(Settings(Test("AdjustEnable", key, "false", "true", "false"))), Does.Contain("Встроенный"));
            Assert.That(Validate(Settings(Test("AdjustEnable", key, "on", "on", "off"))), Does.Contain("Встроенный"));
            Assert.That(Validate(Settings(Test("AdjustEnable", key, "true", "true", "false", "maybe"))), Does.Contain("Встроенный"));
        }

        [Test]
        public void Validate_CustomAdjustKey_MovesTheBuiltInRule()
        {
            var custom = Settings(Test("AdjustEnable", "my_adjust", "off", "on", "off"));
            custom.AdjustEnableRemoteConfigKey = "my_adjust";
            Assert.That(Validate(custom), Does.Contain("Встроенный my_adjust"));

            // With a custom key the old built-in id is an ordinary test.
            var ordinary = Settings(Test("Legacy", Runtime.FirebaseSettingData.AdjustEnableTestId, "a", "a", "b"));
            ordinary.AdjustEnableRemoteConfigKey = "my_adjust";
            Assert.IsNull(Validate(ordinary));
        }

        [Test]
        public void BuildCode_EmitsConstantsForEveryTest()
        {
            var settings = Settings(
                Test("Paywall", "paywall_test", "variant", "control", "variant"),
                Test("AdjustEnable", Runtime.FirebaseSettingData.AdjustEnableTestId, "true", "true", "false"));

            string code = FirebaseABTestConstantsGenerator.BuildCode(settings);

            Assert.That(code, Does.StartWith("// Auto-generated by AMZN GoD SDK Settings."));
            Assert.That(code, Does.Contain("namespace ABTesting.Generated"));
            Assert.That(code, Does.Contain("public static class TestIds"));
            Assert.That(code, Does.Contain("public const string @Paywall = \"paywall_test\";"));
            Assert.That(code, Does.Contain("public static class TestDefaultGroups"));
            Assert.That(code, Does.Contain("public const string @Paywall = \"variant\";"));
            Assert.That(code, Does.Contain("public static class @PaywallGroupNames"));
            Assert.That(code, Does.Contain("public const string @control = \"control\";"));
            // Keyword groups compile thanks to verbatim identifiers.
            Assert.That(code, Does.Contain("public const string @true = \"true\";"));
            Assert.That(code, Does.Contain("public const string @false = \"false\";"));
            Assert.AreEqual(CountOf(code, '{'), CountOf(code, '}'), "Generated code has unbalanced braces.");
        }

        [Test]
        public void BuildCode_InvalidSettings_Throws()
        {
            var settings = Settings(Test("1Bad", "bad", "a", "a"));

            Assert.Throws<ArgumentException>(() => FirebaseABTestConstantsGenerator.BuildCode(settings));
        }

        private static int CountOf(string text, char character)
        {
            int count = 0;
            foreach (char c in text)
            {
                if (c == character)
                    count++;
            }

            return count;
        }
    }
}
