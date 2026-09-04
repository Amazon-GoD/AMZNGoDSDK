using System;
using System.Collections.Generic;
using UnityEngine;

namespace AMZNGoDSDK.Runtime
{
    // PlayerPrefs-ключ намеренно остаётся cp_event_queue — это исторический ключ
    // от Cross-Promo-времён. При апгрейде у клиентов очередь не теряется.
    public static class AnalyticsEventQueue
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
                    Debug.LogWarning("[Analytics] Event queue full, dropping oldest event");
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

        /// <summary>Reads a snapshot of the queue WITHOUT clearing it. Callers remove each
        /// event individually (<see cref="Remove"/> / <see cref="RemoveExact"/>) only after it is
        /// confirmed delivered, so a crash mid-flush never loses the whole batch.</summary>
        public static List<string> Peek()
        {
            lock (_lock)
            {
                return LoadQueue();
            }
        }

        /// <summary>Removes every queued event whose JSON carries the given <c>event_id</c>.</summary>
        public static void Remove(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return;

            string needle = "\"event_id\":\"" + eventId + "\"";
            lock (_lock)
            {
                var events = LoadQueue();
                int removed = events.RemoveAll(e => e != null && e.Contains(needle));
                if (removed > 0)
                    SaveQueueUnlocked(events);
            }
        }

        /// <summary>Removes the first queued event exactly equal to <paramref name="json"/>.
        /// Fallback for legacy entries that predate <c>event_id</c>.</summary>
        public static void RemoveExact(string json)
        {
            if (string.IsNullOrEmpty(json)) return;

            lock (_lock)
            {
                var events = LoadQueue();
                int idx = events.IndexOf(json);
                if (idx >= 0)
                {
                    events.RemoveAt(idx);
                    SaveQueueUnlocked(events);
                }
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
