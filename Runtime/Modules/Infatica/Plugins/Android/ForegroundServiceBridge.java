package com.infatica.agent;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.os.Build;
import android.content.Context;
import androidx.core.app.NotificationCompat;
import com.infatica.agent.service.Service;

public class ForegroundServiceBridge {

    // Method to request battery optimization permission
    public static void askIgnoreBatteryOptimizations(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            Service.Companion.askIgnoreBatteryOptimizations(context);
        }
    }

    // Check if the app is ignoring battery optimizations
    public static boolean isIgnoringBatteryOptimizations(Context context) {
        if (context == null) return false;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            return Service.Companion.isIgnoringBatteryOptimizations(context);
        }
        return true; // pre-Marshmallow devices don't have battery optimizations
    }

    // Method to start the foreground service with a notification
    public static void startForegroundService(Context context, String partnerId, String appName) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            String channelName = (appName != null && !appName.isEmpty()) ? appName : "Background Service";
            NotificationChannel nc = new NotificationChannel(
                "infatica-agent",
                channelName,
                NotificationManager.IMPORTANCE_DEFAULT
            );
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            nm.createNotificationChannel(nc);
        }

        String title = (appName != null && !appName.isEmpty())
            ? "Welcome to " + appName
            : "Welcome";

        Notification notification = new NotificationCompat.Builder(context, "infatica-agent")
            .setSmallIcon(android.R.drawable.ic_dialog_info)
            .setContentTitle(title)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build();
        Service.Companion.startForeground(context, 108, notification, partnerId);
    }

    // Method to stop the service
    public static void stopService(Context context) {
        Service.Companion.stop(context);
    }
}
