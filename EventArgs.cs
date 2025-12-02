using System;
using System.ComponentModel;

namespace WebScraper
{
    public class StatusChangedEventArgs : EventArgs
    {
        public string Status { get; set; } = "";
        public string Detail { get; set; } = "";
        public StatusType StatusType { get; set; }
    }

    public class LogMessageEventArgs : EventArgs
    {
        public string Message { get; set; } = "";
    }

    public class FoundChangedEventArgs : EventArgs
    {
        public int FoundCount { get; set; }
    }

    public class DownloadedChangedEventArgs : EventArgs
    {
        public int DownloadedCount { get; set; }
    }

    public class TotalAmountChangedEventArgs : EventArgs
    {
        public decimal TotalAmount { get; set; }
    }
} 