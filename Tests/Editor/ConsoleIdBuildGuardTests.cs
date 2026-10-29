using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="ConsoleIdBuildGuard.CollectIssues"/>: правила Console ID и Adjust для сборки,
    /// и что конфиг без новых полей (до 1.0.10) грузится пустыми значениями.
    /// </summary>
    public class ConsoleIdBuildGuardTests
    {
        private const string Bundle = "com.example.game";
        private const string Token = "abcdefghij12";

        private static Runtime.SdkSettingsData ValidSettings() => new Runtime.SdkSettingsData
        {
            Enabled = true,
            Analytics = new Runtime.AnalyticsSettingData
            {
                Enabled = true,
                AppType = Runtime.AnalyticsAppType.Free,
                ConsoleId = "F000042-0",
                ConsoleIdBundle = Bundle
            },
            Adjust = new Runtime.AdjustSettingData
            {
                Enabled = true,
                Key = Token,
                Environment = Runtime.AdjustSettingData.AdjustEnvironment.Production
            }
        };

        private static (List<string> errors, List<string> warnings) Collect(
            Runtime.SdkSettingsData settings, string bundle = Bundle, bool development = false)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            ConsoleIdBuildGuard.CollectIssues(settings, bundle, development, errors, warnings);
            return (errors, warnings);
        }

        [Test]
        public void ValidSettings_NoIssues()
        {
            var (errors, warnings) = Collect(ValidSettings());

            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void NoConfigOrSdkDisabled_NoIssues()
        {
            var disabled = ValidSettings();
            disabled.Enabled = false;
            disabled.Analytics.ConsoleId = "bad";
            disabled.Adjust.Key = "";

            Assert.That(Collect(null).errors, Is.Empty);
            Assert.That(Collect(disabled).errors, Is.Empty);
            Assert.That(Collect(disabled).warnings, Is.Empty);
        }

        [Test]
        public void EmptyConsoleId_WarningOnly()
        {
            var settings = ValidSettings();
            settings.Analytics.ConsoleId = "";
            settings.Analytics.ConsoleIdBundle = "";

            var (errors, warnings) = Collect(settings);

            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("Генератор ID"));
        }

        [Test]
        public void BadFormat_Error()
        {
            var settings = ValidSettings();
            settings.Analytics.ConsoleId = "F42-0";

            var (errors, warnings) = Collect(settings);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("не по формату"));
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void IdEnteredForAnotherBundle_Error()
        {
            // Копия шаблона: конфиг с чужим ID приехал вместе с проектом, бандл уже новый.
            var (errors, _) = Collect(ValidSettings(), bundle: "com.example.copy");

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("Console ID вводили для " + Bundle + ", а собираете com.example.copy"));
            Assert.That(errors[0], Does.Contain("Выдайте новый ID"));
        }

        [Test]
        public void IdWithoutBundleStamp_Error()
        {
            var settings = ValidSettings();
            settings.Analytics.ConsoleIdBundle = "";

            var (errors, _) = Collect(settings);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("не привязан к бандлу"));
        }

        [Test]
        public void StampAfterEdit_BindsOnlyARealIdChange()
        {
            // Новый или другой ID привязывается к бандлу, стёртый — отвязывается.
            Assert.AreEqual(Bundle, ConsoleIdBuildGuard.StampAfterEdit("", "F000042-0", "", Bundle));
            Assert.AreEqual(Bundle, ConsoleIdBuildGuard.StampAfterEdit("F000042-0", "F000042-1", "com.example.old", Bundle));
            Assert.AreEqual("", ConsoleIdBuildGuard.StampAfterEdit("F000042-0", "", "com.example.old", Bundle));
            Assert.AreEqual("", ConsoleIdBuildGuard.StampAfterEdit(null, "", null, Bundle));

            // Окно лишь срезало пробелы из правленого конфига: копия шаблона не привязывает чужой
            // ID к своему бандлу одним открытием окна, и сборка её по-прежнему останавливает.
            string stamp = ConsoleIdBuildGuard.StampAfterEdit(" F000042-0 ", "F000042-0", "com.example.template", Bundle);
            Assert.AreEqual("com.example.template", stamp);

            var settings = ValidSettings();
            settings.Analytics.ConsoleIdBundle = stamp;
            Assert.That(Collect(settings).errors, Has.Count.EqualTo(1));
        }

        [Test]
        public void LetterDiffersFromAppType_Error()
        {
            var settings = ValidSettings();
            settings.Analytics.AppType = Runtime.AnalyticsAppType.Paid;

            var (errors, _) = Collect(settings);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("App Type = Paid"));

            settings.Analytics.ConsoleId = "P000042-1";
            Assert.That(Collect(settings).errors, Is.Empty);
        }

        [Test]
        public void AppTypeNone_LeftToAppTypeGuard()
        {
            var settings = ValidSettings();
            settings.Analytics.AppType = Runtime.AnalyticsAppType.None;

            Assert.That(Collect(settings).errors, Is.Empty);
        }

        [Test]
        public void AnalyticsDisabled_ConsoleIdNotChecked()
        {
            var settings = ValidSettings();
            settings.Analytics.Enabled = false;
            settings.Analytics.ConsoleId = "bad";

            var (errors, warnings) = Collect(settings);

            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Is.Empty);
        }

        [Test]
        public void AdjustSandbox_BlocksOnlyReleaseWithoutOptIn()
        {
            var settings = ValidSettings();
            settings.Adjust.Environment = Runtime.AdjustSettingData.AdjustEnvironment.Sandbox;

            var release = Collect(settings).errors;
            Assert.That(release, Has.Count.EqualTo(1));
            Assert.That(release[0], Does.Contain("Sandbox"));

            Assert.That(Collect(settings, development: true).errors, Is.Empty);

            settings.Adjust.AllowSandboxInRelease = true;
            Assert.That(Collect(settings).errors, Is.Empty);
        }

        [TestCase("")]
        [TestCase("abcdefghij1")]
        [TestCase("ABCDEFGHIJ12")]
        [TestCase("abcdefghij12 ")]
        public void AdjustBadKey_Error(string key)
        {
            var settings = ValidSettings();
            settings.Adjust.Key = key;

            var (errors, _) = Collect(settings);

            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.Contain("Adjust"));
            // Сам ключ в текст ошибки не попадает.
            if (key.Length > 0)
                Assert.That(errors[0], Does.Not.Contain(key.Trim()));
        }

        [Test]
        public void AdjustDisabled_NotChecked()
        {
            var settings = ValidSettings();
            settings.Adjust.Enabled = false;
            settings.Adjust.Key = "";
            settings.Adjust.Environment = Runtime.AdjustSettingData.AdjustEnvironment.Sandbox;

            Assert.That(Collect(settings).errors, Is.Empty);
        }

        [Test]
        public void ConfigBefore1010_LoadsEmptyAndOnlyWarns()
        {
            // Конфиг игры без ID: полей ConsoleId / ConsoleIdBundle / AllowSandboxInRelease в нём нет.
            const string json = "{\"Enabled\":true," +
                                "\"Adjust\":{\"Enabled\":true,\"Key\":\"" + Token + "\",\"Environment\":1}," +
                                "\"Analytics\":{\"Enabled\":true,\"AppType\":1}}";

            var settings = JsonUtility.FromJson<Runtime.SdkSettingsData>(json);

            Assert.That(settings.Analytics.ConsoleId, Is.Null.Or.Empty);
            Assert.That(settings.Analytics.ConsoleIdBundle, Is.Null.Or.Empty);
            Assert.IsFalse(settings.Adjust.AllowSandboxInRelease);

            var (errors, warnings) = Collect(settings);
            Assert.That(errors, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));

            var editor = SdkSettingsManager.ConvertToEditorSettings(settings);
            Assert.AreEqual(string.Empty, editor.Analytics.ConsoleId);
            Assert.AreEqual(string.Empty, editor.Analytics.ConsoleIdBundle);
            Assert.IsFalse(editor.Adjust.AllowSandboxInRelease);
        }

        [Test]
        public void SettingsConverters_KeepNewFields()
        {
            var runtime = ValidSettings();
            runtime.Adjust.Environment = Runtime.AdjustSettingData.AdjustEnvironment.Sandbox;
            runtime.Adjust.AllowSandboxInRelease = true;

            var editor = SdkSettingsManager.ConvertToEditorSettings(runtime);
            Assert.AreEqual("F000042-0", editor.Analytics.ConsoleId);
            Assert.AreEqual(Bundle, editor.Analytics.ConsoleIdBundle);
            Assert.IsTrue(editor.Adjust.AllowSandboxInRelease);

            // Пробелы вокруг вставленного ID срезаются при сохранении.
            editor.Analytics.ConsoleId = " F000042-0 ";
            var back = SdkSettingsManager.ConvertToRuntimeSettings(editor);
            Assert.AreEqual("F000042-0", back.Analytics.ConsoleId);
            Assert.AreEqual(Bundle, back.Analytics.ConsoleIdBundle);
            Assert.IsTrue(back.Adjust.AllowSandboxInRelease);
            Assert.AreEqual(Runtime.AdjustSettingData.AdjustEnvironment.Sandbox, back.Adjust.Environment);
        }
    }
}
