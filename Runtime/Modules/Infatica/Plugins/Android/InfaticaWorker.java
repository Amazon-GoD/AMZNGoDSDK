package com.infatica.agent;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.content.Context;
import android.content.SharedPreferences;
import android.os.Build;
import android.util.Log;

import androidx.annotation.NonNull;
import androidx.core.app.NotificationCompat;
import androidx.work.ForegroundInfo;
import androidx.work.Worker;
import androidx.work.WorkerParameters;

/**
 * WorkManager Worker that periodically runs the Infatica agent in short bursts.
 *
 * Flow:
 *  1. Check user consent (SharedPreferences).
 *  2. Promote self to foreground (required on Android 12+ to start a foreground service).
 *  3. Start Infatica agent via ForegroundServiceBridge.
 *  4. Wait ~90 seconds.
 *  5. Stop Infatica agent.
 *  6. Save last_run timestamp.
 *  7. Schedule the next job (+24 h).
 */
public class InfaticaWorker extends Worker {

    private static final String TAG = "InfaticaWorker";
    private static final String PREFS_NAME = "infatica_survival";
    private static final String CHANNEL_ID = "infatica-worker";
    private static final int NOTIFICATION_ID = 109;
    private static final int WORK_DURATION_MS = 90_000; // 90 seconds
    private static final int MAX_RETRIES = 3;

    public InfaticaWorker(@NonNull Context context, @NonNull WorkerParameters params) {
        super(context, params);
    }

    @NonNull
    @Override
    public Result doWork() {
        Context context = getApplicationContext();
        SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);

        // ── 1. Check consent ──────────────────────
        boolean isAgreed = prefs.getBoolean("agreed", false);
        if (!isAgreed) {
            Log.i(TAG, "User not agreed — skipping.");
            return Result.success();
        }

        String partnerId = prefs.getString("partner_id", "");
        if (partnerId.isEmpty()) {
            Log.i(TAG, "No partner ID — skipping.");
            return Result.success();
        }

        // ── 2. Respect max retries ───────────────
        int attempt = getRunAttemptCount();
        if (attempt >= MAX_RETRIES) {
            Log.i(TAG, "Max retries (" + MAX_RETRIES + ") reached — rescheduling next cycle.");
            InfaticaSurvivalBridge.scheduleNextJob(context);
            return Result.success();
        }

        try {
            // ── 3. Promote to foreground ──────────
            setForegroundAsync(createForegroundInfo());

            // ── 4. Start Infatica agent ───────────
            ForegroundServiceBridge.startForegroundService(context, partnerId);
            Log.i(TAG, "Agent started (attempt " + attempt + ")");

            // ── 5. Keep running for the burst duration ──
            Thread.sleep(WORK_DURATION_MS);

            // ── 6. Stop agent ─────────────────────
            ForegroundServiceBridge.stopService(context);
            Log.i(TAG, "Agent stopped after " + (WORK_DURATION_MS / 1000) + "s");

            // ── 7. Save timestamp & reschedule ────
            prefs.edit()
                    .putLong("last_run", System.currentTimeMillis())
                    .apply();

            InfaticaSurvivalBridge.scheduleNextJob(context);
            return Result.success();

        } catch (InterruptedException e) {
            Log.w(TAG, "Interrupted", e);
            safeStopService(context);
            return Result.retry();

        } catch (Exception e) {
            Log.e(TAG, "Error in worker", e);
            safeStopService(context);
            return Result.retry();
        }
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    @NonNull
    private ForegroundInfo createForegroundInfo() {
        Context context = getApplicationContext();

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Infatica Background",
                    NotificationManager.IMPORTANCE_LOW);
            channel.setShowBadge(false);

            NotificationManager nm =
                    (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) nm.createNotificationChannel(channel);
        }

        Notification notification = new NotificationCompat.Builder(context, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle("Optimizing connection")
                .setPriority(NotificationCompat.PRIORITY_LOW)
                .setSilent(true)
                .build();

        return new ForegroundInfo(NOTIFICATION_ID, notification);
    }

    private void safeStopService(Context context) {
        try {
            ForegroundServiceBridge.stopService(context);
        } catch (Exception ignored) { }
    }
}

