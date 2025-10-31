using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace AMZNGoDSDK.Editor
{
    [InitializeOnLoad]
    public static class SdkDependencyManager
    {
        static SdkDependencyManager()
        {
            EditorApplication.delayCall += async () => 
            {
                if(await DependencyInstaller.AllDependenciesAreInstalled() == false)
                    await DependencyInstaller.InstallRequiredDependenciesAsync();
            };
        }
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

        public static async void InstallMissingDependencies()
        {
            var dependenciesToInstall = (await GetSdkDependenciesInstallInfoAsync()).
                    Where(x => x.Value == false)
                    .Select(x => x.Key);
            
            foreach (var dependency in dependenciesToInstall)
                await DependencyInstaller.InstallDependency(dependency);
        }
    }
}