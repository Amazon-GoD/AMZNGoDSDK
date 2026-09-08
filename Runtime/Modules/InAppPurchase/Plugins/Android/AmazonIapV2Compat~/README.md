# AmazonIapV2Compat

Исходник шима `AmazonIapV2Compat.jar` (папка с `~` — Unity её игнорирует, в сборку идёт только сам jar).

## Зачем

Appstore SDK 3.0.2 отдавал класс `com.amazon.android.CrossPlatformPluginUtils` с методом
`notifyActivityVisible(Activity)`. Его вызывает `AmazonIapV2JavaService-1.0.jar` (легаси Unity-плагин
Amazon) сразу после `PurchasingService.registerListener()` — и только после этого выставляет
свой флаг `initialized = true`.

В Appstore SDK 3.0.9 класс удалён. Без шима первый же вызов IAP валится с
`NoClassDefFoundError: com.amazon.android.CrossPlatformPluginUtils` на main-треде,
плагин никогда не переходит в состояние `initialized`, и весь IAP мёртв.

В 3.0.9 `PurchasingService.registerListener()` сам поднимает Appstore SDK
(внутри вызывает тот же init, что и `AmazonAppstoreService.initializeAmazonAppstoreService`),
поэтому шиму остаётся только идемпотентный init-вызов.

## Пересборка

```bash
JDK="C:/Program Files/Unity/Hub/Editor/2022.3.60f1/Editor/Data/PlaybackEngines/AndroidPlayer/OpenJDK/bin"
SDK="C:/Program Files/Unity/Hub/Editor/2022.3.60f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platforms/android-34/android.jar"

"$JDK/javac.exe" -source 8 -target 8 -bootclasspath "$SDK" \
  -cp "$SDK;../amazon-appstore-sdk-3.0.9.jar" -d out CrossPlatformPluginUtils.java
"$JDK/jar.exe" cf ../AmazonIapV2Compat.jar -C out com
```

## Когда шим можно выбросить

Как только C#-биндинг переведут с легаси-плагина (`AmazonIapV2*.jar` + `libAmazonIapV2Bridge.so`,
только 32-бит, Amazon его больше не поддерживает) на прямые вызовы
`com.amazon.device.iap.PurchasingService` через `AndroidJavaClass` / `AndroidJavaProxy`.
