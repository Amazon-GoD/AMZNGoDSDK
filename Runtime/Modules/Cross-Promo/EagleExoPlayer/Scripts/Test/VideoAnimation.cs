using UnityEngine;
using System.Collections;

public class VideoAnimation : MonoBehaviour {

	public EagleExoPlayer eagleExoPlayer;

	private float currentState = 0;

	private bool isStartTrans = false;
	private bool isStartRot=false;
	private bool isStartScale=false;
	void Update () {
		
		if(eagleExoPlayer==null){return;}



		if (isStartRot) {
			currentState += Time.deltaTime * 1.5f;
			eagleExoPlayer.EaglePlayer.SetNativeTextureRotation (0, 0, currentState);
		} 

		if (isStartTrans) {
			currentState += Time.deltaTime*0.1f;
			eagleExoPlayer.EaglePlayer.SetNativeTextureTranslation (currentState, 0, 0);
		} 

		if(isStartScale) {
			currentState -= Time.deltaTime*0.5f;
			eagleExoPlayer.EaglePlayer.SetNativeTextureScale (currentState,currentState,1);
		}
	}

	/// <summary>
	/// Starts the translation.
	/// </summary>
	public void StartTranslation(){
		eagleExoPlayer.EaglePlayer.SetNativeTextureRotation (0, 0, 0);
		eagleExoPlayer.EaglePlayer.SetNativeTextureScale (1,1,1);
		currentState = 0;
		isStartRot = false;
		isStartScale = false;
		isStartTrans = true;

	}

	/// <summary>
	/// Starts the rotation.
	/// </summary>
	public void StartRotation(){
		eagleExoPlayer.EaglePlayer.SetNativeTextureTranslation (0, 0, 0);
		eagleExoPlayer.EaglePlayer.SetNativeTextureScale (1,1,1);
		isStartRot = true;
		isStartScale = false;
		isStartTrans = false;
		currentState = 0;

	}

	/// <summary>
	/// Starts the scale.
	/// </summary>
	public void StartScale(){
		eagleExoPlayer.EaglePlayer.SetNativeTextureTranslation (0, 0, 0);
		eagleExoPlayer.EaglePlayer.SetNativeTextureRotation (0, 0, 0);
		isStartRot = false;
		isStartScale = true;
		isStartTrans = false;
		currentState = 1;
	}


}
