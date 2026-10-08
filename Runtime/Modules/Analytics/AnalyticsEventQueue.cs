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
        private const int MaxBannerImpressions = 10;
        private static readonly object _lock = new object();

        public static void Enqueue(string jsonBody)
        {
            TryEnqueue(jsonBody);
        }

        /// <summary>Persists the event unless a banner impression would displace a regular event.</summary>
        public static bool TryEnqueue(string jsonBody)
        {
            lock (_lock)
            {
                var events = LoadQueue();
                bool added = AddWithCapacity(events, jsonBody, out _);
                SaveQueueUnlocked(events);
                return added;
            }
        }

        /// <summary>
        /// Persists several events with a single PlayerPrefs write. Sealed banner batches use it so
        /// that removing them from the pending-batch store and queuing them land in the same save.
        /// Returns how many were queued (a banner event is refused when regular events fill the queue).
        /// </summary>
        public static int TryEnqueueAll(List<string> jsonBodies) => TryEnqueueAll(jsonBodies, out _);

        /// <summary>
        /// Same, and reports through <paramref name="evicted"/> how many events already in the queue
        /// were dropped to make room (the oldest banner event first, see the capacity rules below).
        /// A caller that sizes its batch with <see cref="GetBannerBudget"/> never causes an eviction.
        /// </summary>
        public static int TryEnqueueAll(List<string> jsonBodies, out int evicted)
        {
            evicted = 0;
            if (jsonBodies == null || jsonBodies.Count == 0)
                return 0;

            lock (_lock)
            {
                var events = LoadQueue();
                int added = 0;
                foreach (string json in jsonBodies)
                {
                    if (AddWithCapacity(events, json, out bool dropped))
                        added++;
                    if (dropped)
                        evicted++;
                }

                SaveQueueUnlocked(events);
                return added;
            }
        }

        /// <summary>
        /// Banner events (CP and MAX banner impressions) waiting in the queue, and how many more can
        /// be added without evicting anything: the 10-slot banner budget and the 50-event total both
        /// have to have room. A queued banner event is one not yet confirmed by the backend — sent
        /// and in flight, or kept after a transient failure.
        /// </summary>
        public static void GetBannerBudget(out int queuedBannerEvents, out int freeBannerSlots)
        {
            lock (_lock)
            {
                var events = LoadQueue();
                int banner = 0;
                foreach (string json in events)
                {
                    if (IsBannerImpression(json))
                        banner++;
                }

                queuedBannerEvents = banner;
                freeBannerSlots = Math.Max(0, Math.Min(MaxBannerImpressions - banner, MaxQueueSize - events.Count));
            }
        }

        /// <summary>Updates an event still waiting in the queue without re-enqueuing a delivered event.</summary>
        public static bool ReplaceExact(string originalJson, string replacementJson)
        {
            if (string.IsNullOrEmpty(originalJson) || string.IsNullOrEmpty(replacementJson))
                return false;

            lock (_lock)
            {
                var events = LoadQueue();
                int index = events.IndexOf(originalJson);
                if (index < 0)
                    return false;

                events[index] = replacementJson;
                SaveQueueUnlocked(events);
                return true;
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

        /// <summary>Checks whether a snapshot entry is still queued and has not been replaced.</summary>
        public static bool ContainsExact(string json)
        {
            lock (_lock)
            {
                return LoadQueue().Contains(json);
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

            var wrapper = new QueueWrapper { Items = ApplyCapacity(events) };
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
                return ApplyCapacity(wrapper?.Items);
            }
            catch
            {
                return new List<string>();
            }
        }

        private static List<string> ApplyCapacity(List<string> events)
        {
            var bounded = new List<string>();
            if (events == null)
                return bounded;

            if (events.Count <= MaxQueueSize
                && events.FindAll(IsBannerImpression).Count <= MaxBannerImpressions)
                return new List<string>(events);

            foreach (string json in events)
                AddWithCapacity(bounded, json, out _);
            return bounded;
        }

        /// <param name="evicted">true when a queued event was dropped to make room.</param>
        private static bool AddWithCapacity(List<string> events, string json, out bool evicted)
        {
            evicted = false;
            bool isBannerImpression = IsBannerImpression(json);
            int firstBanner = -1;
            int bannerCount = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (!IsBannerImpression(events[i]))
                    continue;

                if (firstBanner < 0)
                    firstBanner = i;
                bannerCount++;
            }

            // Only the frequent banner streams have lower priority: CP banner impressions and
            // MAX banner mediation impressions (since 1.0.9 both arrive batched with "n").
            // Legacy, malformed and unknown payloads keep the regular budget, as do clicks,
            // full-screen revenue and identity events.
            if (isBannerImpression && bannerCount >= MaxBannerImpressions)
            {
                events.RemoveAt(firstBanner);
                evicted = true;
                Debug.LogWarning("[Analytics] Banner impression budget full, dropping oldest banner impression");
            }
            else if (events.Count >= MaxQueueSize)
            {
                if (firstBanner >= 0)
                {
                    events.RemoveAt(firstBanner);
                    evicted = true;
                    Debug.LogWarning("[Analytics] Event queue full, dropping oldest banner impression");
                }
                else if (isBannerImpression)
                {
                    Debug.LogWarning("[Analytics] Event queue full of regular events, banner impression not queued");
                    return false;
                }
                else
                {
                    events.RemoveAt(0);
                    evicted = true;
                    Debug.LogWarning("[Analytics] Event queue full of regular events, dropping oldest regular event");
                }
            }

            events.Add(json);
            return true;
        }

        private static bool IsBannerImpression(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var envelope = JsonUtility.FromJson<EventEnvelope>(json);
                return envelope != null
                    && (envelope.event_name == "cp_impression" || envelope.event_name == "mediation_impression")
                    && envelope.placement == "banner";
            }
            catch
            {
                return false;
            }
        }

        [Serializable]
        private class EventEnvelope
        {
            public string event_name;
            public string placement;
        }

        [Serializable]
        private class QueueWrapper
        {
            public List<string> Items = new();
        }
    }
}
