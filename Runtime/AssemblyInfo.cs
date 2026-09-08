using System.Runtime.CompilerServices;

// Фасад ядра (AmznGoDSDKCore) вынесен в отдельную сборку AMZNGoDSDK.Core
// (Runtime/Core): базовая сборка не может ссылаться на модули, а фасад обязан.
// Фасад при этом остаётся частью SDK — ему доступны internal-члены базовой
// сборки (ModuleBase.Initialized и т.п.).
[assembly: InternalsVisibleTo("AMZNGoDSDK.Core")]
