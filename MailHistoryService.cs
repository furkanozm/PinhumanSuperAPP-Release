using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace WebScraper
{
    public class MailHistoryService
    {
        private readonly string _historyFilePath = "mail_history.json";
        private List<MailHistoryModel> _mailHistory;

        public MailHistoryService()
        {
            LoadMailHistory();
        }

        public List<MailHistoryModel> GetAllMailHistory()
        {
            try
            {
                // Mail geçmişini yeniden yükle
                LoadMailHistory();
                
                var result = _mailHistory.OrderByDescending(x => x.Timestamp).ToList();
                System.Diagnostics.Debug.WriteLine($"GetAllMailHistory: {result.Count} kayıt döndürüldü");
                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetAllMailHistory hatası: {ex.Message}");
                return new List<MailHistoryModel>();
            }
        }

        public void AddMailHistory(MailHistoryModel mailHistory)
        {
            _mailHistory.Add(mailHistory);
            SaveMailHistory();
        }

        public void UpdateMailHistory(MailHistoryModel mailHistory)
        {
            var existing = _mailHistory.FirstOrDefault(x => x.Id == mailHistory.Id);
            if (existing != null)
            {
                var index = _mailHistory.IndexOf(existing);
                _mailHistory[index] = mailHistory;
                SaveMailHistory();
            }
        }

        public void DeleteMailHistory(string id)
        {
            var mail = _mailHistory.FirstOrDefault(x => x.Id == id);
            if (mail != null)
            {
                _mailHistory.Remove(mail);
                SaveMailHistory();
            }
        }

        public void ClearAllHistory()
        {
            _mailHistory.Clear();
            SaveMailHistory();
        }

        public List<MailHistoryModel> SearchMailHistory(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return GetAllMailHistory();

            return _mailHistory.Where(x => 
                x.Recipient.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Subject.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                x.Status.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
            ).OrderByDescending(x => x.Timestamp).ToList();
        }

        public void LoadMailHistory()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var json = File.ReadAllText(_historyFilePath);
                    _mailHistory = JsonConvert.DeserializeObject<List<MailHistoryModel>>(json) ?? new List<MailHistoryModel>();
                    
                    // Eksik alanları varsayılan değerlerle doldur
                    foreach (var mail in _mailHistory)
                    {
                        if (string.IsNullOrEmpty(mail.DeliveryType))
                        {
                            mail.DeliveryType = "Otomatik";
                        }
                        if (mail.Attachments == null)
                        {
                            mail.Attachments = new List<string>();
                        }
                    }
                }
                else
                {
                    _mailHistory = new List<MailHistoryModel>();
                }
            }
            catch (Exception)
            {
                _mailHistory = new List<MailHistoryModel>();
            }
        }

        private void SaveMailHistory()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_mailHistory, Formatting.Indented);
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                // Log error if needed
                System.Diagnostics.Debug.WriteLine($"Mail history save error: {ex.Message}");
            }
        }
    }
} 