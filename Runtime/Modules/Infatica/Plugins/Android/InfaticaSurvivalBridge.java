package com.infatica.agent;

import android.content.Context;
import android.content.SharedPreferences;
import android.util.Log;

import androidx.work.BackoffPolicy;
import androidx.work.Constraints;
import androidx.work.ExistingWorkPolicy;
import androidx.work.NetworkType;
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
    private static final long JOB_INTERVAL_MS = 1 * 60 * 1000L; // 24 hours

    // ──────────────────────────────────────────────
    // Preferences helpers
    // ──────────────────────────────────────────────

    /**
     * Save user consent + partnerId so that Worker and BootReceiver can use them later.
     */
    public static void saveAgreement(Context context, String partnerId, boolean agreed) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        prefs.edit()
                .putBoolean("agreed", agreed)
                .putString("partner_id", partnerId)
                .apply();
        Log.i(TAG, "Saved agreement=" + agreed + ", partnerId=" + partnerId);
    }

    public static boolean isAgreed(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .getBoolean("agreed", false);
    }

    public static String getPartnerId(Context context) {
        return context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                .getString("partner_id", "");
    }

    // ──────────────────────────────────────────────
    // Scheduling
    // ──────────────────────────────────────────────

    /**
     * Schedule the first job (called after user agrees).
     * Delay = 24 h, constraints = charging + unmetered WiFi.
     */
    public static void scheduleJob(Context context) {
        scheduleJobWithDelay(context, JOB_INTERVAL_MS);
    }

    /**
     * Schedule the next job (called from inside the Worker after a successful run).
     */
    public static void scheduleNextJob(Context context) {
        scheduleJobWithDelay(context, JOB_INTERVAL_MS);
    }

    /**
     * Schedule a one-time job with a custom delay.
     * Uses REPLACE policy → guarantees only 1 job in the queue at any time.
     */
    public static void scheduleJobWithDelay(Context context, long delayMs) {
        Constraints constraints = new Constraints.Builder()
                .setRequiresCharging(true)
                .setRequiredNetworkType(NetworkType.UNMETERED)
                .build();

        OneTimeWorkRequest workRequest = new OneTimeWorkRequest.Builder(InfaticaWorker.class)
                .setInitialDelay(delayMs, TimeUnit.MILLISECONDS)
                .setConstraints(constraints)
                .setBackoffCriteria(BackoffPolicy.EXPONENTIAL,
                        OneTimeWorkRequest.MIN_BACKOFF_MILLIS, TimeUnit.MILLISECONDS)
                .addTag("INFATICA_JOB")
                .build();

        WorkManager.getInstance(context)
                .enqueueUniqueWork(UNIQUE_WORK_NAME, ExistingWorkPolicy.REPLACE, workRequest);

        Log.i(TAG, "Scheduled job with delay: " + (delayMs / 1000 / 60) + " min");
    }

    /**
     * Cancel any pending / running infatica job (called when user disagrees).
     */
    public static void cancelJob(Context context) {
        WorkManager.getInstance(context).cancelUniqueWork(UNIQUE_WORK_NAME);
        Log.i(TAG, "Cancelled infatica jobs");
    }

    // ──────────────────────────────────────────────
    // Event-based trigger helpers (used by BootReceiver)
    // ──────────────────────────────────────────────

    /**
     * Returns true when:
     *  - user has agreed
     *  - AND ≥ 24 h have passed since the last successful run
     */
    public static boolean shouldScheduleFromEvent(Context context) {
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
        if (!prefs.getBoolean("agreed", false)) return false;

        long lastRun = prefs.getLong("last_run", 0);
        long elapsed = System.currentTimeMillis() - lastRun;
        return elapsed >= JOB_INTERVAL_MS;
    }
}

