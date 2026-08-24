using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NRKLastNed.Core.ViewModels
{
    public class PropertyBatcher : INotifyPropertyChanged
    {
        private readonly object _lock = new();
        private readonly HashSet<string> _pendingProperties = new();
        private readonly Timer _batchTimer;
        private readonly int _throttleMs;
        private readonly SynchronizationContext? _syncContext;
        private bool _isScheduled;

        public event PropertyChangedEventHandler? PropertyChanged;

        public PropertyBatcher(int debounceMs = 50)
        {
            _throttleMs = debounceMs;
            _syncContext = SynchronizationContext.Current;
            _batchTimer = new Timer(OnTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);
        }

        public void SetAndNotify<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                QueueNotification(propertyName);
            }
        }

        public void QueueNotification([CallerMemberName] string? propertyName = null)
        {
            if (string.IsNullOrEmpty(propertyName)) return;

            lock (_lock)
            {
                _pendingProperties.Add(propertyName);

                if (!_isScheduled)
                {
                    _isScheduled = true;
                    _batchTimer.Change(_throttleMs, Timeout.Infinite);
                }
            }
        }

        public void FlushNotifications()
        {
            List<string> propsToNotify;

            lock (_lock)
            {
                if (_pendingProperties.Count == 0) return;

                propsToNotify = new List<string>(_pendingProperties);
                _pendingProperties.Clear();
                _isScheduled = false;
            }

            if (_syncContext != null && SynchronizationContext.Current != _syncContext)
            {
                _syncContext.Post(_ =>
                {
                    foreach (var prop in propsToNotify)
                    {
                        OnPropertyChanged(prop);
                    }
                }, null);
            }
            else
            {
                foreach (var prop in propsToNotify)
                {
                    OnPropertyChanged(prop);
                }
            }
        }

        private void OnTimerElapsed(object? state)
        {
            FlushNotifications();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
