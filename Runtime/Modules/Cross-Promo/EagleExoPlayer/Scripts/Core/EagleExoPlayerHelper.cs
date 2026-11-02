/*==============================================================================
Copyright (c) 2016 By XiangKuiZheng, Inc.
All Rights Reserved.
==============================================================================*/

using UnityEngine;
using System.Collections;
using System.Runtime.InteropServices;

/// <summary>
/// Interaction with the Exo Player
/// </summary>
public class EagleExoPlayerHelper{

	public EagleExoPlayer currentEagleExoPlayer;

	public void SetCurrentEagleExoPlayer(EagleExoPlayer player)  {

		currentEagleExoPlayer = player;
	}

	public enum VideoState
	{
		STATE_UNKNOW=-2,
		STATE_ERROR=-1,
		STATE_IDLE=0,
		STATE_PREPARING=1,
		STATE_PREPARED,
		STATE_PLAYING,
		STATE_PAUSED,
		STATE_PLAYBACK_COMPLETED
	
	}

	public VideoState mCurrentState=VideoState.STATE_IDLE;


	#if UNITY_ANDROID
		protected AndroidJavaObject mEagleVideoPlayer;
		protected AndroidJavaObject mEagleVideoSurface;
	#endif



	/// <summary>
	/// Get current video player state
	/// </summary>
	/// <returns>current state.</returns>
	public VideoState GetCurrentVideoState(){
		return getCurrentVideoState();
	}

	/// <summary>
	/// Get current video player privious state
	/// </summary>
	/// <returns>privious state.</returns>
	public VideoState GetPriviousVideoState(){
		
		return getPriviousVideoState ();
	}

	/// <summary>
	/// Retry to play
	/// </summary>
	public void Retry(){
		
		retry ();
	}

	/// <summary>
	/// Set the video the loop state
	/// </summary>
	/// <param name="isLoop">if value <c>true</c> loop the video.</param>
	public void SetVideoLoop(bool isLoop){
		currentEagleExoPlayer.isLoop = isLoop;
		setVideoLoop (isLoop);
	}


	/// <summary>
	/// Init the video native render
	/// </summary>
	public void InitMediaNativeRender(){
		
		initMediaNativeRender ();
	}

	/// <summary>
	///	Video load and prepare to play
	/// </summary>
	/// <param name="videoPath">video path.</param>
	public void Load(string videoPath){
		currentEagleExoPlayer.videoPath = videoPath;
		if(currentEagleExoPlayer.mMediaInfo!=null){

			currentEagleExoPlayer.mMediaInfo.Uri = videoPath;
		}
		loadVideo (videoPath);
	}

	/// <summary>
	/// Init the unity context 
	/// </summary>
	/// <param name="objName">GameObject name.</param>
	public void InitUnityContext(string objName){
		initUnityContext (objName);
	}

	/// <summary>
	/// Set unity gameObject name to the native
	/// </summary>
	/// <param name="objName">GameObject name.</param>
	public void SetPlayerCommunicationName(string objName){
		setPlayerCommunicationName (objName);
	}

	/// <summary>
	/// Init the video player
	/// </summary>
	public void InitPlayer(){
		initVideoPlayer ();
	}
	/// <summary>
	/// Update video date to the unity texture
	/// </summary>
	public void UpdateVideoData(){
	
		updateVideoData ();
	
	}
	/// <summary>
	/// Release the video data
	/// </summary>
	public void ReleaseVideoData(){
		releaseVideoData ();
	
	}

	/// <summary>
	/// Destroy the video texture
	/// </summary>
	public void DestroyVideoTexture(){
	
		destroyVideoTexture ();
	}

	/// <summary>
	/// Is Playing?
	/// </summary>
	/// <returns><c>true</c> video playing; otherwise, <c>false</c>other states.</returns>
	public bool IsPlaying(){
		
		return isPlaying();
	}

	/// <summary>
	/// Seek the time
	/// </summary>
	/// <param name="seekTime">target seek time.</param>
	public void SeekTo(int seekTime){
		seekTo(seekTime);
	}

	/// <summary>
	/// Get the video lenth
	/// </summary>
	/// <returns>The duration.</returns>
	public int GetDuration (){
	
		return getDuration ();
	}




	/// <summary>
	/// Pause the video
	/// </summary>
	public void PauseVideo (){
		pauseVideo ();
	}

	/// <summary>
	/// Resume the video to play
	/// </summary>
	public  void ResumeVideo (){
	
		resumeVideo ();
	}

	/// <summary>
	/// Set the video volume
	/// </summary>
	/// <param name="volume">volume.</param>
	public void SetVolume(float volume){
		setVolume (volume);
	}



	/// <summary>
	/// Get the video current position 
	/// </summary>
	/// <returns>video current position.</returns>
	public int GetCurrentPosition(){
		
		return getCurrentPosition ();
	}


	/// <summary>
	/// Sets the native texture translation.
	/// </summary>
	/// <param name="x">The x coordinate.</param>
	/// <param name="y">The y coordinate.</param>
	/// <param name="z">The z coordinate.</param>
	public void SetNativeTextureTranslation (float x, float y, float z){
		setNativeTextureTranslation (x,y,z);
	}

	/// <summary>
	/// Sets the native texture rotation.
	/// </summary>
	/// <param name="x">The x coordinate.</param>
	/// <param name="y">The y coordinate.</param>
	/// <param name="z">The z coordinate.</param>
	public void SetNativeTextureRotation (float x, float y, float z){
		setNativeTextureRotation (x, y, z);
	}

