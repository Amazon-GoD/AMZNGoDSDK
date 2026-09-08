#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEditor.Android;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    /// <summary>
    /// Вписывает в манифест Gradle-проекта обязательные Android-записи модуля
    /// InAppPurchase, когда модуль включён.
    ///
    /// Проблема: Amazon Appstore SDK поставляется .jar-файлами, а .jar (в отличие от
    /// .aar) не несёт собственного манифеста. ResponseReceiver'ы и бутстрап-провайдер
    /// исторически объявлены в глобальном Assets/Plugins/Android/AndroidManifest.xml,
    /// но этот файл лежит ВНЕ Assets/AMZNGoDSDK и в экспортируемый .unitypackage не
    /// попадает. У потребителя SDK единственное объявление ResponseReceiver остаётся
    /// в AmazonIapV2SampleAndroidManifest.xml, который в сборку не мержится — и
    /// коллбэки PurchasingListener не приходят вообще (Appstore доставляет ответы
    /// широковещательным интентом com.amazon.inapp.purchasing.NOTIFY).
    ///
    /// Решение: на этапе генерации Gradle-проекта дописываем недостающие записи в
    /// unityLibrary/src/main/AndroidManifest.xml. Каждая запись добавляется только
    /// если компонента с таким android:name ещё нет (идемпотентно; проекты, где
    /// записи уже есть в глобальном манифесте, не затрагиваются).
    ///
    /// Симметрия с <see cref="DisabledModuleManifestCleaner"/>: тот вырезает записи
    /// ВЫКЛЮЧЕННЫХ модулей (callbackOrder 10000, последнее слово), этот добавляет
    /// записи ВКЛЮЧЁННОГО — по одному модулю работает ровно один из них.
    /// </summary>
    public class IapManifestInjector : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => 0;

        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string LogTag = "[AMZN GoD SDK] [IapManifestInjector]";

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            try
            {
                var settings = SdkSettingsManager.LoadRuntimeSettings();
                if (settings == null || !settings.Enabled)
                    return;
                if (settings.InAppPurchase == null || !settings.InAppPurchase.Enabled)
                    return;

                string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
                if (!File.Exists(manifestPath))
                {
                    Debug.LogWarning($"{LogTag} unityLibrary manifest not found at {manifestPath} — skipped.");
                    return;
                }

                InjectRequiredEntries(manifestPath);
            }
            catch (Exception e)
            {
                // Не роняем сборку из-за инжекции — но без этих записей IAP не работает,
                // поэтому предупреждение громкое.
                Debug.LogWarning($"{LogTag} Failed to inject Amazon IAP manifest entries: {e.Message}. " +
                                 "Without com.amazon.device.iap.ResponseReceiver in the manifest, " +
                                 "PurchasingListener callbacks will never arrive.");
            }
        }

        private static void InjectRequiredEntries(string manifestPath)
        {
            var doc = new XmlDocument();
            using (var reader = new XmlTextReader(manifestPath))
            {
                reader.Read();
                doc.Load(reader);
            }

            var manifest = doc.SelectSingleNode("/manifest") as XmlElement;
            var application = doc.SelectSingleNode("/manifest/application") as XmlElement;
            if (manifest == null || application == null)
            {
                Debug.LogWarning($"{LogTag} Malformed manifest (no <manifest>/<application>) — skipped.");
                return;
            }

            var added = new List<string>();

            // Appstore доставляет ответы на purchase / getProductData / getPurchaseUpdates /
            // getUserData широковещательным интентом — без статических ресиверов коллбэки
            // PurchasingListener не приходят вообще.
            if (EnsureReceiver(doc, application,
                    "com.amazon.device.iap.ResponseReceiver",
                    "com.amazon.inapp.purchasing.Permission.NOTIFY",
                    "com.amazon.inapp.purchasing.NOTIFY"))
                added.Add("<receiver com.amazon.device.iap.ResponseReceiver>");

            if (EnsureReceiver(doc, application,
                    "com.amazon.device.drm.ResponseReceiver",
                    "com.amazon.drm.Permission.NOTIFY",
                    "com.amazon.drm.NOTIFY"))
                added.Add("<receiver com.amazon.device.drm.ResponseReceiver>");

            // Поднимает Appstore SDK на старте процесса, до первой активити — иначе окно
            // покупки не показывается («No UI visible»). Детали — в AmznGoDIapBootstrapProvider.java.
            if (EnsureBootstrapProvider(doc, application))
                added.Add("<provider AmznGoDIapBootstrapProvider>");

            // С targetSdk 30+ без package visibility биндинг к сервису Appstore молча
            // отваливается. com.amazon.venezia — Appstore, com.amazon.sdktestclient — App Tester.
            if (EnsureQueriesPackage(doc, manifest, "com.amazon.venezia"))
                added.Add("<queries package com.amazon.venezia>");
            if (EnsureQueriesPackage(doc, manifest, "com.amazon.sdktestclient"))
                added.Add("<queries package com.amazon.sdktestclient>");

            if (added.Count == 0)
                return;

            Save(doc, manifestPath);
            Debug.Log($"{LogTag} Injected {added.Count} Amazon IAP manifest entr(ies): {string.Join(", ", added)}");
        }

        /// <summary>Добавляет ресивер, если компонента с таким android:name ещё нет. true — если добавил.</summary>
        private static bool EnsureReceiver(
            XmlDocument doc, XmlElement application,
            string componentName, string permission, string action)
        {
            if (FindComponentByName(application, componentName) != null)
                return false;

            var receiver = doc.CreateElement("receiver");
            receiver.Attributes.Append(CreateAndroidAttribute(doc, "name", componentName));
            receiver.Attributes.Append(CreateAndroidAttribute(doc, "permission", permission));
            // targetSdk 31+: компонент с intent-filter обязан явно указывать exported.
            receiver.Attributes.Append(CreateAndroidAttribute(doc, "exported", "true"));

            var intentFilter = doc.CreateElement("intent-filter");
            var actionEl = doc.CreateElement("action");
            actionEl.Attributes.Append(CreateAndroidAttribute(doc, "name", action));
            intentFilter.AppendChild(actionEl);
            receiver.AppendChild(intentFilter);

            application.AppendChild(receiver);
            return true;
        }

        private static bool EnsureBootstrapProvider(XmlDocument doc, XmlElement application)
        {
            const string providerName = "com.amzngod.sdk.iap.AmznGoDIapBootstrapProvider";
            if (FindComponentByName(application, providerName) != null)
                return false;

            // Authorities провайдера должны быть уникальны на устройство — строим от
            // applicationId сборки. Берём его из PlayerSettings, а не через плейсхолдер
            // ${applicationId}: unityLibrary — локальный library-модуль, и его манифест
            // merger обрабатывает до мержа в приложение, где плейсхолдер может не
            // резолвиться в id приложения.
            string appId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);

            var provider = doc.CreateElement("provider");
            provider.Attributes.Append(CreateAndroidAttribute(doc, "name", providerName));
            provider.Attributes.Append(CreateAndroidAttribute(doc, "authorities", appId + ".amzngod.iapbootstrap"));
            provider.Attributes.Append(CreateAndroidAttribute(doc, "exported", "false"));
            provider.Attributes.Append(CreateAndroidAttribute(doc, "initOrder", "100"));

            application.AppendChild(provider);
            return true;
        }

        private static bool EnsureQueriesPackage(XmlDocument doc, XmlElement manifest, string packageName)
        {
            // Ищем пакет в ЛЮБОМ из существующих <queries> (их может быть несколько — merger склеит).
            foreach (XmlNode queriesNode in manifest.ChildNodes)
            {
                if (!(queriesNode is XmlElement queries) || queries.LocalName != "queries")
                    continue;

                foreach (XmlNode pkgNode in queries.ChildNodes)
                {
                    if (pkgNode is XmlElement pkg && pkg.LocalName == "package" &&
                        GetAndroidName(pkg) == packageName)
                        return false;
                }
            }

            XmlElement target = null;
            foreach (XmlNode node in manifest.ChildNodes)
            {
                if (node is XmlElement el && el.LocalName == "queries")
                {
                    target = el;
                    break;
                }
            }

            if (target == null)
            {
                target = doc.CreateElement("queries");
                manifest.AppendChild(target);
            }

            var package = doc.CreateElement("package");
            package.Attributes.Append(CreateAndroidAttribute(doc, "name", packageName));
            target.AppendChild(package);
            return true;
        }

        private static XmlElement FindComponentByName(XmlElement application, string androidName)
        {
            foreach (XmlNode node in application.ChildNodes)
            {
                if (node is XmlElement el && GetAndroidName(el) == androidName)
                    return el;
            }

            return null;
        }

        private static string GetAndroidName(XmlElement el)
        {
            string name = el.GetAttribute("name", AndroidNs);
            return string.IsNullOrEmpty(name) ? el.GetAttribute("android:name") : name;
        }

        private static XmlAttribute CreateAndroidAttribute(XmlDocument doc, string key, string value)
        {
            var attr = doc.CreateAttribute("android", key, AndroidNs);
            attr.Value = value;
            return attr;
        }

        private static void Save(XmlDocument doc, string path)
        {
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = new UTF8Encoding(false),
            };
            using (var writer = XmlWriter.Create(path, settings))
            {
                doc.Save(writer);
            }
        }
    }
}
#endif
