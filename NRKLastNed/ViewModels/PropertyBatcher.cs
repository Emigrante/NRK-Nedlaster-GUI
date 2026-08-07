using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NRKLastNed.ViewModels
{
    /// <summary>
    /// Batches PropertyChanged notifications to reduce UI invalidation overhead during high-frequency updates.
    /// Particularly useful for Progress and Status updates during batch operations.
    /// </summary>
    public class PropertyBatcher : INotifyPropertyChanged
    {
        private readonly Dictionary<string, object> _cachedValues = new();
        private readonly Dictionary<string, (DateTime LastUpdate, int UpdateCount)> _updateMetrics = new();
        private readonly int _debounceMs;
        private readonly HashSet<string> _pendingNotifications = new();
        private Timer _batchTimer;
        private readonly object _lock = new();

        public PropertyBatcher(int debounceMs = 50)
        {
            _debounceMs = debounceMs;
        }

        /// <summary>
        /// Sets a property value and queues a batched PropertyChanged notification.
        /// Multiple updates within the debounce window are coalesced into a single notification.
        /// </summary>
        public void SetAndNotify<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
            QueueNotification(propertyName);
        }

        /// <summary>
        /// Manually queue a property for notification (useful for computed properties).
        /// </summary>
        public void QueueNotification(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return;

            lock (_lock)
            {
                _pendingNotifications.Add(propertyName);
                _updateMetrics[propertyName] = (DateTime.UtcNow, 
                    _updateMetrics.TryGetValue(propertyName, out var metrics) ? metrics.UpdateCount + 1 : 1);

                // Start or restart the batch timer
                _batchTimer?.Dispose();
                _batchTimer = new Timer(FlushNotifications, null, _debounceMs, Timeout.Infinite);
            }
        }

        private void FlushNotifications(object state)
        {
            lock (_lock)
            {
                foreach (var propertyName in _pendingNotifications)
                {
                    OnPropertyChanged(propertyName);
                }
                _pendingNotifications.Clear();
            }
        }

        public void Dispose()
        {
            _batchTimer?.Dispose();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
