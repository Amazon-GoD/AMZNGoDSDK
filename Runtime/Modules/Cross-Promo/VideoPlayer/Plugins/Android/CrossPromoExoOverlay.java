package com.amzngod.exoplayer;

import android.app.Activity;
import android.graphics.Color;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.View;
import android.view.ViewGroup;
import android.view.ViewParent;
import android.view.ViewTreeObserver;
import android.widget.Button;
import android.widget.FrameLayout;
import android.widget.TextView;

import com.google.android.exoplayer2.ExoPlayer;
import com.google.android.exoplayer2.MediaItem;
import com.google.android.exoplayer2.PlaybackException;
import com.google.android.exoplayer2.Player;
import com.google.android.exoplayer2.ui.AspectRatioFrameLayout;
import com.google.android.exoplayer2.ui.StyledPlayerView;

import com.unity3d.player.UnityPlayer;

/**
 * Native full-screen cross-promo video overlay built on ExoPlayer's own
 * {@link StyledPlayerView} (no custom rendering / SurfaceTexture bridge).
 *
 * <p>The overlay is added on top of Unity's view via {@code addContentView}. It shows the
 * video, a CTA button (appears after {@code ctaDelaySeconds}), a close button (appears once
 * the video has finished) and a countdown of the remaining seconds.</p>
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

    private String unityObjectName;

    private ExoPlayer player;
    private StyledPlayerView playerView;
    private FrameLayout root;
    private TextView countdownText;
    private View ctaClickCatcher;
    private Button closeButton;

    private Handler handler;
    private Runnable countdownTick;

    private boolean completed;

    /** Parent the overlay is attached to (decor view, or content view on fallback). */
    private ViewGroup overlayParent;
    /** Keeps the overlay on top if the game re-adds / re-orders its own views. */
    private ViewTreeObserver.OnGlobalLayoutListener keepOnTopListener;

    /**
     * Elevation (in px) applied to the overlay root. Elevation decides the draw order among
     * sibling views on API 21+, so an intentionally huge value guarantees the overlay wins
     * over the game's view regardless of insertion order.
     */
    private static final float OVERLAY_ELEVATION_PX = 1_000_000f;

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

                playerView = new StyledPlayerView(activity);
                playerView.setUseController(false);
                playerView.setResizeMode(AspectRatioFrameLayout.RESIZE_MODE_FIT);
                playerView.setPlayer(player);
                playerView.setLayoutParams(new FrameLayout.LayoutParams(
                        ViewGroup.LayoutParams.MATCH_PARENT,
                        ViewGroup.LayoutParams.MATCH_PARENT));

                root = new FrameLayout(activity);
                root.setBackgroundColor(Color.BLACK);
                root.addView(playerView);

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

                attachOnTop(activity, root);

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
     * Attaches {@code overlay} so it is reliably drawn above the game (and any UI the game
     * renders on top of it). The problem this solves: when the overlay is added as a plain
     * sibling of Unity's view (e.g. via {@code addContentView}) there is no guaranteed draw
     * order, so in many games the in-game UI ends up composited over the overlay.
     *
     * <p>The fix is intentionally universal and permission-free:</p>
     * <ol>
     *     <li>attach to the window's <b>decor view</b> — the top-most container, which sits
     *     above the content view that hosts Unity (falls back to {@code addContentView} if the
     *     decor view is unavailable);</li>
     *     <li>push it to the front and give it a very large {@link View#setElevation elevation}
     *     (elevation wins the z-order among siblings on API 21+);</li>
     *     <li>keep it there: if the game later adds / re-orders its own views, re-assert the
     *     overlay on top (see {@link #keepOnTopListener}).</li>
     * </ol>
     */
    private void attachOnTop(Activity activity, View overlay) {
        ViewGroup parent = null;
        try {
            View decor = activity.getWindow() != null ? activity.getWindow().getDecorView() : null;
            if (decor instanceof ViewGroup) parent = (ViewGroup) decor;
        } catch (Exception ignored) {
        }

        if (parent != null) {
            parent.addView(overlay, new FrameLayout.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));
        } else {
            // Fallback: classic Unity attach point. Still benefits from elevation + bringToFront.
            activity.addContentView(overlay, new ViewGroup.LayoutParams(
                    ViewGroup.LayoutParams.MATCH_PARENT,
                    ViewGroup.LayoutParams.MATCH_PARENT));
            if (overlay.getParent() instanceof ViewGroup) parent = (ViewGroup) overlay.getParent();
        }
        overlayParent = parent;

        bringOverlayToFront(overlay);

        final ViewGroup attachParent = parent;
        if (attachParent != null) {
            // Re-assert on top only when we are not already the last (top-most) child. The guard
            // makes this self-terminating: bringToFront() makes us the last child, so the next
            // layout pass is a no-op and we don't spin in a layout loop.
            keepOnTopListener = () -> {
                if (root == null) return;
                int count = attachParent.getChildCount();
                if (count > 0 && attachParent.getChildAt(count - 1) != root) {
                    bringOverlayToFront(root);
                }
            };
            attachParent.getViewTreeObserver().addOnGlobalLayoutListener(keepOnTopListener);
        }
    }

    /** Forces {@code overlay} to the top of its parent's draw order (front + max elevation). */
    private void bringOverlayToFront(View overlay) {
        if (overlay == null) return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            overlay.setElevation(OVERLAY_ELEVATION_PX);
        }
        overlay.bringToFront();
        ViewParent vp = overlay.getParent();
        if (vp != null) {
            vp.requestLayout();
            if (vp instanceof View) ((View) vp).invalidate();
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
            if (!completed) {
                player.play();
                return;
            }
            // Ролик уже доигран. Пока шёл в фоне (блокировка экрана / открытие стора по CTA),
            // SurfaceView внутри StyledPlayerView уничтожил свою поверхность и пересоздал её
            // при возврате. ExoPlayer в состоянии STATE_ENDED новый кадр в поверхность не
            // отрисовывает, поэтому стоп-кадр пропадает (остаётся только чёрный фон и кнопка
            // закрытия). Перематываем (с playWhenReady=false) к последнему кадру, чтобы плеер
            // декодировал и заново показал его в новой поверхности, не возобновляя проигрывание.
            long duration = player.getDuration();
            if (duration > 0) {
                player.setPlayWhenReady(false);
                player.seekTo(Math.max(0, duration - 1));
            }
        });
    }

    public void dismiss() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;
        activity.runOnUiThread(() -> {
            if (handler != null && countdownTick != null) {
                handler.removeCallbacks(countdownTick);
            }
            if (handler != null) {
                handler.removeCallbacksAndMessages(null);
            }
            countdownTick = null;
            handler = null;

            if (player != null) {
                player.release();
                player = null;
            }
            if (playerView != null) {
                playerView.setPlayer(null);
                playerView = null;
            }
            if (overlayParent != null && keepOnTopListener != null) {
                ViewTreeObserver vto = overlayParent.getViewTreeObserver();
                if (vto.isAlive()) vto.removeOnGlobalLayoutListener(keepOnTopListener);
            }
            keepOnTopListener = null;
            overlayParent = null;

            if (root != null && root.getParent() instanceof ViewGroup) {
                ((ViewGroup) root.getParent()).removeView(root);
            }
            root = null;
            countdownText = null;
            ctaClickCatcher = null;
            closeButton = null;
        });
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
