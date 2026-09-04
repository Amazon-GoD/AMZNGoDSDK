package com.amzngod.exoplayer;

import android.content.Context;
import android.net.Uri;
import android.util.Log;

import com.google.android.exoplayer2.C;
import com.google.android.exoplayer2.database.StandaloneDatabaseProvider;
import com.google.android.exoplayer2.upstream.DataSpec;
import com.google.android.exoplayer2.upstream.DefaultDataSource;
import com.google.android.exoplayer2.upstream.cache.CacheDataSource;
import com.google.android.exoplayer2.upstream.cache.CacheKeyFactory;
import com.google.android.exoplayer2.upstream.cache.CacheWriter;
import com.google.android.exoplayer2.upstream.cache.ContentMetadata;
import com.google.android.exoplayer2.upstream.cache.LeastRecentlyUsedCacheEvictor;
import com.google.android.exoplayer2.upstream.cache.SimpleCache;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Общий дисковый кэш кросс-промо роликов: одна {@link SimpleCache} на процесс, из которой
 * И читает плеер ({@link CrossPromoExoOverlay}), И в которую пишет фоновая докачка
 * ({@link #preload}). Это главное требование конструкции: если бы плеер и докачка работали
 * с разными кэшами (или выводили разный ключ), докачка была бы впустую — плеер бы её не нашёл.
 *
 * <p>Ключ кэша — дефолтный (полный URI). Кастомный ключ здесь не нужен и вреден: ссылка
 * приходит из статичного JSON-конфига, и на обеих сторонах это побайтово одна и та же строка
 * (см. {@code CrossPromoModule.ResolvePromoUrl} и {@code CrossPromoExoNativeOverlay.ResolveUrl}).
 * А отбрасывание query сделало бы {@code promo.mp4?v=2} неотличимым от {@code promo.mp4} —
 * замена креатива на том же пути никогда бы не доехала до устройства.
 *
 * <p>Upstream — {@link DefaultDataSource}, а не http-only источник: этой же фабрикой плеер
 * открывает и локальные креативы из streamingAssets ({@code jar:file://...}).
 *
 * <p>Кэш — оптимизация, а не зависимость. Если поднять его не удалось (нет места, папка
 * залочена другим процессом, повреждённый индекс), {@link #factory} отдаёт {@code null},
 * и вызывающий обязан деградировать на дефолтный DataSource: показ рекламы важнее
 * мгновенного старта.
 */
public final class CrossPromoExoCache {

    private static final String TAG = "CrossPromoExoCache";

    /** LRU-потолок дискового кэша. Креативы — единицы мегабайт, в ротации 3–5 штук. */
    private static final long MAX_BYTES = 50L * 1024 * 1024;

    /** Одна фоновая нить: докачки сериализованы и никогда не трогают main-поток. */
    private static final ExecutorService EXEC = Executors.newSingleThreadExecutor();

    /**
     * ОДИН на процесс: второй SimpleCache на ту же директорию кидает исключение.
     * volatile — чтобы {@link #isCached} мог прочитать поле без {@code synchronized} и не
     * ждать фоновую инициализацию (см. там же).
     */
    private static volatile SimpleCache cache;

    /** Кэш поднять не удалось — повторно не долбимся (конструктор SimpleCache недешёвый). */
    private static boolean cacheUnavailable;

    private CrossPromoExoCache() {
    }

    /** @return общий кэш, либо {@code null}, если поднять его не удалось. */
    private static synchronized SimpleCache cache(Context ctx) {
        if (cache != null) return cache;
        if (cacheUnavailable) return null;

        try {
            File dir = new File(ctx.getCacheDir(), "crosspromo-video");
            cache = new SimpleCache(
                    dir,
                    new LeastRecentlyUsedCacheEvictor(MAX_BYTES),
                    new StandaloneDatabaseProvider(ctx));
        } catch (Throwable t) {
            cacheUnavailable = true;
            Log.w(TAG, "cache init failed, playback falls back to no-cache: " + t);
        }
        return cache;
    }

    /**
     * Общая фабрика для плеера и для докачки — один кэш, один ключ.
     *
     * @return {@code null}, если кэш недоступен; вызывающий обязан деградировать на дефолт.
     */
    public static CacheDataSource.Factory factory(Context ctx) {
        SimpleCache c = cache(ctx);
        if (c == null) return null;

        return new CacheDataSource.Factory()
                .setCache(c)
                .setUpstreamDataSourceFactory(new DefaultDataSource.Factory(ctx))
                .setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR);
    }

    /**
     * Фоновая докачка ролика в кэш. Вызывается из C# как {@code CallStatic("preload", url)};
     * вызывающий поток не блокирует. Не-http ссылки игнорирует — локальным файлам кэш не нужен.
     */
    public static void preload(final String url) {
        if (url == null || !(url.startsWith("http://") || url.startsWith("https://"))) return;

        Context activity = UnityPlayer.currentActivity;
        if (activity == null) return;
        final Context ctx = activity.getApplicationContext();

        EXEC.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    CacheDataSource.Factory f = factory(ctx);
                    if (f == null) return;   // кэш недоступен — показ отработает стримом

                    DataSpec spec = new DataSpec.Builder()
                            .setUri(Uri.parse(url))
                            .setLength(C.LENGTH_UNSET)
                            .build();

                    // createDataSourceForDownloading() сам добавляет FLAG_BLOCK_ON_CACHE и
                    // PRIORITY_DOWNLOAD. cache() блокирующий — потому и на EXEC.
                    new CacheWriter(f.createDataSourceForDownloading(), spec, null, null).cache();
                    Log.i(TAG, "preloaded: " + url);
                } catch (Throwable t) {
                    Log.w(TAG, "preload failed: " + t);
                }
            }
        });
    }

    /**
     * Лежит ли ролик в кэше целиком. Предназначено для опроса с main-потока (тестовые сборки
     * гейтят по нему кнопку показа), поэтому здесь два намеренных ограничения:
     *
     * <ul>
     *   <li>кэш НЕ поднимается — если он ещё не инициализирован, честно отвечаем {@code false}.
     *       Иначе первый же опрос повесил бы main-поток на восстановлении индекса SimpleCache;</li>
     *   <li>поле читается без {@code synchronized} — иначе опрос вставал бы в очередь за
     *       фоновым потоком, который в этот момент как раз конструирует кэш.</li>
     * </ul>
     *
     * <p>Ключ считаем тем же {@link CacheKeyFactory#DEFAULT}, что и фабрика в {@link #factory}:
     * разойдись они — и ответ был бы стабильно ложным.
     */
    public static boolean isCached(String url) {
        SimpleCache c = cache;
        if (c == null || url == null) return false;

        try {
            DataSpec spec = new DataSpec.Builder().setUri(Uri.parse(url)).build();
            String key = CacheKeyFactory.DEFAULT.buildCacheKey(spec);
            long length = ContentMetadata.getContentLength(c.getContentMetadata(key));
            return length != C.LENGTH_UNSET && c.isCached(key, 0, length);
        } catch (Throwable t) {
            return false;
        }
    }
}
