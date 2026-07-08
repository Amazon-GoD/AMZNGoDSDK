package com.amzngod.exoplayer;

import android.app.Activity;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.TextureView;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.TextView;

import com.google.android.exoplayer2.ExoPlayer;
import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.PlaybackException;
import com.google.android.exoplayer2.Player;
import com.google.android.exoplayer2.ui.AspectRatioFrameLayout;
import com.google.android.exoplayer2.video.VideoSize;

import com.unity3d.player.UnityPlayer;

/**
 * Native full-screen cross-promo video overlay built on ExoPlayer rendering into a
 * {@link TextureView} (wrapped in an {@link AspectRatioFrameLayout} for letterboxing), hosted in
 * its own {@code TYPE_APPLICATION_PANEL} window layered above the Unity activity window.
 *
 * <p><b>Why a separate window.</b> Unity renders into a {@code SurfaceView} whose surface is
 * destroyed and recreated when the app returns from the background (screen lock, Home, task switch).
 * If the overlay shared Unity's window it would lose the surface z-order fight after such a
 * recreation: the game would draw over the (now invisible) overlay, while the overlay's transparent
 * full-screen click target — still on top in the <i>view</i> hierarchy — kept catching taps and
 * opening the promo store (touch follows the view tree; drawing follows surface z-order, and the two
 * diverged). A separate window is composited at a higher window layer than the whole activity window,
 * so it stays above Unity's surface regardless of how Unity recreates or re-orders its views.</p>
 *
 * <p><b>Why {@code WindowManager.addView} and not a {@code Dialog}.</b> A {@code Dialog} tears down
 * and recreates its {@code ViewRootImpl} when the activity is stopped (Home / task switch). On that
 * detach→reattach the {@code TextureView}'s {@code SurfaceTexture} is destroyed but not reliably
 * re-created, leaving the video permanently black. A window added via {@code WindowManager} is only
 * hidden/shown across background — the view tree (and the {@code TextureView}) stays attached and its
 * surface is destroyed/recreated on the normal path, exactly like an ordinary view in the activity
 * window.</p>
 *
 * <p>Belt-and-suspenders against a stuck black screen: on resume the video surface is re-asserted,
 * and a watchdog reveals the close button if no frame renders shortly after returning — so the user
 * can never be trapped.</p>
 *
 * <p>All analytics / tracking / redirect / cooldown logic lives on the C# side; this class
 * only forwards user/playback events via {@code UnityPlayer.UnitySendMessage}:</p>
 * <ul>
 *     <li>{@code OnExoOverlayCompleted} — playback reached the end</li>
 *     <li>{@code OnExoOverlayCta} — CTA button clicked</li>
 *     <li>{@code OnExoOverlayClosed} — close button clicked</li>
 *     <li>{@code OnExoOverlayError} — playback error (message in the payload)</li>
 * </ul>
 */
public class CrossPromoExoOverlay implements Player.Listener {

    /** How long after a resume we wait for a rendered frame before revealing the close fail-safe. */
    private static final long RECOVERY_WATCHDOG_MS = 4000L;

    private String unityObjectName;

    private ExoPlayer player;
    private AspectRatioFrameLayout videoFrame;
    private TextureView textureView;
    private FrameLayout root;
    private TextView countdownText;
    private View ctaClickCatcher;
    private Button closeButton;

    /** Window that hosts the overlay above the Unity activity window (null if the fallback was used). */
    private WindowManager windowManager;
    private boolean windowAdded;

    private Handler handler;
    private Runnable countdownTick;

    private boolean completed;
    /** Set by {@link #onRenderedFirstFrame()}; reset before a resume to detect surface recovery. */
    private boolean frameRenderedSinceResume;

    public void init(String objectName) {
        this.unityObjectName = objectName;
    }

