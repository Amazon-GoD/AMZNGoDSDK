package com.infatica.agent;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

/**
 * Listens for system events (boot completed, power connected) and
 * schedules an Infatica burst job if conditions are met:
 *   - user has agreed
 *   - ≥ 24 h since last successful run
 *
 * The job itself is a OneTimeWorkRequest with charging + WiFi constraints,
 * so even if we schedule it here, it will only execute when conditions match.
 */
public class InfaticaBootReceiver extends BroadcastReceiver {

    private static final String TAG = "InfaticaBootReceiver";

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null || intent.getAction() == null) return;

        String action = intent.getAction();
        Log.i(TAG, "Received: " + action);

        boolean isBoot = Intent.ACTION_BOOT_COMPLETED.equals(action);
        boolean isPower = Intent.ACTION_POWER_CONNECTED.equals(action);

        if (isBoot || isPower) {
            if (InfaticaSurvivalBridge.shouldScheduleFromEvent(context)) {
                Log.i(TAG, "Scheduling job from event: " + action);
                // Short delay (1 min) — real constraints (charging + WiFi) still apply
                InfaticaSurvivalBridge.scheduleJobWithDelay(context, 60_000L);
            } else {
                Log.i(TAG, "Conditions not met — skipping schedule.");
            }
        }
    }
}

