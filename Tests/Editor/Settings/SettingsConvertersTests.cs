using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AMZNGoDSDK.Editor.Tests
{
    /// <summary>
    /// <see cref="SdkSettingsManager.ConvertToEditorSettings"/> / <see cref="SdkSettingsManager.ConvertToRuntimeSettings"/>:
    /// every runtime field survives runtime → editor → runtime. The editor and runtime models are separate
    /// types, so a field added to one model or one converter only is silently lost on Save Settings.
    /// The round trip fills every field by reflection, so a new field is covered without touching this test.
    /// </summary>
    public class SettingsConvertersTests
    {
        // Fields the converters drop or pin on purpose. Anything else must round-trip unchanged.
        private static readonly HashSet<string> IntentionallyNotConverted = new HashSet<string>
        {
            // Deprecated: migrated into Analytics by DataLoader, never written back.
            "FirstOpenTracking",
            // Removed from the settings UI; the converter always writes the default.
            "CrossPromo.DefaultPromotedAppId",
            "CrossPromo.VideoBackend",
        };

        private int _counter;

        [Test]
        public void RoundTrip_KeepsEveryRuntimeField()
        {
            var original = new Runtime.SdkSettingsData();
            int filled = Fill(original, "");

            // Sanity check of the reflection walker itself: the config has dozens of fields.
            Assert.That(filled, Is.GreaterThan(40), "Reflection filler found too few fields.");

            var editor = SdkSettingsManager.ConvertToEditorSettings(original);
            var back = SdkSettingsManager.ConvertToRuntimeSettings(editor);

            var differences = new List<string>();
            Compare(original, back, "", differences);

            Assert.That(differences, Is.Empty,
                "Fields lost or changed by the settings converters (update BOTH models and BOTH converters):\n  " +
                string.Join("\n  ", differences));
        }

        [Test]
        public void ConfigWithoutModuleBlocks_ConvertsWithDefaults()
        {
            // A config saved by an old SDK version: module blocks missing entirely.
            var runtime = JsonUtility.FromJson<Runtime.SdkSettingsData>("{\"Enabled\":true}");

            var editor = SdkSettingsManager.ConvertToEditorSettings(runtime);

            Assert.IsTrue(editor.Enabled);
            Assert.AreEqual(Runtime.AppLovinSettingData.DefaultInterstitialAdPlacement, editor.AppLovin.InterstitialAdPlacement);
            Assert.AreEqual(Runtime.AppLovinSettingData.DefaultRewardedAdPlacement, editor.AppLovin.RewardedAdPlacement);
            Assert.AreEqual(Runtime.AnalyticsSettingData.DefaultBaseUrl, editor.Analytics.BaseUrl);
            Assert.AreEqual(Runtime.AnalyticsSettingData.DefaultApiKey, editor.Analytics.ApiKey);
            Assert.AreEqual(AnalyticsAppType.None, editor.Analytics.AppType);
            Assert.AreEqual(Runtime.CrossPromoSettingData.DefaultConfigUrl, editor.CrossPromo.ConfigUrl);
            Assert.AreEqual(Runtime.FirebaseSettingData.AdjustEnableTestId, editor.Firebase.AdjustEnableRemoteConfigKey);
            Assert.AreEqual(Runtime.FirebaseSettingData.DefaultABTestConstantsPath, editor.Firebase.ABTestConstantsPath);
        }

        // 1.0.7: Remote Config is enabled by default for new settings.
        [Test]
        public void NewSettings_RemoteConfigEnabledByDefault()
        {
            Assert.IsTrue(new Runtime.FirebaseSettingData().EnableRemoteConfig);
            Assert.IsTrue(new FirebaseSettingData().EnableRemoteConfig);

            var back = SdkSettingsManager.ConvertToRuntimeSettings(new SdkSettingsData());
            Assert.IsTrue(back.Firebase.EnableRemoteConfig);
        }

        [Test]
        public void EmptyOrInvalidValues_ReplacedByDefaultsOnSave()
        {
            var editor = new SdkSettingsData();
            editor.AppLovin.InterstitialAdPlacement = "  ";
            editor.AppLovin.RewardedAdPlacement = " rewarded_main ";
            editor.Firebase.AdjustEnableRemoteConfigKey = "1-invalid";
            editor.Firebase.RemoteConfigFetchTimeoutSeconds = 0;
            editor.Firebase.RemoteConfigMinimumFetchIntervalSeconds = -5;
            editor.Firebase.ABTestConstantsPath = "Assets\\Game\\ABTestConstants.cs";

            var runtime = SdkSettingsManager.ConvertToRuntimeSettings(editor);

            Assert.AreEqual(Runtime.AppLovinSettingData.DefaultInterstitialAdPlacement, runtime.AppLovin.InterstitialAdPlacement);
            Assert.AreEqual("rewarded_main", runtime.AppLovin.RewardedAdPlacement);
            Assert.AreEqual(Runtime.FirebaseSettingData.AdjustEnableTestId, runtime.Firebase.AdjustEnableRemoteConfigKey);
            Assert.AreEqual(Runtime.FirebaseSettingData.DefaultFetchTimeoutSeconds, runtime.Firebase.RemoteConfigFetchTimeoutSeconds);
            Assert.AreEqual(Runtime.FirebaseSettingData.DefaultMinimumFetchIntervalSeconds, runtime.Firebase.RemoteConfigMinimumFetchIntervalSeconds);
            Assert.AreEqual("Assets/Game/ABTestConstants.cs", runtime.Firebase.ABTestConstantsPath);
        }

        [Test]
        public void IapTermDays_NotTakenFromDeprecatedDurationDays()
        {
            // IAP-15: old configs carry the default DurationDays = 30 while the portfolio is weekly.
            // A zero TermDays must stay zero so Save Settings and the build ask for the real term.
            var runtime = new Runtime.SdkSettingsData();
            runtime.InAppPurchase.SubscriptionProducts.Add(new Runtime.SubscriptionProduct { ProductId = "sub.weekly" });

            var editor = SdkSettingsManager.ConvertToEditorSettings(runtime);

            Assert.That(editor.InAppPurchase.SubscriptionProducts, Has.Count.EqualTo(1));
            Assert.AreEqual(0, editor.InAppPurchase.SubscriptionProducts[0].TermDays);
        }

        [Test]
        public void AbTestsDefaultGroup_FilledFromFirstGroupForOldConfigs()
        {
            var runtime = new Runtime.SdkSettingsData();
            runtime.Firebase.ABTests.Add(new Runtime.ABTestEntry
            {
                TestName = "Paywall",
                TestId = "paywall",
                GroupNames = new List<string> { "control", "variant" },
                DefaultGroup = null
            });

            var editor = SdkSettingsManager.ConvertToEditorSettings(runtime);

            Assert.AreEqual("control", editor.Firebase.ABTests[0].DefaultGroup);
            // The converter clones the list: editing the window must not mutate the loaded runtime config.
            Assert.AreNotSame(runtime.Firebase.ABTests[0], editor.Firebase.ABTests[0]);
            Assert.AreNotSame(runtime.Firebase.ABTests[0].GroupNames, editor.Firebase.ABTests[0].GroupNames);
        }

        // 1.0.6: an old Cross-Promo ConfigUrl is replaced by master.json exactly once; later edits are kept.
        [Test]
        public void MigrateConfigUrl_ReplacesOldUrlOnce()
        {
            var settings = new SdkSettingsData();
            settings.CrossPromo.ConfigUrlMigrationVersion = 0;
            settings.CrossPromo.ConfigUrl = "https://old.example.com/config.json";

            SdkSettingsManager.MigrateConfigUrl(settings);

            Assert.AreEqual(Runtime.CrossPromoSettingData.DefaultConfigUrl, settings.CrossPromo.ConfigUrl);
            Assert.AreEqual(Runtime.CrossPromoSettingData.CurrentConfigUrlMigrationVersion, settings.CrossPromo.ConfigUrlMigrationVersion);

            settings.CrossPromo.ConfigUrl = "https://custom.example.com/master.json";
            SdkSettingsManager.MigrateConfigUrl(settings);

            Assert.AreEqual("https://custom.example.com/master.json", settings.CrossPromo.ConfigUrl);
        }

        [Test]
        public void MigrateConfigUrl_NullSettingsOrBlock_Handled()
        {
            Assert.DoesNotThrow(() => SdkSettingsManager.MigrateConfigUrl(null));

            var settings = new SdkSettingsData { CrossPromo = null };
            SdkSettingsManager.MigrateConfigUrl(settings);

            Assert.IsNotNull(settings.CrossPromo);
            Assert.AreEqual(Runtime.CrossPromoSettingData.DefaultConfigUrl, settings.CrossPromo.ConfigUrl);
        }

        [Test]
        public void RuntimeCrossPromoMigrationVersion_ZeroForJsonWithoutField()
        {
            // The field deliberately has no initializer: a config without it must go through the migration.
            var runtime = JsonUtility.FromJson<Runtime.SdkSettingsData>(
                "{\"Enabled\":true,\"CrossPromo\":{\"Enabled\":true,\"ConfigUrl\":\"https://old.example.com/c.json\"}}");

            Assert.AreEqual(0, runtime.CrossPromo.ConfigUrlMigrationVersion);
        }

        private int Fill(object target, string path)
        {
            int filled = 0;
            foreach (FieldInfo field in SerializedFields(target.GetType()))
            {
                string fieldPath = Join(path, field.Name);
                if (IntentionallyNotConverted.Contains(fieldPath))
                    continue;

                object value = MakeValue(field.FieldType, field.GetValue(target), fieldPath, ref filled);
                field.SetValue(target, value);
            }

            return filled;
        }

        private object MakeValue(Type type, object current, string path, ref int filled)
        {
            if (type == typeof(bool))
            {
                filled++;
                return !(bool)current;
            }

            if (type == typeof(int))
            {
                filled++;
                return 100 + ++_counter;
            }

            if (type == typeof(float))
            {
                filled++;
                return 100.5f + ++_counter;
            }

            if (type == typeof(string))
            {
                // A valid C# identifier and Remote Config key, without spaces: stable under the converters' normalization.
                filled++;
                return "v" + ++_counter;
            }

            if (type.IsEnum)
            {
                filled++;
                Array values = Enum.GetValues(type);
                object last = values.GetValue(values.Length - 1);
                return Equals(last, current) ? values.GetValue(0) : last;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type itemType = type.GetGenericArguments()[0];
                var list = (IList)Activator.CreateInstance(type);
                object itemDefault = itemType.IsValueType ? Activator.CreateInstance(itemType) : CreateOrNull(itemType);
                list.Add(MakeValue(itemType, itemDefault, path + "[]", ref filled));
                return list;
            }

            if (type.IsClass && type.IsDefined(typeof(SerializableAttribute), false))
            {
                object instance = current ?? Activator.CreateInstance(type);
                filled += Fill(instance, path);
                return instance;
            }

            Assert.Fail($"Unsupported settings field type {type.Name} at {path}: extend the test filler.");
            return null;
        }

        private static void Compare(object expected, object actual, string path, List<string> differences)
        {
            if (expected == null || actual == null)
            {
                if (!ReferenceEquals(expected, actual))
                    differences.Add($"{path}: expected {Describe(expected)}, got {Describe(actual)}");
                return;
            }

            Type type = expected.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            {
                if (!Equals(expected, actual))
                    differences.Add($"{path}: expected {Describe(expected)}, got {Describe(actual)}");
                return;
            }

            if (expected is IList expectedList)
            {
                var actualList = (IList)actual;
                if (expectedList.Count != actualList.Count)
                {
                    differences.Add($"{path}: expected {expectedList.Count} items, got {actualList.Count}");
                    return;
                }

                for (int i = 0; i < expectedList.Count; i++)
                    Compare(expectedList[i], actualList[i], $"{path}[{i}]", differences);
                return;
            }

            foreach (FieldInfo field in SerializedFields(type))
            {
                string fieldPath = Join(path, field.Name);
                if (IntentionallyNotConverted.Contains(fieldPath))
                    continue;

                Compare(field.GetValue(expected), field.GetValue(actual), fieldPath, differences);
            }
        }

        // What JsonUtility writes: public instance fields, minus constants, readonly and [NonSerialized].
        // [Obsolete] fields are kept readable for old configs but are no longer converted.
        private static IEnumerable<FieldInfo> SerializedFields(Type type)
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly || field.IsLiteral || field.IsNotSerialized)
                    continue;
                if (field.IsDefined(typeof(ObsoleteAttribute), false))
                    continue;

                yield return field;
            }
        }

        private static object CreateOrNull(Type type) =>
            type == typeof(string) ? null : Activator.CreateInstance(type);

        private static string Join(string path, string name) => string.IsNullOrEmpty(path) ? name : path + "." + name;

        private static string Describe(object value) => value == null ? "null" : $"'{value}'";
    }
}
