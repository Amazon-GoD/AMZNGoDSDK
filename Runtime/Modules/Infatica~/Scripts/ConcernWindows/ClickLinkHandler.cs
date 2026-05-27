using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using TMPro;

namespace AMZNGoDSDK.Runtime
{
    [RequireComponent(typeof(TMP_Text))]
    public class ClickLinkHandler : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private ClickAction[] _clickActions;

        private Dictionary<string, UnityEvent> ClickActions => 
            _clickActions.ToDictionary(x => x.Id, x => x.Action); 
        
        private TMP_Text _text;
        private void Awake() => _text = GetComponent<TMP_Text>();

        public void OnPointerClick(PointerEventData eventData)
        {
            Debug.Log("Clicked");

            Vector3 mousePosition = new Vector3(eventData.position.x, eventData.position.y, 0);
            
            int linkIndex = TMP_TextUtilities.FindIntersectingLink(_text, mousePosition, null);

            if (linkIndex == -1)
                return;
            
            TMP_LinkInfo linkInfo = _text.textInfo.linkInfo[linkIndex];
            string link = linkInfo.GetLinkID();

            Debug.Log($"Link: {link}");

            if (!string.IsNullOrEmpty(link))
            {
                if (ClickActions.TryGetValue(link, out var action))
                    action.Invoke();
                else
                    Application.OpenURL(link);
            }
        }
    }

    [Serializable]
    public class ClickAction
    {
        public string Id;
        public UnityEvent Action;
    }
}