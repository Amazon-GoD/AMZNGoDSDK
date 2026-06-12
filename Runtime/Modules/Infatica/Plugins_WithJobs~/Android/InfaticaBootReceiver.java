package com.infatica.agent;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

/**
 * Listens for system events (boot completed, power connected) and schedules
 * an Infatica watchdog job when the user has agreed. The job ensures the
 * long-lived agent service is running (idempotent) and never stops it, so the
 * agent comes back after a reboot / process kill without launching the game.
 */
public class InfaticaBootReceiver extends BroadcastReceiver {

    private static final String TAG = "InfaticaBootReceiver";

    @Override
    public void onReceive(Context context, Intent intent) {
        if (intent == null || intent.getAction() == null) {
            Log.w(TAG, "onReceive: null intent/action — ignored");
            return;
        }

        String action = intent.getAction();
        Log.i(TAG, "═══ onReceive: " + action + " ═══");

        boolean isBoot = Intent.ACTION_BOOT_COMPLETED.equals(action);
        boolean isPower = Intent.ACTION_POWER_CONNECTED.equals(action);

        if (!isBoot && !isPower) {
            Log.d(TAG, "Action not handled (expected BOOT_COMPLETED or ACTION_POWER_CONNECTED).");
            return;
        }

        Log.d(TAG, "Event type: " + (isBoot ? "BOOT_COMPLETED" : "POWER_CONNECTED")
                + " — checking conditions...");

        if (InfaticaSurvivalBridge.shouldScheduleFromEvent(context)) {
            Log.i(TAG, "✓ User agreed — scheduling watchdog job in 60 s from event: " + action);
            // Short delay (~1 min); the watchdog job itself just ensures
            // the agent service is running and is idempotent.
            InfaticaSurvivalBridge.scheduleJobWithDelay(context, 60_000L);
        } else {
            Log.i(TAG, "User not agreed (or no consent stored) — skipping schedule from " + action);
        }
    }
}
