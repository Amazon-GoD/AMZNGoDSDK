using System;
using System.Collections.Generic;
using System.IO;
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
        public const string ExternalDependencyManagerVersion = "1.2.189";
        private static bool _installationInProgress = false;
        public static bool IsBusy => _installationInProgress;
        
        private static readonly Dictionary<string, string> _dependencies = new()
        {
            {
                "com.google.external-dependency-manager", 
                "https://github.com/googlesamples/unity-jar-resolver.git?path=upm#v" + ExternalDependencyManagerVersion
            }
        };
        
        public static IReadOnlyDictionary<string, string> Dependencies => 
            _dependencies;

        #region DependenciesLoading

        public static async Task InstallRequiredDependenciesAsync()
        {
            try { await EnsureRequiredDependenciesAsync(); }
            catch (Exception ex) { Debug.LogError("[AMZNGoDSDK] Не удалось установить зависимости: " + ex.Message); }
        }

        public static async Task EnsureRequiredDependenciesAsync()
        {
            while (_installationInProgress) await Task.Delay(100);
            _installationInProgress = true;
            try
            {
                var installedPackages = await GetInstalledPackagesAsync();
                var packagesToInstall = new List<KeyValuePair<string, string>>();
                
                foreach (var dependency in Dependencies)
                {
                    string packageName = dependency.Key;
                    string packageUrl = dependency.Value;

                    if (!IsCompatible(installedPackages, packageName))
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
            {
                Debug.Log($"AMZN GoD: {packageName} installed successfully");
                return;
            }

            // Раньше throw стоял без else и срабатывал ДАЖЕ после успешной установки,
            // причём addRequest.Error в этом случае null — падало NullReferenceException,
            // его ловил вызывающий и писал «installation were canceled by error».
            // В цикле по нескольким пакетам это обрывало установку на первом же.
            string error = addRequest.Error != null ? addRequest.Error.message : "неизвестная ошибка";
            throw new Exception($"Failed to install {packageName}: {error}");
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

        internal static Dictionary<string, bool> GetRegisteredDependenciesInstallInfo()
        {
            var packages = PackageInfo.GetAllRegisteredPackages().ToDictionary(package => package.name, package => package);
            return Dependencies.ToDictionary(dependency => dependency.Key, dependency => IsCompatible(packages, dependency.Key));
        }

        public static async Task<bool> AllDependenciesAreInstalled()
        {
            var installedPackages = await GetInstalledPackagesAsync();
            return Dependencies.All(x => IsCompatible(installedPackages, x.Key));
        }
        
        public static async Task<bool> IsInstalled(string packageName)
        {
            try
            {
                var installedPackages = await GetInstalledPackagesAsync();

                return IsCompatible(installedPackages, packageName);
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", 
                    $"Failed to check dependencies status: {ex.Message}", "OK");
                
                return false;
            }
        }
        
        #endregion

        private static bool IsCompatible(Dictionary<string, PackageInfo> packages, string name)
        {
            if (!packages.TryGetValue(name, out var package)) return false;
            if (string.IsNullOrEmpty(package.resolvedPath) || !File.Exists(Path.Combine(package.resolvedPath, "package.json"))) return false;
            if (name != "com.google.external-dependency-manager") return true;
            return Version.TryParse(package.version, out var installed) &&
                   installed >= new Version(ExternalDependencyManagerVersion);
        }
    }
}
