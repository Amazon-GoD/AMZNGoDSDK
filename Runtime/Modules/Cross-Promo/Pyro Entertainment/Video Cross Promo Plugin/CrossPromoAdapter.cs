using Pyro;
using UnityEngine;
using UnityEngine.Events;

public class CrossPromoAdapter : MonoBehaviour
{
    public void Initialize()
    {
        SubscribeCallbacks();
    }

    #region Callbacks
    string PyroDebugText = "<color=aqua><b>Pyro</b></color>: ";
    private void SubscribeCallbacks()
    {
        CrossPromoManager.Instance.NoFillCallback += NoFillCallBack;
        CrossPromoManager.Instance.OnPlayErrorCallback += OnPlayErrorCallback;
        CrossPromoManager.Instance.AdCloseCallback += AdCloseCallBack;
    }
    private void NoFillCallBack()
    {
        Debug.Log(PyroDebugText + "plugin did not provide any ad because of video weight settings");
    }
    private void OnPlayErrorCallback(string error)
    {   
        Debug.Log(PyroDebugText + "there is an error when trying to play the selected video");
    }
    private void AdCloseCallBack()
    {
        Debug.Log(PyroDebugText + "ad video closed");
    }
    private void CrossPromoNotReadyCallback()
    {
        Debug.Log(PyroDebugText + "plugin is not ready");
    }
    #endregion
}