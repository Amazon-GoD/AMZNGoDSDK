using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AMZNGoDSDK.Editor
{
    internal static class FirebaseABTestsSettingsGUI
    {
        internal static void Draw(FirebaseSettingData settings)
        {
            settings.ABTests ??= new List<Runtime.ABTestEntry>();
            GUILayout.Space(10);
            EditorGUILayout.LabelField("A/B Tests", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Тесты и группы из списка регистрируются при запуске SDK. Save Settings создаёт C# константы. " +
                "В коде задайте обработчики через RegisterABTestFeature и вызовите RunABTest. " +
                "Для чтения группы без обработчиков используйте TryGetABTestGroup.", MessageType.Info);
            EditorGUILayout.HelpBox(
                $"Встроенный флаг Adjust: {Runtime.FirebaseSettingData.AdjustEnableTestId}, строковые группы " +
                $"{Runtime.FirebaseSettingData.AdjustEnabledGroup} / {Runtime.FirebaseSettingData.AdjustDisabledGroup}. " +
                "При включённых Adjust и Remote Config SDK проверяет его до запуска Adjust. " +
                "Регистрация и обработчики встроены: добавлять A/B-тест в этот список не требуется. " +
                "Опубликуйте значение в Firebase Console и полностью перезапустите приложение.\n" +
                "Константы в AMZNGoDSDK.Runtime.FirebaseSettingData: AdjustEnableTestId, AdjustEnabledGroup, AdjustDisabledGroup.",
                MessageType.Info);
            EditorGUILayout.LabelField("Generated Constants File", EditorStyles.miniBoldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.ABTestConstantsPath = EditorGUILayout.TextField(FirebaseABTestConstantsGenerator.OutputPath(settings));
                if (GUILayout.Button("Browse", GUILayout.Width(70)))
                {
                    string selected = EditorUtility.SaveFilePanelInProject("A/B Test Constants", "ABTestConstants", "cs",
                        "Выберите файл вне папок Editor.");
                    if (!string.IsNullOrEmpty(selected)) settings.ABTestConstantsPath = selected;
                }
            }

            for (int i = 0; i < settings.ABTests.Count; i++)
            {
                var test = settings.ABTests[i] ??= new Runtime.ABTestEntry();
                test.GroupNames ??= new List<string>();
                // Migrate the original package's implicit first-group default once, then track it explicitly.
                if (string.IsNullOrEmpty(test.DefaultGroup)) test.DefaultGroup = test.GetDefaultGroup();
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"Test {i + 1}", EditorStyles.boldLabel);
                    test.TestName = EditorGUILayout.TextField(new GUIContent("Test Name", "Имя константы C#: латиница, цифры, '_'."), test.TestName);
                    test.TestId = EditorGUILayout.TextField(new GUIContent("Test ID", "Точное имя параметра в Firebase Remote Config."), test.TestId);
                    EditorGUILayout.LabelField("Groups", EditorStyles.miniBoldLabel);
                    for (int j = 0; j < test.GroupNames.Count; j++)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            string previous = test.GroupNames[j];
                            string updated = EditorGUILayout.TextField($"Group {j + 1}", previous);
                            test.GroupNames[j] = updated;
                            if (test.DefaultGroup == previous) test.DefaultGroup = updated;
                            if (GUILayout.Button("X", GUILayout.Width(22))) test.GroupNames.RemoveAt(j--);
                        }
                    }
                    if (GUILayout.Button("Add Group"))
                    {
                        int suffix = test.GroupNames.Count + 1;
                        while (test.GroupNames.Contains("Group" + suffix)) suffix++;
                        test.GroupNames.Add("Group" + suffix);
                    }
                    if (test.GroupNames.Count > 0)
                    {
                        int selected = test.GroupNames.IndexOf(test.DefaultGroup);
                        int updated = EditorGUILayout.Popup("Default Group", selected, test.GroupNames.ToArray());
                        if (updated >= 0 && updated < test.GroupNames.Count) test.DefaultGroup = test.GroupNames[updated];
                        if (updated < 0) EditorGUILayout.HelpBox("Выберите контрольную группу.", MessageType.Warning);
                    }
                    if (GUILayout.Button("Remove Test")) settings.ABTests.RemoveAt(i--);
                }
            }
            if (GUILayout.Button("Add A/B Test"))
            {
                int suffix = settings.ABTests.Count + 1;
                while (settings.ABTests.Exists(t => t != null && (t.TestName == "Test" + suffix || t.TestId == "test_" + suffix))) suffix++;
                settings.ABTests.Add(new Runtime.ABTestEntry { TestName = "Test" + suffix, TestId = "test_" + suffix, DefaultGroup = "GroupA" });
            }
            if (!FirebaseABTestConstantsGenerator.Validate(settings, out var error))
                EditorGUILayout.HelpBox(error, MessageType.Error);
        }
    }
}
