using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace AMZNGoDSDK.Editor
{
    [InitializeOnLoad]
    public static class SdkDependencyManager
    {
        public static bool IsBusy => SdkDependencyBootstrap.IsBusy;
        public static string Status => SdkDependencyBootstrap.Status;
        public static void RequestInstall() => SdkDependencyBootstrap.RequestInstall();
        public static void Retry() => SdkDependencyBootstrap.Retry();
        public static bool CanInstallAutomatically(string define) =>
            define == ModuleDefineManager.APPLOVIN_DEFINE || define == ModuleDefineManager.FIREBASE_DEFINE;
        public static async Task<Dictionary<string, bool>> GetSdkDependenciesInstallInfoAsync()
        {
            var dependenciesInstallInfo = new Dictionary<string, bool>();
            var dependencies = DependencyInstaller.Dependencies;
            
            foreach (var dependency in dependencies)
            {
                bool isInstalled = await DependencyInstaller.IsInstalled(dependency.Key);
                dependenciesInstallInfo[dependency.Key] = isInstalled;
            }
            
            return dependenciesInstallInfo;
        }

        public static void InstallMissingDependencies() => Retry();
    }
}