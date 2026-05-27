package com.amzngod.media3;

import android.graphics.SurfaceTexture;
import android.opengl.GLES11Ext;
import android.opengl.GLES20;
import android.view.Surface;

import androidx.media3.common.MediaItem;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.Player;
import androidx.media3.common.VideoSize;
import androidx.media3.exoplayer.ExoPlayer;

import com.unity3d.player.UnityPlayer;

public class Media3VideoPlugin implements Player.Listener, SurfaceTexture.OnFrameAvailableListener {

    private ExoPlayer player;
    private SurfaceTexture surfaceTexture;
    private Surface surface;
    private int glTextureId = -1;
    private String unityObjectName;
    private volatile boolean frameAvailable;
    private int videoWidth;
    private int videoHeight;
    private boolean isPrepared;
    private boolean isLooping;

    public void init(String objectName) {
        this.unityObjectName = objectName;
        createGLTexture();
    }

    private void createGLTexture() {
        int[] texIds = new int[1];
        GLES20.glGenTextures(1, texIds, 0);
        glTextureId = texIds[0];

        GLES20.glBindTexture(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, glTextureId);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MIN_FILTER, GLES20.GL_LINEAR);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_MAG_FILTER, GLES20.GL_LINEAR);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_S, GLES20.GL_CLAMP_TO_EDGE);
        GLES20.glTexParameteri(GLES11Ext.GL_TEXTURE_EXTERNAL_OES, GLES20.GL_TEXTURE_WRAP_T, GLES20.GL_CLAMP_TO_EDGE);

        surfaceTexture = new SurfaceTexture(glTextureId);
        surfaceTexture.setOnFrameAvailableListener(this);
        surface = new Surface(surfaceTexture);
    }

    public void load(String url) {
        loadInternal(url, true);
    }

    public void preload(String url) {
        loadInternal(url, false);
    }

    private void loadInternal(String url, boolean autoPlay) {
        isPrepared = false;
        videoWidth = 0;
        videoHeight = 0;

        UnityPlayer.currentActivity.runOnUiThread(() -> {
            try {
                if (player != null) {
                    player.release();
                    player = null;
                }

                player = new ExoPlayer.Builder(UnityPlayer.currentActivity).build();
                player.addListener(Media3VideoPlugin.this);
                player.setVideoSurface(surface);
                player.setTrackSelectionParameters(
                        player.getTrackSelectionParameters()
                                .buildUpon()
                                .setForceHighestSupportedBitrate(true)
                                .build()
                );
                player.setRepeatMode(isLooping ? Player.REPEAT_MODE_ALL : Player.REPEAT_MODE_OFF);

                MediaItem mediaItem = MediaItem.fromUri(url);
                player.setMediaItem(mediaItem);
                player.prepare();
                player.setPlayWhenReady(autoPlay);
            } catch (Exception e) {
                sendToUnity("OnMedia3Error", e.getMessage() != null ? e.getMessage() : "Unknown error");
            }
        });
    }

    public void updateTexture() {
        if (frameAvailable && surfaceTexture != null) {
            try {
                surfaceTexture.updateTexImage();
            } catch (Exception ignored) {
            }
            frameAvailable = false;
        }
    }

    public void play() {
        if (player != null) {
            UnityPlayer.currentActivity.runOnUiThread(() -> {
                if (player != null) player.play();
            });
        }
    }

    public void pause() {
        if (player != null) {
            UnityPlayer.currentActivity.runOnUiThread(() -> {
                if (player != null) player.pause();
            });
        }
    }

    public void seekTo(long ms) {
        if (player != null) {
            UnityPlayer.currentActivity.runOnUiThread(() -> {
                if (player != null) player.seekTo(ms);
            });
        }
    }

    public void setVolume(float volume) {
        if (player != null) {
            UnityPlayer.currentActivity.runOnUiThread(() -> {
                if (player != null) player.setVolume(volume);
            });
        }
    }

    public void setLooping(boolean loop) {
        isLooping = loop;
        if (player != null) {
            UnityPlayer.currentActivity.runOnUiThread(() -> {
                if (player != null)
                    player.setRepeatMode(loop ? Player.REPEAT_MODE_ALL : Player.REPEAT_MODE_OFF);
            });
        }
    }

    public long getDuration() {
        if (player != null) return player.getDuration();
        return 0;
    }

    public long getCurrentPosition() {
        if (player != null) return player.getCurrentPosition();
        return 0;
    }

    public int getVideoWidth() {
        return videoWidth;
    }

    public int getVideoHeight() {
        return videoHeight;
    }

    public int getTextureId() {
        return glTextureId;
    }

    public boolean isPlaying() {
        return player != null && player.isPlaying();
    }

    public void release() {
        if (player != null) {
            UnityPlayer.currentActivity.runOnUiThread(() -> {
                if (player != null) {
                    player.release();
                    player = null;
                }
            });
        }
        if (surface != null) {
            surface.release();
            surface = null;
        }
        if (surfaceTexture != null) {
            surfaceTexture.release();
            surfaceTexture = null;
        }
        if (glTextureId > 0) {
            GLES20.glDeleteTextures(1, new int[]{glTextureId}, 0);
            glTextureId = -1;
        }
        isPrepared = false;
        frameAvailable = false;
    }

    // --- SurfaceTexture.OnFrameAvailableListener ---

    @Override
    public void onFrameAvailable(SurfaceTexture st) {
        frameAvailable = true;
    }

    // --- Player.Listener ---

    @Override
    public void onVideoSizeChanged(VideoSize videoSize) {
        videoWidth = videoSize.width;
        videoHeight = videoSize.height;
    }

    @Override
    public void onPlaybackStateChanged(int playbackState) {
        if (playbackState == Player.STATE_READY && !isPrepared) {
            isPrepared = true;
            int w = videoWidth > 0 ? videoWidth : 1920;
            int h = videoHeight > 0 ? videoHeight : 1080;
            sendToUnity("OnMedia3Prepared", glTextureId + "|" + w + "|" + h);
        }
        if (playbackState == Player.STATE_ENDED) {
            sendToUnity("OnMedia3Completed", "");
        }
    }

    @Override
    public void onPlayerError(PlaybackException error) {
        String msg = error.getMessage() != null ? error.getMessage() : "Playback error";
        sendToUnity("OnMedia3Error", msg);
    }

    private void sendToUnity(String method, String param) {
        try {
            UnityPlayer.UnitySendMessage(unityObjectName, method, param);
        } catch (Exception ignored) {
        }
    }
}
