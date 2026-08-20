using UnityEditor;

namespace AMZNGoDSDK.Editor.Deploy
{
    /// <summary>
    /// Per-user настройки деплоя. Всё в EditorPrefs — не попадает в git и не
    /// экспортируется в SDK-пакет. Перенесено из Assets/GoDSDKDeploy;
    /// добавлены настройки Release (UPM) секции.
    /// </summary>
    internal static class SdkDeployUserPrefs
    {
        private const string KeyToken          = "GoDSDKDeploy.YandexOAuthToken";
        private const string KeyLocalRoot      = "GoDSDKDeploy.LocalRoot";
        private const string KeyRemoteDir      = "GoDSDKDeploy.RemoteDir";
        private const string KeyUpload         = "GoDSDKDeploy.UploadEnabled";
        private const string KeyReleaseVersion = "GoDSDKDeploy.ReleaseVersion";
        private const string KeyReleaseNote    = "GoDSDKDeploy.ReleaseNote";

        public const string DefaultLocalRoot = @"A:\GoDSDK";
        public const string DefaultRemoteDir = "/SDK";

        public static string YandexToken
        {
            get => EditorPrefs.GetString(KeyToken, string.Empty);
            set => EditorPrefs.SetString(KeyToken, value ?? string.Empty);
        }

        public static string LocalRoot
        {
            get => EditorPrefs.GetString(KeyLocalRoot, DefaultLocalRoot);
            set => EditorPrefs.SetString(KeyLocalRoot, string.IsNullOrWhiteSpace(value) ? DefaultLocalRoot : value);
        }

        public static string RemoteDir
        {
            get => EditorPrefs.GetString(KeyRemoteDir, DefaultRemoteDir);
            set => EditorPrefs.SetString(KeyRemoteDir, string.IsNullOrWhiteSpace(value) ? DefaultRemoteDir : value);
        }

        public static bool UploadEnabled
        {
            get => EditorPrefs.GetBool(KeyUpload, false);
            set => EditorPrefs.SetBool(KeyUpload, value);
        }

        public static string ReleaseVersion
        {
            get => EditorPrefs.GetString(KeyReleaseVersion, string.Empty);
            set => EditorPrefs.SetString(KeyReleaseVersion, value ?? string.Empty);
        }

        public static string ReleaseNote
        {
            get => EditorPrefs.GetString(KeyReleaseNote, string.Empty);
            set => EditorPrefs.SetString(KeyReleaseNote, value ?? string.Empty);
        }
    }
}