    /**
     * @param url             video URL (http/https or any URI ExoPlayer understands)
     * @param ctaText         unused (kept for signature compatibility; the whole screen is the click target)
     * @param ctaDelaySeconds seconds before the full-screen click target becomes active
     * @param startMuted      whether playback starts muted
     */
    public void show(String url, String ctaText, int ctaDelaySeconds, boolean startMuted) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            sendToUnity("OnExoOverlayError", "No current activity");
            return;
        }

        activity.runOnUiThread(() -> {
            try {
                completed = false;
                frameRenderedSinceResume = false;
                handler = new Handler(Looper.getMainLooper());

                player = new ExoPlayer.Builder(activity).build();
                player.addListener(CrossPromoExoOverlay.this);
                player.setRepeatMode(Player.REPEAT_MODE_OFF);
                player.setTrackSelectionParameters(
                        player.getTrackSelectionParameters()
                                .buildUpon()
                                .setForceHighestSupportedBitrate(true)
                                .build()
                );
                if (startMuted) player.setVolume(0f);

                // Video renders into a TextureView (letterboxed by an AspectRatioFrameLayout).
                // See the class javadoc for why a TextureView is used instead of a SurfaceView.
                videoFrame = new AspectRatioFrameLayout(activity);
                videoFrame.setResizeMode(AspectRatioFrameLayout.RESIZE_MODE_FIT);
                videoFrame.setLayoutParams(new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        Gravity.CENTER));

                textureView = new TextureView(activity);
                textureView.setLayoutParams(new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        Gravity.CENTER));
                videoFrame.addView(textureView);
                player.setVideoTextureView(textureView);

                root = new FrameLayout(activity);
                root.setBackgroundColor(Color.BLACK);
                root.addView(videoFrame);

                // Full-screen transparent click target: a tap anywhere opens the promo link.
                // Added directly above the video so the countdown label and the close button
                // (added afterwards) stay on top of it and remain independently tappable.
                ctaClickCatcher = new View(activity);
                ctaClickCatcher.setBackgroundColor(Color.TRANSPARENT);
                ctaClickCatcher.setVisibility(View.GONE);
                ctaClickCatcher.setLayoutParams(new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT));
                ctaClickCatcher.setOnClickListener(v -> sendToUnity("OnExoOverlayCta", ""));
                root.addView(ctaClickCatcher);

                int pad = dp(activity, 12);

                countdownText = new TextView(activity);
                countdownText.setTextColor(Color.WHITE);
                countdownText.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
                countdownText.setPadding(pad, pad, pad, pad);
                countdownText.setShadowLayer(4f, 0f, 0f, Color.BLACK);
                FrameLayout.LayoutParams cdLp = new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        Gravity.TOP | Gravity.END);
                root.addView(countdownText, cdLp);

                closeButton = new Button(activity);
                closeButton.setText("✕");
                closeButton.setTextColor(Color.WHITE);
                closeButton.setBackgroundColor(Color.argb(140, 0, 0, 0));
                closeButton.setVisibility(View.GONE);
                FrameLayout.LayoutParams closeLp = new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        ViewGroup.LayoutParams.WRAP_CONTENT,
                        Gravity.TOP | Gravity.END);
                closeLp.setMargins(pad, pad, pad, pad);
                closeButton.setOnClickListener(v -> {
                    sendToUnity("OnExoOverlayClosed", "");
                    dismiss();
                });
                root.addView(closeButton, closeLp);

                showInOwnWindow(activity, root);

                player.setMediaItem(MediaItem.fromUri(url));
                player.prepare();
                player.setPlayWhenReady(true);

                if (ctaDelaySeconds <= 0) {
                    ctaClickCatcher.setVisibility(View.VISIBLE);
                } else {
                    handler.postDelayed(() -> {
                        if (ctaClickCatcher != null) ctaClickCatcher.setVisibility(View.VISIBLE);
                    }, ctaDelaySeconds * 1000L);
                }

                startCountdown();
            } catch (Exception e) {
                sendToUnity("OnExoOverlayError", e.getMessage() != null ? e.getMessage() : "Unknown error");
            }
        });
    }

    /**
     * Hosts {@code content} in a dedicated {@code TYPE_APPLICATION_PANEL} window layered above the
     * Unity activity window. See the class javadoc for why a separate, persistent window (rather than
     * a {@code Dialog} or an attach into Unity's own view hierarchy) is required.
     *
     * <p>If adding the panel window fails (e.g. an invalid window token on some OEM builds) we fall
     * back to attaching into the activity's content view — this loses the separate-window z-order
     * guarantee but still shows the ad rather than nothing.</p>
     */
    private void showInOwnWindow(Activity activity, View content) {
        // Mirror the game's immersive system-UI state so the status / nav bars don't pop over the video.
        View gameDecor = activity.getWindow() != null ? activity.getWindow().getDecorView() : null;
        if (gameDecor != null) content.setSystemUiVisibility(gameDecor.getSystemUiVisibility());

        // The ad must be watched: swallow the Back key so it can't dismiss the overlay or leak to the game.
        content.setFocusableInTouchMode(true);
        content.setOnKeyListener((v, keyCode, event) -> keyCode == KeyEvent.KEYCODE_BACK);

        try {
            windowManager = activity.getWindowManager();

            WindowManager.LayoutParams lp = new WindowManager.LayoutParams();
            lp.type = WindowManager.LayoutParams.TYPE_APPLICATION_PANEL;
            lp.token = gameDecor != null ? gameDecor.getWindowToken() : null;
            lp.width = WindowManager.LayoutParams.MATCH_PARENT;
            lp.height = WindowManager.LayoutParams.MATCH_PARENT;
            lp.format = PixelFormat.TRANSLUCENT;
            lp.gravity = Gravity.TOP | Gravity.START;
            lp.flags = WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON
                    | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                lp.layoutInDisplayCutoutMode =
                        WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            }

            windowManager.addView(content, lp);
            windowAdded = true;
            content.requestFocus();
        } catch (Exception e) {
            windowManager = null;
            windowAdded = false;
            activity.addContentView(content, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));
        }
    }

    public void setMuted(boolean muted) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null || player == null) return;
        activity.runOnUiThread(() -> {
            if (player != null) player.setVolume(muted ? 0f : 1f);
        });
    }

    /** Пауза воспроизведения (например, когда поверх плеера открылся стор). */
    public void pause() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null || player == null) return;
        activity.runOnUiThread(() -> {
            if (player != null) player.pause();
        });
    }

    /** Возобновление после возврата в приложение. Уже доигранный ролик не перезапускаем. */
    public void resume() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null || player == null) return;
        activity.runOnUiThread(() -> {
            if (player == null) return;

            // Окно оверлея, пока приложение было в фоне, уничтожило свою поверхность и пересоздало
            // её при возврате. Заново привязываем видеоповерхность, чтобы ExoPlayer рисовал в свежую
            // SurfaceTexture, а не оставался чёрным.
            if (textureView != null) player.setVideoTextureView(textureView);

            if (!completed) {
                frameRenderedSinceResume = false;
                player.play();
                armRecoveryWatchdog();
                return;
            }
            // Ролик уже доигран. ExoPlayer в состоянии STATE_ENDED новый кадр не отрисовывает,
            // поэтому стоп-кадр пропадает (остаётся только чёрный фон и кнопка закрытия).
            // Перематываем (с playWhenReady=false) к последнему кадру, чтобы плеер декодировал и
            // заново показал его, не возобновляя проигрывание.
            long duration = player.getDuration();
            if (duration > 0) {
                player.setPlayWhenReady(false);
                player.seekTo(Math.max(0, duration - 1));
            }
        });
    }

    /**
     * Fail-safe: if the video surface never recovers after returning to the app (no rendered frame
     * within {@link #RECOVERY_WATCHDOG_MS}), reveal the close button so the user is never trapped on
     * a black screen.
     */
    private void armRecoveryWatchdog() {
        if (handler == null) return;
        handler.postDelayed(() -> {
            if (root == null || completed) return;
            if (!frameRenderedSinceResume
                    && closeButton != null && closeButton.getVisibility() != View.VISIBLE) {
                closeButton.setVisibility(View.VISIBLE);
                if (ctaClickCatcher != null) ctaClickCatcher.setVisibility(View.VISIBLE);
            }
        }, RECOVERY_WATCHDOG_MS);
    }

    public void dismiss() {
        final Activity activity = UnityPlayer.currentActivity;

        Runnable teardown = () -> {
            if (handler != null && countdownTick != null) {
                handler.removeCallbacks(countdownTick);
            }
            if (handler != null) {
                handler.removeCallbacksAndMessages(null);
            }
            countdownTick = null;
            handler = null;

            if (player != null) {
                if (textureView != null) player.clearVideoTextureView(textureView);
                player.release();
                player = null;
            }
            textureView = null;
            videoFrame = null;

            if (root != null) {
                if (windowAdded && windowManager != null) {
                    try { windowManager.removeViewImmediate(root); } catch (Exception ignored) {
                    }
                } else if (root.getParent() instanceof ViewGroup) {
                    // Fallback attach path.
                    ((ViewGroup) root.getParent()).removeView(root);
                }
            }
            windowAdded = false;
            windowManager = null;
            root = null;
            countdownText = null;
            ctaClickCatcher = null;
            closeButton = null;
        };

        // Tear down on the main thread whether or not the Unity activity is still around.
        if (activity != null) activity.runOnUiThread(teardown);
        else new Handler(Looper.getMainLooper()).post(teardown);
    }

    private void startCountdown() {
        countdownTick = new Runnable() {
            @Override
            public void run() {
                if (player == null || countdownText == null) return;

                long duration = player.getDuration();
                long position = player.getCurrentPosition();
                if (duration > 0) {
                    int remaining = (int) Math.ceil((duration - position) / 1000.0);
                    if (remaining < 0) remaining = 0;
                    countdownText.setText(String.valueOf(remaining));
                }

                if (handler != null && !completed) {
                    handler.postDelayed(this, 250L);
                }
            }
        };
        handler.post(countdownTick);
    }

    private void onPlaybackEnded() {
        if (completed) return;
        completed = true;

        if (countdownText != null) countdownText.setVisibility(View.GONE);
        if (closeButton != null) closeButton.setVisibility(View.VISIBLE);
        if (ctaClickCatcher != null) ctaClickCatcher.setVisibility(View.VISIBLE);

        sendToUnity("OnExoOverlayCompleted", "");
    }

    // --- Player.Listener ---

    @Override
    public void onPlaybackStateChanged(int playbackState) {
        if (playbackState == Player.STATE_ENDED) {
            onPlaybackEnded();
        }
    }

    @Override
    public void onRenderedFirstFrame() {
        frameRenderedSinceResume = true;
    }

    @Override
    public void onVideoSizeChanged(VideoSize videoSize) {
        if (videoFrame == null || videoSize.height == 0) return;
        float ratio = (videoSize.width * videoSize.pixelWidthHeightRatio) / videoSize.height;
        videoFrame.setAspectRatio(ratio);
    }

    @Override
    public void onPlayerError(PlaybackException error) {
        String msg = error.getMessage() != null ? error.getMessage() : "Playback error";
        sendToUnity("OnExoOverlayError", msg);
    }

    private int dp(Activity activity, int value) {
        float density = activity.getResources().getDisplayMetrics().density;
        return Math.round(value * density);
    }

    private void sendToUnity(String method, String param) {
        if (unityObjectName == null) return;
        try {
            UnityPlayer.UnitySendMessage(unityObjectName, method, param);
        } catch (Exception ignored) {
        }
    }
}
