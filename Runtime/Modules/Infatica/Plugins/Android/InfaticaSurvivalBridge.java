package com.infatica.agent;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import androidx.work.BackoffPolicy;
import androidx.work.ExistingWorkPolicy;
import androidx.work.OneTimeWorkRequest;
import androidx.work.WorkManager;

import java.util.concurrent.TimeUnit;

/**
 * Bridge between Unity C# and Android WorkManager for Infatica survival scheduling.
 * Called from C# via AndroidJavaClass("com.infatica.agent.InfaticaSurvivalBridge").
 */
public class InfaticaSurvivalBridge {

    private static final String TAG = "InfaticaSurvival";
    private static final String PREFS_NAME = "infatica_survival";
    private static final String UNIQUE_WORK_NAME = "infatica_periodic";
    // Watchdog re-check cadence. Short enough that a killed agent comes back
    // (requirement: stay connected as long as possible), long enough not to
    // thrash WorkManager. No charging/network constraints — the watchdog must
    // be able to restore the service at any time.
    // TODO: вернуть на 60L * 60 * 1000L (1 час) после тестирования!
    private static final long JOB_INTERVAL_MS = 1L * 60 * 1000L; // TEST: 1 minute

    // ──────────────────────────────────────────────
    // Preferences helpers
    // ──────────────────────────────────────────────

    /**
     * Save user consent + partnerId + appName so that Worker and BootReceiver can use them later.
     */
    public static void saveAgreement(Context context, String partnerId, boolean agreed, String appName) {
        SharedPreferences.Editor editor = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE).edit()
                .putBoolean("agreed", agreed)
                .putString("partner_id", partnerId);
        if (appName != null && !appName.isEmpty()) {
            editor.putString("app_name", appName);
        }
        editor.apply();
        Log.i(TAG, "Saved agreement=" + agreed + ", partnerId=" + partnerId + ", appName=" + appName);
    }

    public static boolean isAgreed(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .getBoolean("agreed", false);
    }

    public static String getPartnerId(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .getString("partner_id", "");
    }

    public static String getAppName(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .getString("app_name", "");
    }

    // ──────────────────────────────────────────────
    // Scheduling
    // ──────────────────────────────────────────────

    /**
     * Schedule the first watchdog check (called after user agrees).
     */
    public static void scheduleJob(Context context) {
        Log.i(TAG, "scheduleJob() called from C# (initial schedule after Agree)");
        scheduleJobWithDelay(context, JOB_INTERVAL_MS);
    }

    /**
     * Schedule the next watchdog check (called from inside the Worker).
     */
    public static void scheduleNextJob(Context context) {
        Log.i(TAG, "scheduleNextJob() called (from Worker, periodic re-schedule)");
        scheduleJobWithDelay(context, JOB_INTERVAL_MS);
    }

    /**
     * Schedule a one-time watchdog check with a custom delay.
     * Uses REPLACE policy → guarantees only 1 job in the queue at any time.
     * No constraints: the watchdog must be able to (re)start the agent at any
     * time (after reboot / process kill), not only on charger + unmetered WiFi.
     */
    public static void scheduleJobWithDelay(Context context, long delayMs) {
        Log.d(TAG, "scheduleJobWithDelay: building OneTimeWorkRequest, delayMs=" + delayMs);

        OneTimeWorkRequest workRequest = new OneTimeWorkRequest.Builder(InfaticaWorker.class)
                .setInitialDelay(delayMs, TimeUnit.MILLISECONDS)
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL,
                        OneTimeWorkRequest.MIN_BACKOFF_MILLIS, TimeUnit.MILLISECONDS)
                .addTag("INFATICA_JOB")
                .build();

        try {
            WorkManager.getInstance(context)
                    .enqueueUniqueWork(UNIQUE_WORK_NAME, ExistingWorkPolicy.REPLACE, workRequest);
            Log.i(TAG, "Enqueued unique work '" + UNIQUE_WORK_NAME + "' (REPLACE) — fires in "
                    + (delayMs / 1000) + " s (~" + (delayMs / 1000 / 60) + " min)");
        } catch (Exception e) {
            // WorkManager.getInstance throws IllegalStateException if androidx.startup
            // provider was stripped by proguard / manifest merger — log clearly.
            Log.e(TAG, "Failed to enqueue work — WorkManager init issue?", e);
        }
    }

    /**
     * Cancel any pending / running infatica job (called when user disagrees).
     */
    public static void cancelJob(Context context) {
        Log.i(TAG, "cancelJob() called (from C# on Disagree)");
        try {
            WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_WORK_NAME);
            Log.i(TAG, "Cancelled unique work '" + UNIQUE_WORK_NAME + "'");
        } catch (Exception e) {
            Log.e(TAG, "Failed to cancel work", e);
        }
    }

    // ──────────────────────────────────────────────
    // Event-based trigger helpers (used by BootReceiver)
    // ──────────────────────────────────────────────

    /**
     * Returns true when the user has agreed. On boot / power events we always
     * want to (re)ensure the agent service — the watchdog job is cheap and
     * idempotent, so there is no "elapsed since last run" gate anymore.
     */
    public static boolean shouldScheduleFromEvent(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        boolean agreed = prefs.getBoolean("agreed", false);
        long lastRun = prefs.getLong("last_run", 0L);
        Log.d(TAG, "shouldScheduleFromEvent: agreed=" + agreed + ", last_run=" + lastRun
                + (lastRun > 0 ? " (" + ((System.currentTimeMillis() - lastRun) / 1000) + " s ago)" : ""));
        return agreed;
    }
}
