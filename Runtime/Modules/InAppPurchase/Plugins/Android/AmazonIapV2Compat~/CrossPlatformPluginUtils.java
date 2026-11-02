package com.amazon.android;

import android.app.Activity;

/**
 * Compatibility shim for the legacy AmazonIapV2 Unity plugin.
 *
 * Appstore SDK 3.0.2 exposed com.amazon.android.CrossPlatformPluginUtils.notifyActivityVisible(Activity),
 * which AmazonIapV2JavaService-1.0.jar calls right after PurchasingService.registerListener().
 * The class was removed in Appstore SDK 3.0.9; without it the plugin dies with
 * NoClassDefFoundError on the main thread before it can mark itself initialized.
 *
 * In 3.0.9 the Appstore SDK bootstraps itself from PurchasingService.registerListener(), so the
 * only thing left to do here is the idempotent init call that replaced the old visibility ping.
 */
public final class CrossPlatformPluginUtils {

    private CrossPlatformPluginUtils() {
    }

    public static void notifyActivityVisible(final Activity activity) {
        if (activity == null) {
            return;
        }
        try {
            AmazonAppstoreService.initializeAmazonAppstoreService(activity);
        } catch (Throwable ignored) {
            // The Appstore SDK is already initialized by registerListener(); never break IAP init here.
        }
    }
}
