#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class CrossPromoTrackingQueue
    {
        private const string QueueKey = "cp_event_queue";
        private const int MaxQueueSize = 50;

        public static void Enqueue(string jsonBody)
        {
            var events = LoadQueue();
            if (events.Count >= MaxQueueSize)
            {
                Debug.LogWarning("[CrossPromoTracking] Event queue full, dropping oldest event");
                events.RemoveAt(0);
            }
            events.Add(jsonBody);
            SaveQueue(events);
        }

        public static List<string> DequeueAll()
        {
            return LoadQueue();
        }

        public static void SaveQueue(List<string> events)
        {
            if (events == null || events.Count == 0)
            {
                PlayerPrefs.DeleteKey(QueueKey);
                PlayerPrefs.Save();
                return;
            }

            var wrapper = new QueueWrapper { Items = events };
            PlayerPrefs.SetString(QueueKey, JsonUtility.ToJson(wrapper));
            PlayerPrefs.Save();
        }

        private static List<string> LoadQueue()
        {
            if (!PlayerPrefs.HasKey(QueueKey))
                return new List<string>();

            var raw = PlayerPrefs.GetString(QueueKey, "");
            if (string.IsNullOrEmpty(raw))
                return new List<string>();

            try
            {
                var wrapper = JsonUtility.FromJson<QueueWrapper>(raw);
                return wrapper?.Items ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        [Serializable]
        private class QueueWrapper
        {
            public List<string> Items = new();
        }
    }
}
#endif
