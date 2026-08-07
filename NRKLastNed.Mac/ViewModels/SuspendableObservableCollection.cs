using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NRKLastNed.Mac.ViewModels
{
    /// <summary>
    /// Suspendable ObservableCollection for efficient batch updates.
    /// Suppresses CollectionChanged notifications during bulk operations,
    /// then fires a single notification with Reset action when resumed.
    /// 
    /// This significantly improves UI performance when adding many items in rapid succession.
    /// </summary>
    public class SuspendableObservableCollection<T> : ObservableCollection<T>
    {
        private int _suspendCount = 0;
        private bool _hasChangesWhileSuspended = false;

        /// <summary>
        /// Suspends CollectionChanged notifications. Multiple calls nest; 
        /// notifications resume only when Resume() has been called the same number of times.
        /// </summary>
        public void Suspend()
        {
            _suspendCount++;
            _hasChangesWhileSuspended = false;
        }

        /// <summary>
        /// Resumes CollectionChanged notifications. If changes occurred while suspended,
        /// fires a single CollectionChanged event with action=Reset.
        /// </summary>
        public void Resume()
        {
            if (_suspendCount > 0)
            {
                _suspendCount--;
                if (_suspendCount == 0 && _hasChangesWhileSuspended)
                {
                    // Fire a single Reset event for all accumulated changes
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                    _hasChangesWhileSuspended = false;
                }
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (_suspendCount > 0)
            {
                // Mark that changes occurred, but suppress the notification
                _hasChangesWhileSuspended = true;
            }
            else
            {
                // Fire notification immediately when not suspended
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (_suspendCount > 0)
            {
                // Suppress property changed notifications during suspend
                return;
            }
            base.OnPropertyChanged(e);
        }
    }
}
