#if AMZN_CROSSPROMO_ENABLED
using System;
using System.Collections.Generic;
using System.Linq;
using static AMZNGoDSDK.Runtime.CrossPromoConfigurationManager;

namespace AMZNGoDSDK.Runtime
{
    /// <summary>Закреплённые слоты и взвешенное заполнение пробелов. Peek не расходует слот.</summary>
    internal sealed class CrossPromoPositionRotation
    {
        internal sealed class Selection
        {
            internal PromoConfiguration Video;
            internal int Slot;
            internal string[] ResetCooldownTitles;
            internal string LastShownTitle;
        }

        // ID — исходный индекс в JSON. Владельцы и длина круга не меняются при фильтрации.
        private readonly Dictionary<int, int> _ownerPositions;
        private readonly int _cycleLength;
        private int _nextSlot = 1;
        private Selection _pending;

        internal bool IsOrdered => _ownerPositions.Count > 0;

        internal CrossPromoPositionRotation(List<PromoConfiguration> videos)
        {
            _ownerPositions = new Dictionary<int, int>();
            _cycleLength = videos?.Count ?? 0;
            var occupied = new HashSet<int>();
            for (int i = 0; i < (videos?.Count ?? 0); i++)
            {
                var video = videos[i];
                if (video == null) continue;
                video.RotationId = i;
                if (video.position <= 0) continue;
                _cycleLength = Math.Max(_cycleLength, video.position);
                // При конфликте место получает первый креатив в JSON, остальные — в общий пул.
                if (occupied.Add(video.position))
                    _ownerPositions.Add(i, video.position);
            }
        }

        private CrossPromoPositionRotation(Dictionary<int, int> ownerPositions, int cycleLength)
        {
            _ownerPositions = ownerPositions;
            _cycleLength = cycleLength;
        }

        internal CrossPromoPositionRotation CopyLayout()
        {
            return new CrossPromoPositionRotation(_ownerPositions, _cycleLength);
        }

        internal Selection Peek(List<PromoConfiguration> videos, string lastShownTitle,
            Func<List<PromoConfiguration>, PromoConfiguration> selectWeighted)
        {
            if (!IsOrdered || videos == null)
            {
                _pending = null;
                return null;
            }
            var available = videos.Where(v => v != null && !v.IsShowLimitReached()
                && (!string.IsNullOrWhiteSpace(v.VideoUrl) || !string.IsNullOrWhiteSpace(v.FileName))).ToList();
            var pinned = available.Where(v => _ownerPositions.ContainsKey(v.RotationId)).ToList();
            var owner = pinned.Find(v => _ownerPositions[v.RotationId] == _nextSlot);
            int slot = _nextSlot;
            string[] resetTitles = null;
            List<PromoConfiguration> pool;

            if (owner != null)
            {
                pool = new List<PromoConfiguration> { owner };
            }
            else
            {
                var fillers = available.Where(v => !_ownerPositions.ContainsKey(v.RotationId)).ToList();
                pool = fillers.Where(v => !VideoCooldownRegistry.IsOnCooldown(v.Title)).ToList();
                if (pool.Count == 0 && fillers.Count > 0)
                {
                    // Сбрасываем только пул свободных мест и только после засчитанного показа.
                    resetTitles = fillers.Select(v => v.Title).ToArray();
                    pool = fillers.Where(v => v.Title != lastShownTitle).ToList();
                    if (pool.Count == 0) pool = fillers;
                }

                if (pool.Count == 0)
                {
                    // Пустые места пропускаем за O(число креативов), даже при position=int.MaxValue.
                    long nearestDistance = long.MaxValue;
                    foreach (var video in pinned)
                    {
                        int position = _ownerPositions[video.RotationId];
                        long distance = position >= _nextSlot
                            ? (long)position - _nextSlot
                            : (long)_cycleLength - _nextSlot + position;
                        if (distance >= nearestDistance) continue;
                        nearestDistance = distance;
                        owner = video;
                        slot = position;
                    }
                    if (owner != null) pool.Add(owner);
                }
            }

            if (pool.Count == 0)
            {
                _pending = null;
                return null;
            }

            // Прелоад переживает пересборку списка, но не исчезновение/лимит/кулдаун креатива.
            var retained = _pending != null && _pending.Slot == slot
                ? pool.Find(v => v.RotationId == _pending.Video.RotationId)
                : null;
            if (retained != null)
            {
                _pending.Video = retained;
                _pending.ResetCooldownTitles = resetTitles;
                _pending.LastShownTitle = lastShownTitle;
                return _pending;
            }

            _pending = new Selection
            {
                Video = owner ?? selectWeighted(pool),
                Slot = slot,
                ResetCooldownTitles = resetTitles,
                LastShownTitle = lastShownTitle
            };
            return _pending;
        }

        internal void Commit(Selection selection)
        {
            if (selection == null || !ReferenceEquals(selection, _pending)) return;
            _pending = null;
            _nextSlot = selection.Slot == _cycleLength ? 1 : selection.Slot + 1;
            if (selection.ResetCooldownTitles != null)
            {
                VideoCooldownRegistry.ClearAllCooldownsExcept(selection.LastShownTitle, selection.ResetCooldownTitles);
                // Оверлей уже записал показ до callback; восстанавливаем его после сброса круга.
                VideoCooldownRegistry.RecordShown(selection.Video.Title);
            }
        }
    }
}
#endif
