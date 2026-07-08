package com.amzngod.exoplayer;

import android.app.Activity;
import android.app.Dialog;
import android.graphics.Color;
import android.graphics.drawable.ColorDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.Looper;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.TextureView;
import android.view.View;
import android.view.ViewGroup;
import android.view.Window;
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
 * {@link TextureView} (wrapped in an {@link AspectRatioFrameLayout} for letterboxing),
 * hosted in its own translucent full-screen {@link Dialog} window on top of the Unity activity.
 *
 * <p><b>Why a separate window.</b> The overlay lives in a dedicated sub-window (the {@code Dialog}),
 * NOT inside Unity's own view hierarchy. Unity renders into a {@code SurfaceView} whose surface is
 * destroyed and recreated when the app returns from the background (e.g. the user locks and unlocks
 * the screen mid-ad). If the overlay shared Unity's window it would lose the surface z-order fight
 * after such a recreation: the game would draw over the (now invisible) overlay, while the overlay's
 * transparent full-screen click target — still on top in the <i>view</i> hierarchy — kept catching
 * taps and opening the promo store. Touch dispatch follows the view tree; drawing follows surface
 * z-order, and the two diverged. A separate window is composited at a higher window layer than the
 * entire activity window, so it stays above Unity's surface no matter how Unity recreates or
 * re-orders its views — drawing and input stay consistent.</p>
 *
 * <p>Video uses a {@code TextureView} (composites like an ordinary view — no separate surface
 * layer, no first-frame composition race).</p>
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
    private AspectRatioFrameLayout videoFrame;
    private TextureView textureView;
    private FrameLayout root;
    private TextView countdownText;
    private View ctaClickCatcher;
    private Button closeButton;

    /** Dedicated window that hosts the overlay above the Unity activity window. */
    private Dialog dialog;

    private Handler handler;
    private Runnable countdownTick;

    private boolean completed;

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
     * Hosts {@code content} in a dedicated translucent, full-screen {@link Dialog} window layered
     * above the Unity activity window. See the class javadoc for why a separate window (rather than
     * attaching into Unity's own view hierarchy) is required to stay reliably on top.
     */
    private void showInOwnWindow(Activity activity, View content) {
        dialog = new Dialog(activity, android.R.style.Theme_Translucent_NoTitleBar_Fullscreen);
        dialog.setContentView(content, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT,
                ViewGroup.LayoutParams.MATCH_PARENT));

        // The ad must be watched: no accidental dismissal via the Back button or an outside tap.
        dialog.setCancelable(false);
        dialog.setCanceledOnTouchOutside(false);

        Window w = dialog.getWindow();
        if (w != null) {
            w.setBackgroundDrawable(new ColorDrawable(Color.TRANSPARENT));
            w.setLayout(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT);
            // Keep the screen on for the duration of the video so it doesn't dim / auto-lock mid-play.
            w.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

            // Mirror the game's system-UI (immersive) state so the status / nav bars don't pop over
            // the video in a full-screen game.
            View gameDecor = activity.getWindow() != null ? activity.getWindow().getDecorView() : null;
            if (gameDecor != null && w.getDecorView() != null) {
                w.getDecorView().setSystemUiVisibility(gameDecor.getSystemUiVisibility());
            }

            // Let the video extend into a display cutout (notch) instead of being letterboxed by it.
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                w.getAttributes().layoutInDisplayCutoutMode =
                        WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
            }
        }

        dialog.show();
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
            // TextureView мог потерять содержимое своей SurfaceTexture. ExoPlayer в состоянии
            // STATE_ENDED новый кадр не отрисовывает, поэтому стоп-кадр пропадает (остаётся
            // только чёрный фон и кнопка закрытия). Перематываем (с playWhenReady=false) к
            // последнему кадру, чтобы плеер декодировал и заново показал его, не возобновляя
            // проигрывание.
            long duration = player.getDuration();
            if (duration > 0) {
                player.setPlayWhenReady(false);
                player.seekTo(Math.max(0, duration - 1));
            }
        });
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

            if (dialog != null) {
                try { dialog.dismiss(); } catch (Exception ignored) {
                }
                dialog = null;
            }
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
