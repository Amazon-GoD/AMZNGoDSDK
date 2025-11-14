using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AMZNGoDSDK.Editor
{
    public static class DependencyInstaller
    {
        private static bool _installationInProgress = false;
        
        private static readonly Dictionary<string, string> _dependencies = new()
        {
            {
                "com.google.external-dependency-manager", 
                "https://github.com/googlesamples/unity-jar-resolver.git?path=upm"
            }
        };
        
        public static IReadOnlyDictionary<string, string> Dependencies => 
            _dependencies;

        #region DependenciesLoading

        public static async Task InstallRequiredDependenciesAsync()
        {
            if (_installationInProgress)
            {
                EditorUtility.DisplayDialog("Installation in Progress", 
                    "Dependencies are currently being installed. Please wait.", "OK");
                return;
            }
            
            _installationInProgress = true;
            try
            {
                var installedPackages = await GetInstalledPackagesAsync();
                var packagesToInstall = new List<KeyValuePair<string, string>>();
                
                foreach (var dependency in Dependencies)
                {
                    string packageName = dependency.Key;
                    string packageUrl = dependency.Value;

                    if (!installedPackages.ContainsKey(packageName)) 
                        packagesToInstall.Add(new KeyValuePair<string, string>(packageName, packageUrl));
                }

                if (packagesToInstall.Count > 0)
                {
                    foreach (var package in packagesToInstall)
                        await DownloadPackageFromGitAsync(package.Key, package.Value);
                    
                    Debug.Log("Installation successful. All dependencies have been installed successfully!");
                }
                else
                {
                    Debug.Log("Amzn GoD SDK: All required dependencies are already installed!");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError(
                    $"Amzn GoD SDK: Dependencies installation were canceled by error: {ex.Message}");
            }
            finally
            {
                _installationInProgress = false;
            }
        }

        private static async Task<Dictionary<string, PackageInfo>> GetInstalledPackagesAsync()
        {
            var listRequest = Client.List();
            await WaitForRequestAsync(listRequest);
            
            if (listRequest.Status == StatusCode.Success)
                return listRequest.Result.ToDictionary(p => p.name, p => p);
                
            throw new Exception($"Failed to list packages: {listRequest.Error.message}");
        }

        private static async Task DownloadPackageFromGitAsync(string packageName, string packageUrl)
        {
            var addRequest = Client.Add(packageUrl);
            await WaitForRequestAsync(addRequest);
            
            if (addRequest.Status == StatusCode.Success)
                Debug.Log($"AMZN GoD: {packageName} installed successfully");
                
            throw new Exception($"Failed to install {packageName}: {addRequest.Error.message}");
        }
        
        public static async Task InstallDependency(string dependency)
        {
            var packageUrl = Dependencies[dependency];
            
            await  DownloadPackageFromGitAsync(dependency, packageUrl);
        }

        private static async Task WaitForRequestAsync(Request request)
        {
            while (!request.IsCompleted)
            {
                await Task.Delay(100);
            }
        }

        #endregion
        
        #region Status

        public static async Task<bool> AllDependenciesAreInstalled()
        {
            var installedPackages = await GetInstalledPackagesAsync();
            return Dependencies.All(x => installedPackages.ContainsKey(x.Key));
        }
        
        public static async Task<bool> IsInstalled(string packageName)
        {
            try
            {
                var installedPackages = await GetInstalledPackagesAsync();

                return installedPackages.ContainsKey(packageName);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", 
                    $"Failed to check dependencies status: {ex.Message}", "OK");
                
                return false;
            }
        }
        
        #endregion
    }
}