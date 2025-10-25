using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace AMZNGoDSDK.Editor.SdkDependencies
{
    [InitializeOnLoad]
    public static class DependencyInstaller
    {
        private static bool _installationInProgress = false;
        private static bool _isManualInstallation = false;
        private static ListRequest _listRequest;
        private static readonly Queue<KeyValuePair<string, string>> PackagesToInstall = new();
        
        private static readonly Dictionary<string, string> Dependencies = new()
        {
            {
                "com.google.external-dependency-manager", 
                "https://github.com/googlesamples/unity-jar-resolver.git?path=upm"
            },
            {
                "io.appmetrica.analytics", 
                "https://github.com/appmetrica/appmetrica-unity-plugin.git#v6.7.0"
            }
        };

        static DependencyInstaller()
        {
            EditorApplication.delayCall += () => 
            {
                if (!_installationInProgress)
                {
                    CheckAndInstallDependencies(false);
                }
            };
        }
        
        public static void InstallDependenciesMenu()
        {
            if (_installationInProgress)
            {
                EditorUtility.DisplayDialog("Installation in Progress", 
                    "Dependencies are currently being installed. Please wait.", "OK");
                return;
            }
            
            CheckAndInstallDependencies(true);
        }
        
        public static void CheckStatusMenu() => 
            CheckDependenciesStatus();
        
        public static IReadOnlyDictionary<string, string> GetDependencies() => 
            Dependencies;

        private static void CheckAndInstallDependencies(bool showDialogs = false)
        {
            _installationInProgress = true;
            _isManualInstallation = showDialogs;
            Debug.Log("AMZN GoD: Checking dependencies...");
            
            _listRequest = Client.List();
            EditorApplication.update += ListProgress;
        }

        private static void ListProgress()
        {
            if (_listRequest.IsCompleted)
            {
                EditorApplication.update -= ListProgress;
                
                if (_listRequest.Status == StatusCode.Success)
                {
                    PackagesToInstall.Clear();
                    
                    var installedPackages = _listRequest.Result.ToDictionary(p => p.name, p => p);
                    
                    foreach (var dependency in Dependencies)
                    {
                        string packageName = dependency.Key;
                        string packageUrl = dependency.Value;

                        if (!installedPackages.ContainsKey(packageName))
                        {
                            PackagesToInstall.Enqueue(new KeyValuePair<string, string>(packageName, packageUrl));
                            Debug.Log($"AMZN GoD: {packageName} will be installed");
                        }
                        else
                        {
                            Debug.Log($"AMZN GoD: {packageName} is already installed");
                        }
                    }

                    if (PackagesToInstall.Count > 0)
                    {
                        Debug.Log($"AMZN GoD: Found {PackagesToInstall.Count} packages to install");
                        InstallNextPackage();
                    }
                    else
                    {
                        Debug.Log("AMZN GoD: All dependencies are already installed");
                        _installationInProgress = false;
                        
                        if (_isManualInstallation)
                        {
                            EditorApplication.delayCall += () => 
                            {
                                EditorUtility.DisplayDialog("Installation Complete", 
                                    "All required dependencies are already installed!", "OK");
                            };
                        }
                    }
                }
                else
                {
                    Debug.LogError("AMZN GoD: Failed to list packages: " + _listRequest.Error.message);
                    _installationInProgress = false;
                    
                    if (_isManualInstallation)
                    {
                        EditorApplication.delayCall += () => 
                        {
                            EditorUtility.DisplayDialog("Installation Error", 
                                "Failed to check package dependencies. See console for details.", "OK");
                        };
                    }
                }
            }
        }

        private static void InstallNextPackage()
        {
            if (PackagesToInstall.Count == 0)
            {
                _installationInProgress = false;
                Debug.Log("AMZN GoD: All dependencies installed successfully!");
                
                if (_isManualInstallation)
                {
                    EditorApplication.delayCall += () => 
                    {
                        EditorUtility.DisplayDialog("Installation Complete", 
                            "All dependencies have been installed successfully!", "OK");
                    };
                }
                return;
            }

            var package = PackagesToInstall.Dequeue();
            string packageName = package.Key;
            string packageUrl = package.Value;
            
            Debug.Log($"AMZN GoD: Installing {packageName} from {packageUrl}...");
            
            var addRequest = Client.Add(packageUrl);
            EditorApplication.update += () => AddProgress(addRequest, packageName);
        }

        private static void AddProgress(AddRequest request, string packageName)
        {
            if (request.IsCompleted)
            {
                EditorApplication.update -= () => AddProgress(request, packageName);
                
                if (request.Status == StatusCode.Success)
                {
                    Debug.Log($"AMZN GoD: {packageName} installed successfully");
                }
                else
                {
                    Debug.LogError($"AMZN GoD: Failed to install {packageName}: {request.Error.message}");
                    
                    if (_isManualInstallation)
                    {
                        EditorApplication.delayCall += () => 
                        {
                            EditorUtility.DisplayDialog("Installation Error", 
                                $"Failed to install {packageName}. See console for details.", "OK");
                        };
                    }
                }
                
                InstallNextPackage();
            }
        }

        private static void CheckDependenciesStatus()
        {
            var listRequest = Client.List();
            
            void UpdateHandler()
            {
                if (listRequest.IsCompleted)
                {
                    EditorApplication.update -= UpdateHandler;
                    
                    if (listRequest.Status == StatusCode.Success)
                    {
                        var installedPackages = listRequest.Result.ToDictionary(p => p.name, p => p);
                        var statusMessage = "Dependencies Status:\n\n";

                        foreach (var dependency in Dependencies)
                        {
                            string packageName = dependency.Key;
                            bool isInstalled = installedPackages.ContainsKey(packageName);
                            statusMessage += $"• {packageName}: {(isInstalled ? "✅" : "❌")}\n";
                        }

                        EditorUtility.DisplayDialog("Dependencies Status", statusMessage, "OK");
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", 
                            "Failed to check dependencies status. See console for details.", "OK");
                    }
                }
            }
            
            EditorApplication.update += UpdateHandler;
        }
    }
}