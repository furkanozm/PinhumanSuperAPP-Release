using System;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace WebScraper
{
    public class MailHistoryModel
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.Now;

        [JsonProperty("recipient")]
        public string Recipient { get; set; } = "";

        [JsonProperty("subject")]
        public string Subject { get; set; } = "";

        [JsonProperty("content")]
        public string Content { get; set; } = "";

        [JsonProperty("status")]
        public string Status { get; set; } = "Gönderildi";

        [JsonProperty("errorMessage")]
        public string ErrorMessage { get; set; } = "";

        [JsonProperty("attachmentCount")]
        public int AttachmentCount { get; set; } = 0;

        [JsonProperty("deliveryType")]
        public string DeliveryType { get; set; } = "Otomatik";

        [JsonProperty("attachments")]
        public List<string> Attachments { get; set; } = new List<string>();
    }
} 