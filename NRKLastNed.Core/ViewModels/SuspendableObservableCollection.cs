using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NRKLastNed.Core.ViewModels
{
    public class SuspendableObservableCollection<T> : ObservableCollection<T>
    {
        private int _suspendCount = 0;
        private bool _hasChangesWhileSuspended = false;

        public bool IsSuspended => _suspendCount > 0;

        public void Suspend()
        {
            if (_suspendCount == 0)
            {
                _hasChangesWhileSuspended = false;
            }
            _suspendCount++;
        }

        public void Resume()
        {
            if (_suspendCount > 0)
            {
                _suspendCount--;

                if (_suspendCount == 0 && _hasChangesWhileSuspended)
                {
                    _hasChangesWhileSuspended = false;
                    OnPropertyChanged(new PropertyChangedEventArgs("Count"));
                    OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                    OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
                }
            }
        }

        public void AddRange(IEnumerable<T> items)
        {
            Suspend();
            try
            {
                foreach (var item in items)
                {
                    Items.Add(item);
                }
                _hasChangesWhileSuspended = true;
            }
            finally
            {
                Resume();
            }
        }

        protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            if (IsSuspended)
            {
                _hasChangesWhileSuspended = true;
            }
            else
            {
                base.OnCollectionChanged(e);
            }
        }

        protected override void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            if (IsSuspended)
            {
                _hasChangesWhileSuspended = true;
            }
            else
            {
                base.OnPropertyChanged(e);
            }
        }
    }
}
