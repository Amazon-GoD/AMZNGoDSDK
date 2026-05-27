package com.infatica.agent;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.content.pm.ServiceInfo;
import android.os.Build;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.core.app.NotificationCompat;
import androidx.work.ForegroundInfo;
import androidx.work.Worker;
import androidx.work.WorkerParameters;

/**
 * WorkManager watchdog for the Infatica agent.
 *
 * The agent itself runs in the long-lived foreground service from the AAR
 * (com.infatica.agent.service.Service) exactly like the WithoutJobs variant —
 * it must stay up "as long as possible" and is only stopped on user Disagree
 * or by the OS.
 *
 * The ONLY thing WithJobs adds on top of WithoutJobs is the ability to
 * (re)start that service WITHOUT the game being launched: this worker (and
 * InfaticaBootReceiver) just ensure the service is running and never stop it.
 *
 * Flow:
 *  1. Check user consent (SharedPreferences).
 *  2. Promote self to foreground (required on Android 12+ so a background
 *     WorkManager job is allowed to start a foreground service).
 *  3. Ensure the Infatica agent service is running — same call WithoutJobs
 *     makes on every game launch (idempotent on the AAR side).
 *  4. Save last_run timestamp.
 *  5. Reschedule the next watchdog check.
 *
 * NOTE: there is intentionally NO Thread.sleep and NO stopService here —
 * the previous "90 s burst then stop" model contradicted the requirement
 * that the agent stays connected as long as possible.
 */
public class InfaticaWorker extends Worker {

    private static final String TAG = "InfaticaWorker";
    private static final String PREFS_NAME = "infatica_survival";
    private static final String CHANNEL_ID = "infatica-worker";
    private static final int NOTIFICATION_ID = 109;
    private static final int MAX_RETRIES = 3;

    public InfaticaWorker(@NonNull Context context, @NonNull WorkerParameters params) {
        super(context, params);
    }

    @NonNull
    @Override
    public Result doWork() {
        long startMs = System.currentTimeMillis();
        Log.i(TAG, "═══ doWork() START ═══ android="
                + Build.VERSION.SDK_INT + ", attempt=" + getRunAttemptCount());

        Context context = getApplicationContext();
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);

        // ── 1. Check consent ──────────────────────
        boolean isAgreed = prefs.getBoolean("agreed", false);
        Log.d(TAG, "step 1: consent check — agreed=" + isAgreed);
        if (!isAgreed) {
            Log.i(TAG, "User not agreed — skipping. (doWork done in "
                    + (System.currentTimeMillis() - startMs) + " ms)");
            return Result.success();
        }

        String partnerId = prefs.getString("partner_id", "");
        Log.d(TAG, "step 1: partnerId=" + (partnerId.isEmpty() ? "<empty>" : partnerId));
        if (partnerId.isEmpty()) {
            Log.i(TAG, "No partner ID — skipping.");
            return Result.success();
        }

        String appName = prefs.getString("app_name", "");
        Log.d(TAG, "step 1: appName=" + (appName.isEmpty() ? "<empty>" : appName));

        long lastRun = prefs.getLong("last_run", 0L);
        if (lastRun > 0) {
            Log.d(TAG, "step 1: previous last_run was "
                    + ((System.currentTimeMillis() - lastRun) / 1000) + " s ago");
        } else {
            Log.d(TAG, "step 1: no previous last_run (first run)");
        }

        // ── 2. Respect max retries ───────────────
        int attempt = getRunAttemptCount();
        if (attempt >= MAX_RETRIES) {
            Log.w(TAG, "step 2: max retries (" + MAX_RETRIES + ") reached — rescheduling next cycle.");
            InfaticaSurvivalBridge.scheduleNextJob(context);
            return Result.success();
        }

        try {
            // ── 3. Promote to foreground ──────────
            Log.d(TAG, "step 3: setForegroundAsync(...) — promoting worker to FGS (type dataSync on API 29+)");
            setForegroundAsync(createForegroundInfo(appName));

            // ── 4. Ensure the agent service is running ──
            // Same call WithoutJobs makes on every game launch; the AAR
            // service handles being (re)started when already alive.
            Log.d(TAG, "step 4: calling ForegroundServiceBridge.startForegroundService(...)");
            ForegroundServiceBridge.startForegroundService(context, partnerId, appName);
            Log.i(TAG, "step 4: ✓ Agent service ensured running (attempt " + attempt + ")");

            // ── 5. Save timestamp & reschedule watchdog ──
            long nowMs = System.currentTimeMillis();
            prefs.edit()
                    .putLong("last_run", nowMs)
                    .apply();
            Log.d(TAG, "step 5: last_run saved = " + nowMs);

            InfaticaSurvivalBridge.scheduleNextJob(context);

            Log.i(TAG, "═══ doWork() SUCCESS in "
                    + (System.currentTimeMillis() - startMs) + " ms ═══");
            return Result.success();

        } catch (Exception e) {
            // Do NOT stop the service here — it may already be running.
            // Just retry (bounded by MAX_RETRIES above).
            Log.e(TAG, "═══ doWork() FAILED — " + e.getClass().getSimpleName()
                    + ": " + e.getMessage() + " — will Result.retry() ═══", e);
            return Result.retry();
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    @NonNull
    private ForegroundInfo createForegroundInfo(String appName) {
        Context context = getApplicationContext();

        String channelName = (appName != null && !appName.isEmpty()) ? appName : "Background Service";
        String title = (appName != null && !appName.isEmpty())
                ? "Welcome to " + appName
                : "Welcome";

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    channelName,
                    NotificationManager.IMPORTANCE_LOW);
            channel.setShowBadge(false);

            NotificationManager nm =
                    (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.createNotificationChannel(channel);
        }

        Notification notification = new NotificationCompat.Builder(context, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle(title)
                .setPriority(NotificationCompat.PRIORITY_LOW)
                .setSilent(true)
                .build();

        // Android 10+ (API 29) requires a foreground service type; Android 14
        // (API 34) enforces it for WorkManager foreground workers.
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            Log.d(TAG, "createForegroundInfo: id=" + NOTIFICATION_ID
                    + ", channel=" + CHANNEL_ID + ", type=DATA_SYNC");
            return new ForegroundInfo(NOTIFICATION_ID, notification,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
        }
        Log.d(TAG, "createForegroundInfo: id=" + NOTIFICATION_ID
                + ", channel=" + CHANNEL_ID + " (pre-Q, no FGS type)");
        return new ForegroundInfo(NOTIFICATION_ID, notification);
    }

    @Override
    public void onStopped() {
        Log.w(TAG, "onStopped() — WorkManager asked worker to stop "
                + "(constraints lost / cancelled / timeout). Agent service NOT stopped.");
        super.onStopped();
    }
}
