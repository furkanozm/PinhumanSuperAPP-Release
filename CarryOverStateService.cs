using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WebScraper
{
    // Backward compatibility için eski model (deprecated - sadece eski verileri okumak için)
    public class PersonnelCarryOverState
    {
        public string PersonnelCode { get; set; } = string.Empty;
        public string PersonnelName { get; set; } = string.Empty;
        public string LastShiftType { get; set; } = string.Empty;
        public DateTime LastWorkDate { get; set; }
        public int ConsecutiveWorkDays { get; set; }
        public double LastWorkedHours { get; set; }
    }

    public sealed class CarryOverStateService
    {
        private readonly string rootFolder;
        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public CarryOverStateService()
        {
            rootFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "carryover-data");
        }

        public bool HasSnapshot(string? companyCode, int year, int month)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(companyCode))
                {
                    return false;
                }

                var prev = GetPreviousMonth(year, month);
                string filePath = BuildSnapshotPath(companyCode, prev.year, prev.month);
                return File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        public Dictionary<string, PersonnelCarryOverState> LoadPreviousMonthStates(string? companyCode, int year, int month)
        {
            var prev = GetPreviousMonth(year, month);
            return LoadMonthStates(companyCode, prev.year, prev.month);
        }

        public Dictionary<string, PersonnelCarryOverState> LoadMonthStates(string? companyCode, int year, int month)
        {
            var result = new Dictionary<string, PersonnelCarryOverState>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return result;
            }

            try
            {
                string filePath = BuildSnapshotPath(companyCode, year, month);

                if (!File.Exists(filePath))
                {
                    return result;
                }

                string json = File.ReadAllText(filePath);
                var list = JsonSerializer.Deserialize<List<PersonnelCarryOverState>>(json);
                if (list == null)
                {
                    return result;
                }

                // Dummy verileri filtrele
                var dummyPersonnelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "12345", "67890" };
                var dummyPersonnelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ahmet Yılmaz", "Ayşe Demir" };

                foreach (var state in list.Where(s => 
                    !string.IsNullOrWhiteSpace(s.PersonnelCode) &&
                    !dummyPersonnelCodes.Contains(s.PersonnelCode) &&
                    !string.IsNullOrWhiteSpace(s.PersonnelName) &&
                    !dummyPersonnelNames.Contains(s.PersonnelName.Trim())))
                {
                    result[state.PersonnelCode] = state;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tatil Devir] {year}/{month:D2} ayı verisi yüklenemedi: {ex.Message}");
                File.AppendAllText("debug_log.txt", $"[Tatil Devir] {year}/{month:D2} ayı verisi yüklenemedi: {ex.Message}\n");
            }

            return result;
        }

        public bool HasMonthSnapshot(string? companyCode, int year, int month)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(companyCode))
                {
                    return false;
                }

                string filePath = BuildSnapshotPath(companyCode, year, month);
                return File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        public void SaveCurrentMonthStates(string? companyCode, int year, int month, IEnumerable<PersonnelCarryOverState> states)
        {
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return;
            }

            try
            {
                string filePath = BuildSnapshotPath(companyCode, year, month);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                var filteredStates = states?
                    .Where(s => s != null && !string.IsNullOrWhiteSpace(s.PersonnelCode) && s.ConsecutiveWorkDays > 0)
                    .Select(s => new PersonnelCarryOverState
                    {
                        PersonnelCode = s.PersonnelCode,
                        PersonnelName = s.PersonnelName,
                        LastShiftType = s.LastShiftType ?? string.Empty,
                        LastWorkDate = s.LastWorkDate,
                        ConsecutiveWorkDays = s.ConsecutiveWorkDays,
                        LastWorkedHours = s.LastWorkedHours
                    })
                    .ToList() ?? new List<PersonnelCarryOverState>();

                string json = JsonSerializer.Serialize(filteredStates, jsonOptions);
                File.WriteAllText(filePath, json);

                Console.WriteLine($"[Tatil Devir] {filteredStates.Count} kayıt {filePath} dosyasına kaydedildi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Tatil Devir] Devir verisi kaydedilemedi: {ex.Message}");
                File.AppendAllText("debug_log.txt", $"[Tatil Devir] Devir verisi kaydedilemedi: {ex.Message}\n");
            }
        }

        private (int year, int month) GetPreviousMonth(int year, int month)
        {
            if (month <= 1)
            {
                return (year - 1, 12);
            }

            return (year, month - 1);
        }

        private string BuildSnapshotPath(string companyCode, int year, int month)
        {
            string safeCompany = Sanitize(companyCode);
            string companyFolder = Path.Combine(rootFolder, safeCompany, year.ToString("0000"), month.ToString("00"));
            return Path.Combine(companyFolder, "carryover.json");
        }

        private static string Sanitize(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = input.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            return new string(chars);
        }

        // Yeni günlük kayıt metodları
        private string BuildStoredRecordsPath(string companyCode, int year, int month)
        {
            string safeCompany = Sanitize(companyCode);
            string companyFolder = Path.Combine(rootFolder, safeCompany, year.ToString("0000"), month.ToString("00"));
            return Path.Combine(companyFolder, "stored_pdks_records.json");
        }

        public void SaveStoredPDKSRecords(string? companyCode, int year, int month, IEnumerable<StoredPDKSRecord> records)
        {
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return;
            }

            try
            {
                string filePath = BuildStoredRecordsPath(companyCode, year, month);
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                var validRecords = records?
                    .Where(r => r != null && !string.IsNullOrWhiteSpace(r.PersonnelCode))
                    .ToList() ?? new List<StoredPDKSRecord>();

                string json = JsonSerializer.Serialize(validRecords, jsonOptions);
                File.WriteAllText(filePath, json);

                Console.WriteLine($"[Kayıtlı PDKS] {validRecords.Count} günlük kayıt {filePath} dosyasına kaydedildi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Kayıtlı PDKS] Kayıt verisi kaydedilemedi: {ex.Message}");
                File.AppendAllText("debug_log.txt", $"[Kayıtlı PDKS] Kayıt verisi kaydedilemedi: {ex.Message}\n");
            }
        }

        public List<StoredPDKSRecord> LoadStoredPDKSRecords(string? companyCode, int year, int month)
        {
            var result = new List<StoredPDKSRecord>();

            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return result;
            }

            try
            {
                string filePath = BuildStoredRecordsPath(companyCode, year, month);

                if (!File.Exists(filePath))
                {
                    return result;
                }

                string json = File.ReadAllText(filePath);
                var records = JsonSerializer.Deserialize<List<StoredPDKSRecord>>(json);
                if (records != null)
                {
                    // Tüm dummy verileri direkt sil (12345, 67890, Ahmet Yılmaz, Ayşe Demir)
                    var dummyPersonnelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "12345", "67890" };
                    var dummyPersonnelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ahmet Yılmaz", "Ayşe Demir" };
                    
                    var originalCount = records.Count;
                    records = records.Where(r => 
                        !string.IsNullOrWhiteSpace(r.PersonnelCode) &&
                        !dummyPersonnelCodes.Contains(r.PersonnelCode) &&
                        !string.IsNullOrWhiteSpace(r.PersonnelName) &&
                        !dummyPersonnelNames.Contains(r.PersonnelName.Trim())).ToList();
                    
                    // Eğer dummy veriler silindiyse, JSON dosyasını temizle
                    if (records.Count < originalCount)
                    {
                        int deletedCount = originalCount - records.Count;
                        SaveStoredPDKSRecords(companyCode, year, month, records);
                        Console.WriteLine($"[Kayıtlı PDKS] {deletedCount} dummy kayıt silindi ({companyCode}, {year}/{month:D2})");
                    }
                    
                    result = records;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Kayıtlı PDKS] {year}/{month:D2} ayı kayıtları yüklenemedi: {ex.Message}");
                File.AppendAllText("debug_log.txt", $"[Kayıtlı PDKS] {year}/{month:D2} ayı kayıtları yüklenemedi: {ex.Message}\n");
            }

            return result;
        }

        public bool HasStoredRecords(string? companyCode, int year, int month)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(companyCode))
                {
                    return false;
                }

                string filePath = BuildStoredRecordsPath(companyCode, year, month);
                return File.Exists(filePath);
            }
            catch
            {
                return false;
            }
        }

        public void DeleteStoredPDKSRecords(string? companyCode, int year, int month, IEnumerable<StoredPDKSRecord> recordsToDelete)
        {
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return;
            }

            try
            {
                // Mevcut kayıtları yükle
                var existingRecords = LoadStoredPDKSRecords(companyCode, year, month);
                
                // Silinecek kayıtları belirle (PersonnelCode, Date, CheckInTime, CheckOutTime kombinasyonuna göre)
                var recordsToDeleteSet = new HashSet<string>();
                foreach (var record in recordsToDelete)
                {
                    string key = $"{record.PersonnelCode}|{record.Date:yyyy-MM-dd}|{record.CheckInTime:hh\\:mm}|{record.CheckOutTime:hh\\:mm}";
                    recordsToDeleteSet.Add(key);
                }

                // Silinecek kayıtları filtrele
                var remainingRecords = existingRecords.Where(r =>
                {
                    string key = $"{r.PersonnelCode}|{r.Date:yyyy-MM-dd}|{r.CheckInTime:hh\\:mm}|{r.CheckOutTime:hh\\:mm}";
                    return !recordsToDeleteSet.Contains(key);
                }).ToList();

                // Güncellenmiş kayıtları kaydet
                SaveStoredPDKSRecords(companyCode, year, month, remainingRecords);

                Console.WriteLine($"[Kayıtlı PDKS] {recordsToDeleteSet.Count} kayıt silindi, {remainingRecords.Count} kayıt kaldı");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Kayıtlı PDKS] Kayıt silme hatası: {ex.Message}");
                File.AppendAllText("debug_log.txt", $"[Kayıtlı PDKS] Kayıt silme hatası: {ex.Message}\n");
                throw;
            }
        }

        /// <summary>
        /// Tüm firmalar ve aylar için tüm dummy kayıtları siler (12345, 67890, Ahmet Yılmaz, Ayşe Demir)
        /// </summary>
        public void CleanupAllDummyRecords()
        {
            try
            {
                if (!Directory.Exists(rootFolder))
                {
                    return;
                }

                var dummyPersonnelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "12345", "67890" };
                var dummyPersonnelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ahmet Yılmaz", "Ayşe Demir" };

                int totalDeleted = 0;
                var companyFolders = Directory.GetDirectories(rootFolder);
                
                foreach (var companyFolder in companyFolders)
                {
                    var yearFolders = Directory.GetDirectories(companyFolder);
                    foreach (var yearFolder in yearFolders)
                    {
                        var monthFolders = Directory.GetDirectories(yearFolder);
                        foreach (var monthFolder in monthFolders)
                        {
                            string filePath = Path.Combine(monthFolder, "stored_pdks_records.json");
                            if (File.Exists(filePath))
                            {
                                try
                                {
                                    string json = File.ReadAllText(filePath);
                                    var records = JsonSerializer.Deserialize<List<StoredPDKSRecord>>(json);
                                    if (records != null && records.Count > 0)
                                    {
                                        var originalCount = records.Count;
                                        records = records.Where(r => 
                                            !string.IsNullOrWhiteSpace(r.PersonnelCode) &&
                                            !dummyPersonnelCodes.Contains(r.PersonnelCode) &&
                                            !string.IsNullOrWhiteSpace(r.PersonnelName) &&
                                            !dummyPersonnelNames.Contains(r.PersonnelName?.Trim() ?? string.Empty)).ToList();
                                        
                                        if (records.Count < originalCount)
                                        {
                                            int deleted = originalCount - records.Count;
                                            totalDeleted += deleted;
                                            SaveStoredPDKSRecords(Path.GetFileName(companyFolder), 
                                                int.Parse(Path.GetFileName(yearFolder)), 
                                                int.Parse(Path.GetFileName(monthFolder)), 
                                                records);
                                            Console.WriteLine($"[Temizleme] {deleted} dummy kayıt silindi: {filePath}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"[Temizleme] {filePath} işlenirken hata: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                
                if (totalDeleted > 0)
                {
                    Console.WriteLine($"[Temizleme] Toplam {totalDeleted} dummy kayıt silindi");
                    File.AppendAllText("debug_log.txt", $"[Temizleme] Toplam {totalDeleted} dummy kayıt silindi\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Temizleme] Genel temizleme hatası: {ex.Message}");
                File.AppendAllText("debug_log.txt", $"[Temizleme] Genel temizleme hatası: {ex.Message}\n");
            }
        }
    }
}