	/// <summary>
	/// Sets the native texture scale.
	/// </summary>
	/// <param name="x">The x coordinate.</param>
	/// <param name="y">The y coordinate.</param>
	/// <param name="z">The z coordinate.</param>
	public void SetNativeTextureScale(float x, float y, float z){
		setNativeTextureScale (x, y, z);
	}


	#if UNITY_EDITOR && !UNITY_ANDROID 

	private void retry(){


	}

	private VideoState getPriviousVideoState(){
		return (VideoState)0;
	}

	private int getVolume(){

		return 0;
	}

	private int getCurrentPosition(){

		return 0;
	}

	private void setVolume(float volume){
		
		
	}

	private void resumeVideo (){

	}

	private void pauseVideo (){

	}

	private int getDuration(){

		return -1;
	}

	private void seekTo(int seekTime){
		
	}

	private bool isPlaying(){
		
		return false;
	}

	private void destroyVideoTexture(){

	}

	private void releaseVideoData(){

	}


	private void updateVideoData(){

	}

	private void initMediaNativeRender(){

	}

	private void loadVideo(string path){
	}

	private void initUnityContext(string objName){

	}

	private void setPlayerCommunicationName(string objName){

	}


	private void initVideoPlayer(){

	}

	private VideoState getCurrentVideoState(){

		return (VideoState)0;
	}

	private void setVideoLoop(bool isLoop){


	}

	private void setNativeTextureTranslation (float x, float y, float z){
		
	}


	private void setNativeTextureRotation (float x, float y, float z){

	}


	private void setNativeTextureScale(float x, float y, float z){

	}

	#elif UNITY_ANDROID
		[DllImport("eagleRenderer")]
		private static extern void setNativeTextureTranslation (float x, float y, float z);

		[DllImport("eagleRenderer")]
		private static extern void setNativeTextureRotation (float x, float y, float z);

		[DllImport("eagleRenderer")]
		private static extern void setNativeTextureScale(float x, float y, float z);
		
		private VideoState getPriviousVideoState(){
			return (VideoState)getEagleVideoPlayer ().Call<int> ("getPreviousState");
		}

		private void retry(){
			getEagleVideoPlayer ().Call ("RetryVideo");
		}

		private VideoState getCurrentVideoState(){
			return (VideoState)getEagleVideoPlayer ().Call<int> ("getCurrentVideoState");
		}

		private int getCurrentPosition(){
			return getEagleVideoPlayer ().Call<int> ("getCurrentPosition");
		}

		private void setVolume(float volume){
			getEagleVideoPlayer ().Call ("setVolumn",volume);
			
		}

		private bool isPlaying ()
		{
			
			return getEagleVideoPlayer().Call<bool> ("isPlaying");
	
		}

		private void seekTo (int time)
		{
			
			getEagleVideoPlayer().Call ("seekTo", time);
			
		}

		private int getDuration ()
		{
			
			return getEagleVideoPlayer().Call<int> ("getDuration");
			
		}
	

		private void pauseVideo ()
		{
			
			EaglePlayerNativeUtils.CallObjectMethod ("pauseVideo");
			
		}

		private void resumeVideo ()
		{
			EaglePlayerNativeUtils.CallObjectMethod ("resumeVideo");
		}



	private void destroyVideoTexture(){
	
		releaseVideoData ();
		getEagleVideoPlayer ().Call ("destroyRender");
	}

	private void releaseVideoData(){
		getEagleVideoSurface ().Call ("releaseEagleSurface");
		EaglePlayerNativeUtils.CallObjectMethod("releaseVideo");
	}


	private void updateVideoData(){
	
		getEagleVideoPlayer ().Call ("updateVideoImage");
	}

	private void initMediaNativeRender(){

		getEagleVideoPlayer().Call ("InitNativeMediaRender");
	}

	private void loadVideo(string path){
		getEagleVideoPlayer ().Call ("openVideo",getActivity (),path,getEagleVideoSurface());
	}

	private AndroidJavaObject getActivity ()
	{
		return new AndroidJavaClass ("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject> ("currentActivity");
	}


	private void initUnityContext(string objName){
		EaglePlayerNativeUtils.CallObjectMethod("initFromUnity", getEagleVideoPlayer());
		EaglePlayerNativeUtils.CallObjectMethod("setPlayerUnityObjName", objName);
		
	}

	private void setPlayerCommunicationName(string objName){
		getEagleVideoPlayer ().Call ("setPlayerUnityObjName", objName);
	}


	private void initVideoPlayer(){
		getEagleVideoSurface ().Call ("init");
	}

	private AndroidJavaObject getEagleVideoPlayer(){
		if(mEagleVideoPlayer==null){
			mEagleVideoPlayer = new AndroidJavaObject ("com.eagle.lib.EagleVideoPlayer");
		}
		return mEagleVideoPlayer;
	}

	private AndroidJavaObject getEagleVideoSurface(){
		if(mEagleVideoSurface==null){
			mEagleVideoSurface = new AndroidJavaObject ("com.eagle.lib.EagleVideoSurface");
		}
		return mEagleVideoSurface;
	}

	private void setVideoLoop(bool isLoop){

		EaglePlayerNativeUtils.CallObjectMethod("setVideoLooping",isLoop);      
	}
	#endif
}
