﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using PureLib.Common.Entities;

namespace PureLib.Common {
    public class WebDownloader {
        private Dictionary<IAsyncWebClient, DownloadItem> _clientItemMaps;
        private List<DownloadItem> _items;
        private int _downloadingCount;

        public int ThreadCount { get; private set; }
        public bool IsStopped {
            get {
                return _clientItemMaps.Count == 0;
            }
        }

        public WebDownloader()
            : this(null, 1) {
        }

        public WebDownloader(int threadCount)
            : this(null, threadCount) {
        }

        public WebDownloader(List<DownloadItem> items, int threadCount) {
            SetThreadCount(threadCount);
            _clientItemMaps = new Dictionary<IAsyncWebClient, DownloadItem>();
            _items = items ?? new List<DownloadItem>();
        }

        public void StartDownloading() {
            for (int i = 0; i < ThreadCount; i++)
                Download();
        }

        public void SetThreadCount(int threadCount) {
            if (threadCount <= 0)
                throw new ArgumentOutOfRangeException("Thread count must be greater than zero.");

            ThreadCount = threadCount;
        }

        public void AddItem(DownloadItem item) {
            if (item == null)
                throw new ArgumentNullException("Download item is null.");

            _items.Add(item);
            if (item.State == DownloadItemState.Queued)
                Download();
        }

        public void StopAll() {
            foreach (DownloadItem i in _items.Where(i => i.State == DownloadItemState.Queued)) {
                i.State = DownloadItemState.Stopped;
            }
            foreach (var p in _clientItemMaps) {
                p.Key.CancelAsync();
            }
        }

        public void ResumeAll() {
            foreach (DownloadItem i in _items.Where(i => i.State == DownloadItemState.Stopped)) {
                i.State = DownloadItemState.Queued;
            }
            StartDownloading();
        }

        private void Download() {
            lock (this) {
                if (_downloadingCount < ThreadCount) {
                    DownloadItem item = _items.FirstOrDefault(i => i.State == DownloadItemState.Queued);
                    if (item != null) {
                        item.State = DownloadItemState.Downloading;
                        _downloadingCount++;

                        object[] parameters = new object[] { item.Referer, item.Cookies };
                        IAsyncWebClient client = File.Exists(item.Path) ?
                            (IAsyncWebClient)Utility.GetInstance<ResumableWebClient>(parameters) :
                            (IAsyncWebClient)Utility.GetInstance<AdvancedWebClient>(parameters);
                        _clientItemMaps.Add(client, item);
                        client.DownloadFileCompleted += new AsyncCompletedEventHandler(DownloadFileCompleted);
                        if (client is AdvancedWebClient)
                            ((AdvancedWebClient)client).DownloadProgressChanged += new DownloadProgressChangedEventHandler(DownloadProgressChanged);
                        client.DownloadFileAsync(new Uri(item.Url), item.Path);
                    }
                }
            }
        }

        private void DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e) {
            DownloadItem item = _clientItemMaps[(AdvancedWebClient)sender];
            item.TotalBytes = e.TotalBytesToReceive;
            item.ReceivedBytes = e.BytesReceived;
            item.Percentage = e.ProgressPercentage;
        }

        private void DownloadFileCompleted(object sender, AsyncCompletedEventArgs e) {
            dynamic client = sender;
            DownloadItem item = _clientItemMaps[client];
            _downloadingCount--;
            if (e.Cancelled) {
                item.State = DownloadItemState.Stopped;
                FileInfo file = new FileInfo(item.Path);
                if (file.Exists) {
                    item.ReceivedBytes = file.Length;
                    if (item.TotalBytes > 0)
                        item.Percentage = (int)((item.ReceivedBytes * 100) / item.TotalBytes);
                }
            }
            else {
                item.State = DownloadItemState.Completed;
                if (item.TotalBytes == 0)
                    item.TotalBytes = new FileInfo(item.Path).Length;
                item.ReceivedBytes = item.TotalBytes;
                item.Percentage = 100;
                Download();
            }
            _clientItemMaps.Remove(client);
            client.Dispose();
        }
    }
}
