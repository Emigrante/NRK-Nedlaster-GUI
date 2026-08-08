using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using NRKLastNed.Models;
using NRKLastNed.ViewModels;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace NRKLastNed.Benchmarks
{
    [MemoryDiagnoser]
    public class PerformanceBenchmarks
    {
        private SuspendableObservableCollection<DownloadItem> _suspendableCollection;
        private ObservableCollection<DownloadItem> _regularCollection;
        private List<DownloadItem> _testItems;
        private PropertyBatcher _progressBatcher;
        private double _progress;

        [GlobalSetup]
        public void Setup()
        {
            _suspendableCollection = new SuspendableObservableCollection<DownloadItem>();
            _regularCollection = new ObservableCollection<DownloadItem>();
            _progressBatcher = new PropertyBatcher(50);
            _progress = 0;

            // Create test items representing 100 media files
            _testItems = new List<DownloadItem>();
            for (int i = 0; i < 100; i++)
            {
                _testItems.Add(new DownloadItem
                {
                    Title = $"Media_{i}",
                    SeasonEpisode = $"S01E{i:D2}",
                    Url = $"https://tv.nrk.no/program/{i}",
                    SelectedResolution = "1080p",
                    SelectedLanguage = "Norsk"
                });
            }
        }

        [Benchmark(Description = "Regular Collection Add x100 (baseline)")]
        public void RegularCollectionAdd()
        {
            _regularCollection.Clear();
            foreach (var item in _testItems)
            {
                _regularCollection.Add(item);
            }
        }

        [Benchmark(Description = "SuspendableCollection Add x100 (Suspend/Resume)")]
        public void SuspendableCollectionAdd()
        {
            _suspendableCollection.Clear();
            _suspendableCollection.Suspend();
            try
            {
                foreach (var item in _testItems)
                {
                    _suspendableCollection.Add(item);
                }
            }
            finally
            {
                _suspendableCollection.Resume();
            }
        }

        [Benchmark(Description = "PropertyBatcher NotifyProperty (batch mode)")]
        public void PropertyBatcherNotify()
        {
            // Simulate 100 rapid property updates
            for (int i = 0; i < 100; i++)
            {
                _progressBatcher.SetAndNotify(ref _progress, i, nameof(_progress));
            }
        }

        [Benchmark(Description = "Direct PropertyChanged (no batching)")]
        public void DirectPropertyChangedNotify()
        {
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++)
            {
                // Simulating rapid updates without batching
                // This would normally trigger PropertyChanged 100 times
                _ = i.ToString();
            }

            sw.Stop();
        }
    }
}