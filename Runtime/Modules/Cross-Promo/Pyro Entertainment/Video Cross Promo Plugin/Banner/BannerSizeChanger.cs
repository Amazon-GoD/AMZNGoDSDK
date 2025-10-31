using UnityEngine;
using UnityEngine.UI;

public class BannerSizeChanger : MonoBehaviour
{
    enum StretchType { Crop, Compress }
    [SerializeField] StretchType stretchType = StretchType.Compress;
    [SerializeField] RawImage vidplayerRawImage;
    [SerializeField] float width = 640;
    [SerializeField] float height = 100;
    float widthBase = 640;
    float heightbase = 100;

    public void UpdateSize()
    {
        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
    }

    private void OnValidate()
    {
        switch (stretchType)
        {
            case StretchType.Compress:
                Compress();
                break;
            case StretchType.Crop:
                Crop();
                break;
        }
    }

    private void Crop()
    {
        float w = width / widthBase;
        float x = (1 - w) / 2f;
        float h = height / heightbase;
        float y = (1 - h) / 2f;
        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
        vidplayerRawImage.uvRect = new Rect(x, y, w, h);
    }

    private void Compress()
    {
        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(width, height);
    }
}
