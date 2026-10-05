/*==============================================================================
Copyright (c) 2016 By XiangKuiZheng, Inc.
All Rights Reserved.
==============================================================================*/


using UnityEngine;
using System.Collections;
using System;

public class PlayerMediaInfo {

	private string mediaName;
	/// <summary>
	/// Media name
	/// </summary>
	/// <value>media name.</value>
	public string MediaName {
		get {
			return mediaName;
		}
		set {
			mediaName = value;
		}
	}

	private string uri;
	/// <summary>
	/// video url
	/// </summary>
	/// <value>video url.</value>
	public string Uri {
		get {
			return uri;
		}
		set {
			uri = value;
		}
	}

	private int mediaWidth;
	/// <summary>
	///  video width
	/// </summary>
	/// <value>video width.</value>
	public int MediaWidth {
		get {
			return mediaWidth;
		}
		set {
			mediaWidth = value;
		}
	}

	private int mediaHeight;
	/// <summary>
	/// video height
	/// </summary>
	/// <value>video height.</value>
	public int MediaHeight {
		get {
			return mediaHeight;
		}
		set {
			mediaHeight = value;
		}
	}
}
