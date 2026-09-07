using UnityEngine;
using System.Collections;
using UnityEngine.UI;
public class VideoPlayerControl : MonoBehaviour {
	public EagleExoPlayer eagleExoPlayer;

	public Text currentTimeLabel;

	public Text totolTimeLabel;

	private float currentSeekTime=0.0f;


	/// <summary>
	/// pause the video
	/// </summary>
	public void Pause(){
		if (eagleExoPlayer != null &&
			eagleExoPlayer.EaglePlayer.IsPlaying()
		) {
			eagleExoPlayer.EaglePlayer.PauseVideo ();
		
		}
			
		
		 
	}

	/// <summary>
	/// resume video play
	/// </summary>
	public void ResumePlay(){
		if (eagleExoPlayer != null &&
			eagleExoPlayer.EaglePlayer.GetCurrentVideoState()==EagleExoPlayerHelper.VideoState.STATE_PAUSED
		) {
			eagleExoPlayer.EaglePlayer.ResumeVideo ();

		}
	
	}

	/// <summary>
	/// Retry video play
	/// </summary>
	public void Retry(){
		if (eagleExoPlayer != null
		) {
			eagleExoPlayer.EaglePlayer.Retry ();

		}

	}

	/// <summary>
	///  load the video
	/// </summary>
	public void Load(){
		if (eagleExoPlayer != null
		) {
			eagleExoPlayer.EaglePlayer.Load ("bigbuckbunny.mp4");

		}


	}
	


	public void SeekDown(){
		if (eagleExoPlayer != null) {
			eagleExoPlayer.EaglePlayer.SeekTo (eagleExoPlayer.EaglePlayer.GetCurrentPosition()+6000);
		}
	
	}

	public void SeekUp(){
		if (eagleExoPlayer != null) {
			if (eagleExoPlayer != null) {
				eagleExoPlayer.EaglePlayer.SeekTo (eagleExoPlayer.EaglePlayer.GetCurrentPosition()-6000);
			}
		}

	}

	void Update(){

		if(!eagleExoPlayer.EaglePlayer.IsPlaying()){
			return;
		}

		currentSeekTime = ((float)eagleExoPlayer.EaglePlayer.GetCurrentPosition())/1000.0f;

		float playLengthTime = ((float)eagleExoPlayer.EaglePlayer.GetDuration ())/1000.0f;

		currentTimeLabel.text=NormalizeTime (currentSeekTime);
		totolTimeLabel.text= NormalizeTime (playLengthTime);

	}


	


	/// <summary>
	/// Normalize the time "00:00:00"
	/// </summary>
	/// <returns>The time.</returns>
	/// <param name="nTime">N time.</param>
	public  string NormalizeTime(float nTime){
		if (nTime < 60) {

			return "00:" +  (Mathf.Floor(nTime) <10.0f ? "0"+Mathf.Floor(nTime) :""+ Mathf.Floor(nTime));

		} else if (nTime>=60 && nTime<3600) {

			return (Mathf.Floor (nTime / 60.0f)<10.0f ? "0"+Mathf.Floor (nTime / 60.0f) : ""+Mathf.Floor (nTime / 60.0f)) + ":" + (Mathf.Floor((nTime % 60.0f))<10.0f ? "0"+Mathf.Floor((nTime % 60.0f)) : ""+Mathf.Floor((nTime % 60.0f)));

		}

		return (nTime / 3600<10.0f ? "0"+nTime / 3600 : ""+nTime / 3600) + ":" + (Mathf.Floor ((nTime % 3600) / 60)<10.0f ? "0"+Mathf.Floor ((nTime % 3600) / 60) : ""+Mathf.Floor ((nTime % 3600) / 60)) + ":" +( Mathf.Floor((Mathf.Floor ((nTime % 3600) % 60)))<10.0f ? "0"+Mathf.Floor((Mathf.Floor ((nTime % 3600) % 60))) : ""+Mathf.Floor((Mathf.Floor ((nTime % 3600) % 60))));

	}

}
