using System;
using System.Collections.Generic;

namespace RobleysQuestingFramework
{
    // A server-side view of one character's latest client-reported inventory.
    public class QuestInventory
    {
        readonly Action<bool> _trackingChanged;
        Action _changed;
        bool _notificationPending;
        Dictionary<Tuple<string, int>, int> _counts = new Dictionary<Tuple<string, int>, int>();

        public event Action Changed
        {
            add
            {
                bool wasObserved = IsObserved;
                _changed += value;
                if (!wasObserved && IsObserved)
                    _trackingChanged(true);
                // Deliver after subscription configuration completes, never reentrantly.
                _notificationPending = HasSnapshot;
            }
            remove
            {
                bool wasObserved = IsObserved;
                _changed -= value;
                if (wasObserved && !IsObserved)
                {
                    Invalidate();
                    _trackingChanged(false);
                }
            }
        }

        public bool HasSnapshot { get; private set; }
        public bool IsObserved => _changed != null;

        public QuestInventory(Action<bool> trackingChanged)
        {
            _trackingChanged = trackingChanged ?? throw new ArgumentNullException(nameof(trackingChanged));
        }

        public long CountItems(string prefabID, int? quality = null)
        {
            if (quality.HasValue)
                return _counts.TryGetValue(Tuple.Create(prefabID, quality.Value), out int count) ? count : 0;

            long total = 0;
            foreach (var entry in _counts)
            {
                if (entry.Key.Item1 == prefabID)
                    total += entry.Value;
            }
            return total;
        }

        public void SetSnapshot(Dictionary<Tuple<string, int>, int> snapshot)
        {
            _counts = snapshot;
            HasSnapshot = true;
            _notificationPending = true;
        }

        public void Dispatch()
        {
            if (!_notificationPending)
                return;
            _notificationPending = false;
            _changed?.Invoke();
        }

        public void Invalidate()
        {
            _counts.Clear();
            HasSnapshot = false;
            _notificationPending = false;
        }
    }
}
