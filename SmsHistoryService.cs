using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WebScraper
{
    public class SmsHistoryService
    {
        private readonly string _historyFilePath;
        private readonly ObservableCollection<SmsHistoryItem> _smsHistory;
        
        public ObservableCollection<SmsHistoryItem> SmsHistory => _smsHistory;

        public SmsHistoryService()
        {
            _historyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sms_history.json");
            _smsHistory = new ObservableCollection<SmsHistoryItem>();
            
            // UI thread'de deadlock'u önlemek için async olarak yükle
            Task.Run(async () =>
            {
                try
                {
                    await LoadHistoryAsync();
                }
                catch (Exception ex)
                {
                    // Hata durumunda boş koleksiyon ile devam et
                }
            });
        }

        /// <summary>
        /// SMS geçmişine yeni kayıt ekler
        /// </summary>
        public async Task AddSmsRecordAsync(string recipientName, string phoneNumber, string periodName, string status = "Başarılı")
        {
            var historyItem = new SmsHistoryItem
            {
                RecipientName = recipientName,
                PhoneNumber = phoneNumber,
                PeriodName = periodName,
                SentTime = DateTime.Now,
                Status = status
            };

            // UI thread'de koleksiyona ekle
            await Task.Run(() =>
            {
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Aynı kişi ve dönem için zaten kayıt var mı kontrol et
                        var existingRecord = _smsHistory.FirstOrDefault(x => 
                            x.RecipientName == historyItem.RecipientName && 
                            x.PhoneNumber == historyItem.PhoneNumber && 
                            x.PeriodName == historyItem.PeriodName);
                        
                        if (existingRecord == null)
                        {
                            // Yeni kayıt ekle
                            _smsHistory.Insert(0, historyItem);
                        }
                        else
                        {
                            // Mevcut kaydı güncelle (durum ve zaman)
                            existingRecord.Status = historyItem.Status;
                            existingRecord.SentTime = historyItem.SentTime;
                            
                            // Kaydı en üste taşı
                            _smsHistory.Remove(existingRecord);
                            _smsHistory.Insert(0, existingRecord);
                        }
                        
                        // Maksimum 1000 kayıt tut
                        if (_smsHistory.Count > 1000)
                        {
                            _smsHistory.RemoveAt(_smsHistory.Count - 1);
                        }
                    });
                }
            });

            // Dosyaya kaydet
            await SaveHistoryAsync();
        }

        /// <summary>
        /// Birden fazla SMS kaydını toplu olarak ekler
        /// </summary>
        public async Task AddBulkSmsRecordsAsync(List<SmsRecipientInfo> recipients, string periodName, string status = "Başarılı")
        {
            var newRecords = new List<SmsHistoryItem>();
            
            foreach (var recipient in recipients)
            {
                var historyItem = new SmsHistoryItem
                {
                    RecipientName = recipient.Name,
                    PhoneNumber = recipient.Phone,
                    PeriodName = periodName,
                    SentTime = DateTime.Now,
                    Status = status
                };
                newRecords.Add(historyItem);
            }

            // UI thread'de koleksiyona ekle
            await Task.Run(() =>
            {
                if (System.Windows.Application.Current != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var record in newRecords)
                        {
                            // Aynı kişi ve dönem için zaten kayıt var mı kontrol et
                            var existingRecord = _smsHistory.FirstOrDefault(x => 
                                x.RecipientName == record.RecipientName && 
                                x.PhoneNumber == record.PhoneNumber && 
                                x.PeriodName == record.PeriodName);
                            
                            if (existingRecord == null)
                            {
                                // Yeni kayıt ekle
                                _smsHistory.Insert(0, record);
                            }
                            else
                            {
                                // Mevcut kaydı güncelle (durum ve zaman)
                                existingRecord.Status = record.Status;
                                existingRecord.SentTime = record.SentTime;
                                
                                // Kaydı en üste taşı
                                _smsHistory.Remove(existingRecord);
                                _smsHistory.Insert(0, existingRecord);
                            }
                        }
                        
                        // Maksimum 1000 kayıt tut
                        while (_smsHistory.Count > 1000)
                        {
                            _smsHistory.RemoveAt(_smsHistory.Count - 1);
                        }
                    });
                }
            });

            // Dosyaya kaydet
            await SaveHistoryAsync();
        }

        /// <summary>
        /// Geçmişi dosyadan yükler
        /// </summary>
        private async Task LoadHistoryAsync()
        {
            try
            {
                if (File.Exists(_historyFilePath))
                {
                    var jsonContent = await File.ReadAllTextAsync(_historyFilePath);
                    if (!string.IsNullOrEmpty(jsonContent))
                    {
                        var historyList = JsonSerializer.Deserialize<List<SmsHistoryItem>>(jsonContent);
                        if (historyList != null)
                        {
                            if (System.Windows.Application.Current != null)
                            {
                                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                                {
                                    _smsHistory.Clear();
                                    foreach (var item in historyList.OrderByDescending(x => x.SentTime))
                                    {
                                        _smsHistory.Add(item);
                                    }
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda boş koleksiyon ile devam et
                System.Diagnostics.Debug.WriteLine($"SMS geçmişi yüklenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Geçmişi dosyaya kaydeder
        /// </summary>
        private async Task SaveHistoryAsync()
        {
            try
            {
                var historyList = _smsHistory.ToList();
                var jsonContent = JsonSerializer.Serialize(historyList, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                await File.WriteAllTextAsync(_historyFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SMS geçmişi kaydedilirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Geçmişi temizler
        /// </summary>
        public async Task ClearHistoryAsync()
        {
            if (System.Windows.Application.Current != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _smsHistory.Clear();
                });
            }
            
            await SaveHistoryAsync();
        }

        /// <summary>
        /// Belirli bir tarih aralığındaki kayıtları getirir
        /// </summary>
        public List<SmsHistoryItem> GetHistoryByDateRange(DateTime startDate, DateTime endDate)
        {
            return _smsHistory
                .Where(x => x.SentTime >= startDate && x.SentTime <= endDate)
                .OrderByDescending(x => x.SentTime)
                .ToList();
        }

        /// <summary>
        /// Belirli bir dönem için kayıtları getirir
        /// </summary>
        public List<SmsHistoryItem> GetHistoryByPeriod(string periodName)
        {
            return _smsHistory
                .Where(x => x.PeriodName.Contains(periodName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.SentTime)
                .ToList();
        }

        /// <summary>
        /// Belirli bir alıcı için kayıtları getirir
        /// </summary>
        public List<SmsHistoryItem> GetHistoryByRecipient(string recipientName)
        {
            return _smsHistory
                .Where(x => x.RecipientName.Contains(recipientName, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.SentTime)
                .ToList();
        }

        /// <summary>
        /// Bugün gönderilen SMS sayısını getirir
        /// </summary>
        public int GetTodaySmsCount()
        {
            var today = DateTime.Today;
            return _smsHistory.Count(x => x.SentTime.Date == today);
        }

        /// <summary>
        /// Bu hafta gönderilen SMS sayısını getirir
        /// </summary>
        public int GetThisWeekSmsCount()
        {
            var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            return _smsHistory.Count(x => x.SentTime >= startOfWeek);
        }

        /// <summary>
        /// Bu ay gönderilen SMS sayısını getirir
        /// </summary>
        public int GetThisMonthSmsCount()
        {
            var startOfMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            return _smsHistory.Count(x => x.SentTime >= startOfMonth);
        }
    }
} 