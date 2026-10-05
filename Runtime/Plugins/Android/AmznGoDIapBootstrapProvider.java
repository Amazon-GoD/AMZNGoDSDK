package com.amzngod.sdk.iap;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.util.Log;

import java.lang.reflect.InvocationHandler;
import java.lang.reflect.Method;
import java.lang.reflect.Proxy;

/**
 * Boots the Amazon Appstore SDK at process start, before the first Activity is created.
 *
 * WHY THIS EXISTS
 * PurchasingService.registerListener() calls com.amazon.android.a.a(Context), which installs
 * Application.registerActivityLifecycleCallbacks exactly once. Those callbacks are how the
 * Appstore SDK's ForegroundTaskPipeline learns that the app has a visible UI, and it refuses
 * to launch PurchaseActivity (the purchase dialog) until it does. Lifecycle callbacks only
 * observe events that happen AFTER they are installed.
 *
 * The Unity side of this SDK boots IAP from InAppPurchaseModule.Initialize(), several seconds
 * after the activity has already resumed, and C# cannot win that race: the Unity engine itself
 * starts after onResume. The pipeline therefore considers the UI invisible for the whole
 * session, every purchase receives its intent from the Appstore and is silently parked in a
 * pending queue. On the device this looks like "the button does nothing".
 *
 * ContentProvider.onCreate runs during process initialization, before Application.onCreate and
 * before any Activity, so the callbacks are installed in time to observe the first onResume.
 *
 * WHY REFLECTION
 * Appstore SDK classes live inside the InAppPurchase module folder and disappear with it when
 * the module is disabled. A direct reference would break builds with IAP turned off, and moving
 * this provider into the module folder would trade that for a worse failure: the class would be
 * gone while the manifest entry remained, and the app would not start at all. Loaded by name,
 * this provider always exists and simply does nothing when the Appstore SDK is absent.
 *
 * A no-op listener is registered here on purpose. The real one is installed by the plugin's
 * native bridge when the module initializes, replacing ours; no IAP request can happen in
 * between, because nothing is there to issue one until the module is up. Registering twice is
 * safe: the SDK bootstrap is idempotent (double-checked guard inside com.amazon.android.a.a).
 */
public final class AmznGoDIapBootstrapProvider extends ContentProvider {

    private static final String TAG = "AmznGoDIapBootstrap";
    private static final String LISTENER_CLASS = "com.amazon.device.iap.PurchasingListener";
    private static final String SERVICE_CLASS = "com.amazon.device.iap.PurchasingService";

    @Override
    public boolean onCreate() {
        Context context = getContext();
        if (context == null) {
            Log.w(TAG, "Context is null, bootstrap skipped");
            return true;
        }

        try {
            ClassLoader loader = AmznGoDIapBootstrapProvider.class.getClassLoader();
            Class<?> listenerClass = Class.forName(LISTENER_CLASS, true, loader);
            Class<?> serviceClass = Class.forName(SERVICE_CLASS, true, loader);

            Object listener = Proxy.newProxyInstance(
                    loader, new Class<?>[]{listenerClass}, new NoOpHandler());

            Method register = serviceClass.getMethod(
                    "registerListener", Context.class, listenerClass);
            register.invoke(null, context.getApplicationContext(), listener);

            Log.i(TAG, "Appstore SDK bootstrapped before first activity resume");
        } catch (ClassNotFoundException e) {
            // InAppPurchase module is disabled, so the Appstore SDK is not in this build.
            Log.i(TAG, "Appstore SDK is not present in this build, bootstrap skipped");
        } catch (Throwable t) {
            // A failed bootstrap must never take the app down with it: purchases would merely
            // fall back to the previous behaviour, everything else keeps working.
            Log.w(TAG, "Bootstrap failed, purchases may not show the Amazon dialog: " + t);
        }

        return true;
    }

    /**
     * All four PurchasingListener methods return void, so returning null is correct. Object
     * methods are handled explicitly: a null from hashCode would blow up on unboxing to int,
     * and a listener can plausibly end up inside a collection.
     */
    private static final class NoOpHandler implements InvocationHandler {
        @Override
        public Object invoke(Object proxy, Method method, Object[] args) {
            String name = method.getName();
            if ("hashCode".equals(name)) {
                return System.identityHashCode(proxy);
            }
            if ("equals".equals(name)) {
                return args != null && args.length == 1 && proxy == args[0];
            }
            if ("toString".equals(name)) {
                return "AmznGoDNoOpPurchasingListener";
            }
            return null;
        }
    }

    // --- Used purely as an early entry point; this provider serves no data. ---

    @Override
    public Cursor query(Uri uri, String[] projection, String selection,
                        String[] selectionArgs, String sortOrder) {
        return null;
    }

    @Override
    public String getType(Uri uri) {
        return null;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        return null;
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        return 0;
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        return 0;
    }
}
