package com.infatica.agent;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.os.Build;
import android.content.Context;
import android.util.Log;
import androidx.core.app.NotificationCompat;
import com.infatica.agent.service.Service;

public class ForegroundServiceBridge {

    private static final String TAG = "InfaticaFG";
    private static final String CHANNEL_ID = "infatica-agent";
    private static final int NOTIFICATION_ID = 108;

    // Method to request battery optimization permission
    public static void askIgnoreBatteryOptimizations(Context context) {
        Log.i(TAG, "askIgnoreBatteryOptimizations() called (api=" + Build.VERSION.SDK_INT + ")");
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            try {
                Service.Companion.askIgnoreBatteryOptimizations(context);
                Log.d(TAG, "askIgnoreBatteryOptimizations: native call returned");
            } catch (Exception e) {
                Log.e(TAG, "askIgnoreBatteryOptimizations: native call FAILED", e);
            }
        } else {
            Log.d(TAG, "askIgnoreBatteryOptimizations: skipped (pre-M)");
        }
    }

    // Check if the app is ignoring battery optimizations
    public static boolean isIgnoringBatteryOptimizations(Context context) {
        if (context == null) {
            Log.w(TAG, "isIgnoringBatteryOptimizations: null context — returning false");
            return false;
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            boolean res = Service.Companion.isIgnoringBatteryOptimizations(context);
            Log.d(TAG, "isIgnoringBatteryOptimizations: " + res);
            return res;
        }
        return true; // pre-Marshmallow devices don't have battery optimizations
    }

    // Method to start the foreground service with a notification
    public static void startForegroundService(Context context, String partnerId, String appName) {
        Log.i(TAG, "startForegroundService() called — partnerId=" + partnerId
                + ", appName=" + (appName == null ? "null" : appName)
                + ", api=" + Build.VERSION.SDK_INT);

        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            String channelName = (appName != null && !appName.isEmpty()) ? appName : "Background Service";
            NotificationChannel nc = new NotificationChannel(
                CHANNEL_ID,
                channelName,
                NotificationManager.IMPORTANCE_DEFAULT
            );
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            if (nm != null) {
                nm.createNotificationChannel(nc);
                Log.d(TAG, "Notification channel ensured: id=" + CHANNEL_ID + ", name=" + channelName);
            } else {
                Log.w(TAG, "NotificationManager is null — channel NOT created");
            }
        }

        String title = (appName != null && !appName.isEmpty())
            ? "Welcome to " + appName
            : "Welcome";

        Notification notification = new NotificationCompat.Builder(context, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(title)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build();

        Log.d(TAG, "Calling Service.Companion.startForeground(id=" + NOTIFICATION_ID + ", title=\"" + title + "\")");
        try {
            Service.Companion.startForeground(context, NOTIFICATION_ID, notification, partnerId);
            Log.i(TAG, "✓ Service.Companion.startForeground returned — agent service should be running");
        } catch (Exception e) {
            Log.e(TAG, "✗ Service.Companion.startForeground FAILED", e);
            throw e;
        }
    }

    // Method to stop the service
    public static void stopService(Context context) {
        Log.i(TAG, "stopService() called");
        try {
            Service.Companion.stop(context);
            Log.i(TAG, "✓ Service.Companion.stop returned — agent service stop requested");
        } catch (Exception e) {
            Log.e(TAG, "✗ Service.Companion.stop FAILED", e);
        }
    }
}
