using System.Runtime.CompilerServices;

// EditMode-тесты пакета (Tests/Editor) проверяют internal-логику редактора напрямую:
// build-гарды и конвертеры настроек. Сборка тестов компилируется только в проекте,
// где пакет указан в "testables" манифеста.
[assembly: InternalsVisibleTo("AMZNGoDSDK.Editor.Tests")]
