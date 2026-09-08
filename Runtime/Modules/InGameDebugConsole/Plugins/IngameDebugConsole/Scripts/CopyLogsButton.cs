using IngameDebugConsole;
using UnityEngine;
using UnityEngine.UI;

namespace AMZNGoDSDK.Runtime
{
    [RequireComponent(typeof(Button))]
    public class CopyLogsButton : MonoBehaviour
    {
        private void Awake()
        {
            GetComponent<Button>().onClick.AddListener(CopyLogsToClipboard);
        }

        [ConsoleMethod("logs.copy", "Copies all logs to the system clipboard")]
        public static void CopyLogsToClipboard()
        {
            var mgr = DebugLogManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[CopyLogsButton] DebugLogManager not in scene.");
                return;
            }

            string logs = mgr.GetAllLogs();
            Debug.Log($"[CopyLogsButton] GetAllLogs length={logs.Length} chars");
            GUIUtility.systemCopyBuffer = logs;
            string readback = GUIUtility.systemCopyBuffer;
            Debug.Log($"[CopyLogsButton] Clipboard readback length={(readback != null ? readback.Length : -1)} chars");
        }

        [ConsoleMethod("logs.save", "Saves logs to persistentDataPath and returns the file path")]
        public static void SaveLogsToFile()
        {
            var mgr = DebugLogManager.Instance;
            if (mgr == null)
            {
                Debug.LogWarning("[CopyLogsButton] DebugLogManager not in scene.");
                return;
            }

            string path = System.IO.Path.Combine(
                Application.persistentDataPath,
                System.DateTime.Now.ToString("dd-MM-yyyy--HH-mm-ss") + ".txt");
            mgr.SaveLogsToFile(path);
            Debug.Log($"[CopyLogsButton] Logs saved to: {path}");
        }
    }
}
