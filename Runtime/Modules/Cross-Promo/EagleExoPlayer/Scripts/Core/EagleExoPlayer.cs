/*==============================================================================
Copyright (c) 2016 By XiangKuiZheng, Inc.
All Rights Reserved.
==============================================================================*/
using UnityEngine;
using System.Collections;
using System;
using System.Collections.Generic;

/// <summary>
/// Video Display.Can be used in the class for the rendered
/// texture information to other objects
/// </summary>
public class EagleExoPlayer : MonoBehaviour
{
	/// <summary>
	/// The object interaction with the Exo Player
	/// </summary>
	private EagleExoPlayerHelper eaglePlayer=new EagleExoPlayerHelper();
	public EagleExoPlayerHelper EaglePlayer{
		get{ 
		
			return eaglePlayer;
		}

	}


	/// <summary>
	/// Load the video and to play
	/// </summary>
	public bool isAutoPlay=true;

	/// <summary>
	/// video is loop?
	/// </summary>
	public bool isLoop=false;

	/// <summary>
	///  Media info,include url,video width and height.
	/// </summary>
	public PlayerMediaInfo mMediaInfo;

	[HideInInspector]
	public int U3dTextureId = -1;

	private Texture2D mMediaTexture2D;

	public List<MeshRenderer> mMeshRenderList=new List<MeshRenderer>();
	private string gameObjName;
	private bool videoReady = false;
	public String videoPath;


	public Texture2D GetMediaTexture(){
	
		return mMediaTexture2D;
	}

	public void SetMediaTexture(Texture2D mTexture){
	
		mMediaTexture2D = mTexture;
	}


	void Start ()
	{	
		eaglePlayer.SetCurrentEagleExoPlayer (this);
		PlayerInit ();
		if(this.isAutoPlay){
			eaglePlayer.Load(mMediaInfo.Uri);
		}
	}
	/// <summary>
	/// Init the video player and Opengl ES
	/// </summary>
	public void PlayerInit(){
		videoReady = false;
		gameObjName = this.name;

		mMediaInfo = new PlayerMediaInfo ();

		mMediaInfo.Uri = videoPath;

		eaglePlayer.SetVideoLoop (isLoop);

		eaglePlayer.InitPlayer();

		eaglePlayer.SetPlayerCommunicationName(gameObjName);

		eaglePlayer.InitUnityContext(gameObjName);


	}

	//this method is called from android when exoplayer is prepared
	void VideoPreparedCallback(){
		eaglePlayer.InitMediaNativeRender ();
	}

	void GetNativeTextureId(string mediaInfo){

		if(this.mMediaTexture2D!=null){
			Destroy (this.mMediaTexture2D);
			this.mMediaTexture2D = null;
		}
		if (mediaInfo == null || mediaInfo == "") {
			return;
		}
		string[] mediaInfoArray = mediaInfo.Split ('|');
		if (mediaInfoArray.Length < 3) {
			Debug.LogError ("GetNativeTextureId: input parameter is invalid-->"+mediaInfo);
			return;
		}
		int textureId = int.Parse(mediaInfoArray [0]);
		int width = int.Parse(mediaInfoArray [1]);
		int height = int.Parse(mediaInfoArray [2]);
		mMediaInfo.MediaWidth = width;
		mMediaInfo.MediaHeight = height;

		IntPtr tmpPtr;


		U3dTextureId = textureId;
		tmpPtr = (IntPtr)U3dTextureId;

		mMediaTexture2D = Texture2D.CreateExternalTexture (width, height, TextureFormat.ARGB32, false, true, tmpPtr);

		for(int mRenderIndex=0;mRenderIndex<mMeshRenderList.Count;mRenderIndex++){
			mMeshRenderList[mRenderIndex].material.mainTexture = mMediaTexture2D;
		}
		videoReady = true;
	}
		


	void Update ()
	{
		
		if (isBackKeyDown ()) {
			
			Destroy (this.gameObject);
			Application.Quit ();

		}

		if(videoReady){
			eaglePlayer.UpdateVideoData();
		}
			
	}


	void OnApplicationPause(bool pause){
		if (pause) {
			eaglePlayer.PauseVideo ();
		} else {
			eaglePlayer.ResumeVideo ();
		}
	
	}

	void OnDestroy ()
	{
		
		eaglePlayer.DestroyVideoTexture();

	}

	private bool isBackKeyDown(){
		if ( Application.platform == RuntimePlatform.Android &&(Input.GetKeyDown(KeyCode.Escape)))
		{
			return true;
		}
		return false;
	}

	private bool isHomeKeyDown(){
		if ( Application.platform == RuntimePlatform.Android &&(Input.GetKeyDown(KeyCode.Home)))
		{
			return true;
		}
		return false;
	}
}
