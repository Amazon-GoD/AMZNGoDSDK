using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;

namespace AMZNGoDSDK.Editor.SdkDependencies
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
    }
}