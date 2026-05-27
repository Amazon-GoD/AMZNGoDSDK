using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    public static class CrossPromoTrackingQueue
    {
        private const string QueueKey = "cp_event_queue";
        private const int MaxQueueSize = 50;
        private static readonly object _lock = new object();

        public static void Enqueue(string jsonBody)
        {
            lock (_lock)
            {
                var events = LoadQueue();
                if (events.Count >= MaxQueueSize)
                {
                    Debug.LogWarning("[CrossPromoTracking] Event queue full, dropping oldest event");
                    events.RemoveAt(0);
                }
                events.Add(jsonBody);
                SaveQueueUnlocked(events);
            }
        }

        /// <summary>Atomically reads and clears the queue.</summary>
        public static List<string> DequeueAll()
        {
            lock (_lock)
            {
                var events = LoadQueue();
                if (events.Count > 0)
                {
                    PlayerPrefs.DeleteKey(QueueKey);
                    PlayerPrefs.Save();
                }
                return events;
            }
        }

        public static void SaveQueue(List<string> events)
        {
            lock (_lock)
            {
                SaveQueueUnlocked(events);
            }
        }

        /// <summary>Atomically prepends events back to the front of the queue (used to restore unsent events after a flush).</summary>
        public static void Requeue(List<string> events)
        {
            if (events == null || events.Count == 0) return;

            lock (_lock)
            {
                var current = LoadQueue();
                var merged = new List<string>(events.Count + current.Count);
                merged.AddRange(events);
                merged.AddRange(current);

                if (merged.Count > MaxQueueSize)
                    merged.RemoveRange(0, merged.Count - MaxQueueSize);

                SaveQueueUnlocked(merged);
            }
        }

        private static void SaveQueueUnlocked(List<string> events)
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
