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

    // Method to start the foreground service with a notification
    public static void startForegroundService(Context context) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel nc = new NotificationChannel("infatica-agent", "Infatica Agent", NotificationManager.IMPORTANCE_DEFAULT);
            NotificationManager nm = (NotificationManager) context.getSystemService(Context.NOTIFICATION_SERVICE);
            nm.createNotificationChannel(nc);
        }

        Notification notification = new NotificationCompat.Builder(context, "infatica-agent")
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle("Is running")
                .build();

        Service.Companion.startForeground(context, 108, notification);
    }

    // Method to stop the service
    public static void stopService(Context context) {
        Service.Companion.stop(context);
    }
}
