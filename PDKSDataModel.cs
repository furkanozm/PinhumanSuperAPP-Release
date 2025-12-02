using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Windows.Markup;
using System.Linq; // Added for .Any() and .OrderBy()
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using OfficeOpenXml;

namespace WebScraper
{
    public class PersonnelSummary
    {
        public string PersonnelCode { get; set; } // Sicil numarası
        public string PersonnelName { get; set; } // Ad Soyad
        public string TCNo { get; set; }

        // Aylık toplamlar
        public double TotalWorkedHours { get; set; } // Toplam çalışma saati
        public double TotalFMNormalHours { get; set; } // Toplam fazla mesai normal
        public double TotalFM50PercentHours { get; set; } // Toplam fazla mesai %50
        public int TotalVacationDays { get; set; } // Toplam tatil günleri
        public int TotalAbsentDays { get; set; } // Toplam devamsızlık günleri
        public double TotalAbsentHours { get; set; } // Toplam eksik saat

        // Detaylı kırılımlar
        public int TotalWorkDays { get; set; } // Toplam çalışma gün sayısı
        public int TotalConsecutiveDays { get; set; } // Maksimum ardışık çalışma günü
        public double AverageDailyHours { get; set; } // Günlük ortalama çalışma saati

        // Vardiya bazlı kırılımlar
        public Dictionary<string, double> ShiftTypeHours { get; set; } // Vardiya türüne göre toplam saatler
        public Dictionary<string, double> FMColumnTotals { get; set; } // ERP kolonuna göre fazla mesai saatleri
        public string PrimaryShiftGroupName { get; set; } = string.Empty; // Eksik gün kolonu için baskın vardiya grubu

        public PersonnelSummary()
        {
            ShiftTypeHours = new Dictionary<string, double>();
            FMColumnTotals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            TotalFMNormalHours = 0;
            TotalFM50PercentHours = 0;
            TotalVacationDays = 0;
            TotalAbsentDays = 0;
            TotalAbsentHours = 0;
            TotalWorkDays = 0;
            TotalConsecutiveDays = 0;
            AverageDailyHours = 0;
        }

        public void CalculateAverages()
        {
            if (TotalWorkDays > 0)
            {
                AverageDailyHours = TotalWorkedHours / TotalWorkDays;
            }
        }
    }

    public class PDKSDataModel
    {
        public string PersonnelCode { get; set; } // Sicil numarası
        public string PersonnelName { get; set; } // Ad Soyad
        public string TCNo { get; set; }
        public DateTime Date { get; set; }
        public string ShiftType { get; set; } // 8/18, 8/19, Normal, vb.
        public TimeSpan CheckInTime { get; set; }
        public TimeSpan CheckOutTime { get; set; }
        public double WorkedHours { get; set; }
        public bool IsMatched { get; set; }
        public PersonnelRecord MatchedPersonnel { get; set; }
        public string MatchedPersonnelCode { get; set; }
        public string MatchedPersonnelName { get; set; }
        public string MatchType { get; set; } = "İsim Benzerliği";

        // Hesaplanan alanlar
        public double FMNormalHours { get; set; } // Fazla mesai normal
        public double FM50PercentHours { get; set; } // Fazla mesai %50
        public int VacationDays { get; set; } // Tatil günleri
        public int AbsentDays { get; set; } // Devamsızlık günleri
        public double AbsentHours { get; set; } // Eksik saat
        public Dictionary<string, double> FMColumnHours { get; set; } // ERP kolonuna göre günlük fazla mesai saatleri
        public bool WorkedOnEarnedRestDay { get; set; } // Hak edilen tatil gününde çalıştı mı
        public bool WorkedOnOfficialHoliday { get; set; } // Resmi tatilde çalıştı mı
        public bool IsOfficialHoliday { get; set; } // Kayıt resmi tatil gününe mi ait
        public string HolidayName { get; set; }
        public bool IsHalfDayHoliday { get; set; }
        public string EarnedRestSourceRange { get; set; }
        public string EarnedRestRuleName { get; set; }
        public int EarnedRestRequiredDays { get; set; }
        public DateTime? SpecialOvertimeEffectiveDate { get; set; }

        public PDKSDataModel()
        {
            IsMatched = false;
            FMNormalHours = 0;
            FM50PercentHours = 0;
            VacationDays = 0;
            AbsentDays = 0;
            AbsentHours = 0;
            FMColumnHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            WorkedOnEarnedRestDay = false;
            WorkedOnOfficialHoliday = false;
            IsOfficialHoliday = false;
            HolidayName = string.Empty;
            IsHalfDayHoliday = false;
            EarnedRestSourceRange = string.Empty;
            EarnedRestRuleName = string.Empty;
            EarnedRestRequiredDays = 0;
            SpecialOvertimeEffectiveDate = null;
        }
    }

    public class CarryOverSnapshotResult
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public List<PersonnelCarryOverState> States { get; set; } = new List<PersonnelCarryOverState>(); // Backward compatibility
        public List<StoredPDKSRecord> Records { get; set; } = new List<StoredPDKSRecord>(); // Yeni günlük kayıtlar
    }

    public class PDKSDataService
    {
        private List<PDKSDataModel> pdksRecords;
        private readonly CalendarService calendarService = new CalendarService();
        private readonly CarryOverStateService carryOverStateService = new CarryOverStateService();
        private readonly Dictionary<string, PersonnelCarryOverState> previousCarryOverStates = new Dictionary<string, PersonnelCarryOverState>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PersonnelCarryOverState> currentCarryOverStates = new Dictionary<string, PersonnelCarryOverState>(StringComparer.OrdinalIgnoreCase);
        private bool includePreviousMonthCarryOver = false;
        private CompanyConfig? activeCarryOverCompany;
        
        // Bir önceki aydan devir eden günlerle tatil hakkı kazanıldığında log mesajları
        private static List<string> carryOverVacationLogs = new List<string>();

        public PDKSDataService()
        {
            pdksRecords = new List<PDKSDataModel>();
        }

        /// <summary>
        /// Bir önceki aydan devir eden günlerle tatil hakkı kazanıldığında log mesajı ekler
        /// </summary>
        private static void AddCarryOverVacationLog(string logMessage)
        {
            carryOverVacationLogs.Add(logMessage);
        }

        /// <summary>
        /// Bir önceki aydan devir eden günlerle tatil hakkı kazanıldığında log mesajlarını alır ve temizler
        /// </summary>
        public static List<string> GetAndClearCarryOverVacationLogs()
        {
            var logs = new List<string>(carryOverVacationLogs);
            carryOverVacationLogs.Clear();
            return logs;
        }

        public void ConfigureCarryOverTracking(CompanyConfig? companyConfig, bool includePreviousMonthData)
        {
            activeCarryOverCompany = companyConfig;
            includePreviousMonthCarryOver = includePreviousMonthData;
            previousCarryOverStates.Clear();
            currentCarryOverStates.Clear();

            if (companyConfig == null)
            {
                return;
            }

            if (includePreviousMonthData)
            {
                int year = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
                int month = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;
                var prevPeriod = GetPreviousPeriod(year, month);
                
                // Önce yeni format StoredPDKSRecords'ları yükle
                var storedRecords = carryOverStateService.LoadStoredPDKSRecords(
                    companyConfig.CompanyCode,
                    prevPeriod.year,
                    prevPeriod.month);
                
                if (storedRecords != null && storedRecords.Count > 0)
                {
                    // StoredPDKSRecords'ları PDKSDataModel'e dönüştür ve işleme dahil et
                    foreach (var storedRecord in storedRecords)
                    {
                        var pdksRecord = new PDKSDataModel
                        {
                            PersonnelCode = storedRecord.PersonnelCode,
                            PersonnelName = storedRecord.PersonnelName,
                            Date = storedRecord.Date,
                            CheckInTime = storedRecord.CheckInTime,
                            CheckOutTime = storedRecord.CheckOutTime,
                            ShiftType = storedRecord.ShiftType,
                            IsMatched = true // Eşleştirilmiş olarak işaretle
                        };
                        
                        // Çalışma saatini hesapla
                        if (pdksRecord.CheckOutTime > pdksRecord.CheckInTime)
                        {
                            pdksRecord.WorkedHours = (pdksRecord.CheckOutTime - pdksRecord.CheckInTime).TotalHours;
                        }
                        
                        // Önceki ayın kayıtlarını ekle (tatil hakları hesaplaması için)
                        if (!pdksRecords.Any(r => r.PersonnelCode == pdksRecord.PersonnelCode && r.Date == pdksRecord.Date))
                        {
                            pdksRecords.Add(pdksRecord);
                        }
                    }
                    
                    Console.WriteLine($"[Tatil Devir] {storedRecords.Count} StoredPDKSRecord yüklendi ({companyConfig.CompanyCode})");
                    File.AppendAllText("debug_log.txt", $"[Tatil Devir] {storedRecords.Count} StoredPDKSRecord yüklendi ({companyConfig.CompanyCode})\n");
                }
                
                // Backward compatibility: Eski formatı da yükle
                var loadedStates = carryOverStateService.LoadPreviousMonthStates(
                    companyConfig.CompanyCode,
                    year,
                    month);

                foreach (var kvp in loadedStates)
                {
                    previousCarryOverStates[kvp.Key] = kvp.Value;
                }

                Console.WriteLine($"[Tatil Devir] {loadedStates.Count} personel için önceki ay devir verisi yüklendi ({companyConfig.CompanyCode})");
                File.AppendAllText("debug_log.txt", $"[Tatil Devir] {loadedStates.Count} personel için devir verisi yüklendi ({companyConfig.CompanyCode})\n");
            }
        }

        public bool HasPreviousCarryOverSnapshot(CompanyConfig? companyConfig)
        {
            if (companyConfig == null)
            {
                return false;
            }

            int year = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            int month = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;

            return carryOverStateService.HasSnapshot(companyConfig.CompanyCode, year, month);
        }

        public CarryOverSnapshotResult GetPreviousCarryOverSnapshot(CompanyConfig companyConfig)
        {
            var result = new CarryOverSnapshotResult();
            if (companyConfig == null)
            {
                return result;
            }

            int currentYear = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            int currentMonth = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;
            var prev = GetPreviousPeriod(currentYear, currentMonth);
            return GetCarryOverSnapshot(companyConfig, prev.year, prev.month);
        }

        public CarryOverSnapshotResult GetCarryOverSnapshot(CompanyConfig companyConfig, int year, int month)
        {
            var result = new CarryOverSnapshotResult();
            if (companyConfig == null)
            {
                return result;
            }

            result.Year = year;
            result.Month = month;

            // Önce yeni günlük kayıt formatını yükle
            var storedRecords = carryOverStateService.LoadStoredPDKSRecords(
                companyConfig.CompanyCode,
                year,
                month);

            if (storedRecords != null && storedRecords.Count > 0)
            {
                result.Records = storedRecords
                    .OrderByDescending(r => r.Date)
                    .ThenBy(r => r.PersonnelName)
                    .ThenBy(r => r.CheckInTime)
                    .ToList();
            }
            else
            {
                // Backward compatibility: Eski formatı yükle
                var snapshotDict = carryOverStateService.LoadMonthStates(
                    companyConfig.CompanyCode,
                    year,
                    month);

                // Dummy verileri filtrele (zaten LoadMonthStates içinde filtrelenmiş ama ekstra güvenlik için)
                var dummyPersonnelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "12345", "67890" };
                var dummyPersonnelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ahmet Yılmaz", "Ayşe Demir" };

                result.States = snapshotDict.Values
                    .Where(s => 
                        !dummyPersonnelCodes.Contains(s.PersonnelCode) &&
                        !dummyPersonnelNames.Contains(s.PersonnelName?.Trim() ?? string.Empty))
                    .OrderByDescending(s => s.LastWorkDate)
                    .ThenByDescending(s => s.ConsecutiveWorkDays)
                    .ToList();
            }

            return result;
        }

        public bool HasMonthSnapshot(CompanyConfig? companyConfig, int year, int month)
        {
            if (companyConfig == null)
            {
                return false;
            }

            return carryOverStateService.HasMonthSnapshot(companyConfig.CompanyCode, year, month);
        }

        private string? FindPythonExecutable()
        {
            // Windows Python Launcher'ı dene (en güvenilir)
            string[] pythonCommands = { "py", "python", "python3" };
            
            foreach (var cmd in pythonCommands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(2000); // 2 saniye timeout
                        if (process.ExitCode == 0 || process.ExitCode == 1) // --version genelde 0 veya 1 döner
                        {
                            Console.WriteLine($"[Python] Bulundu: {cmd}");
                            return cmd;
                        }
                    }
                }
                catch
                {
                    // Bu komut bulunamadı, bir sonrakini dene
                    continue;
                }
            }
            
            return null;
        }

        private class OvertimeAllocationResult
        {
            public double NormalHours { get; set; }
            public double SpecialHours { get; set; }
            public Dictionary<string, double> ColumnHours { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        private class EarnedRestDayDetail
        {
            public DateTime RestDate { get; set; }
            public DateTime RangeStart { get; set; }
            public DateTime RangeEnd { get; set; }
            public string RuleName { get; set; } = string.Empty;
            public int RequiredConsecutiveDays { get; set; }
        }

        public List<PDKSDataModel> GetAllPDKSRecords()
        {
            return pdksRecords;
        }

        public void LoadPDKSData(string filePath)
        {
            pdksRecords.Clear();

            try
            {
                // Python'u bul
                string? pythonCmd = FindPythonExecutable();
                if (pythonCmd == null)
                {
                    throw new Exception("Python bulunamadı. Lütfen Python'u yükleyin ve PATH'e ekleyin, veya Windows Python Launcher (py) kullanın.");
                }

                // Python ile PDKS verilerini oku - hampdks.xlsx formatına göre
                // Sicil | Ad Soyad | Giriş Saati | Çıkış Saati | TARİH | Vardiya
                // Python script'ini ayrı bir string olarak hazırla
                string pythonScript = @"
import pandas as pd
import sys
import json

file_path = r'" + filePath.Replace("\\", "\\\\") + @"'
df = pd.read_excel(file_path)

# Debug bilgilerini stderr'e yaz (console'a gitmesin)
sys.stderr.write(f'DataFrame shape: {df.shape}\n')
sys.stderr.write('İlk 5 satır:\n')
sys.stderr.write(str(df.head()) + '\n')

records = []
for _, row in df.iterrows():
    records.append({
        'personnelCode': str(row[0]).strip() if pd.notna(row[0]) else '',
        'name': str(row[1]).strip() if pd.notna(row[1]) else '',
        'checkin': str(row[2]) if pd.notna(row[2]) else '',
        'checkout': str(row[3]) if pd.notna(row[3]) else '',
        'date': str(row[4]) if pd.notna(row[4]) else '',
        'shift': str(row[5]) if pd.notna(row[5]) else ''
    })

sys.stderr.write(f'Total records created: {len(records)}\n')
print(json.dumps(records))
";

                var psi = new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = $"-c \"{pythonScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    throw new Exception("Python işlemi başlatılamadı");
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    // Debug bilgilerini stderr'den oku
                    Console.WriteLine($"[PDKS Load] Debug info:\n{error}");

                    // JSON parse et (stdout'tan)
                    var records = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(output);

                    Console.WriteLine($"[PDKS Load] JSON parsed, {records.Count} records");

                    foreach (var record in records)
                    {
                        var pdksRecord = new PDKSDataModel
                        {
                            PersonnelCode = record["personnelCode"],
                            PersonnelName = record["name"],
                            ShiftType = record["shift"]
                        };

                        // Tarih parse et (boş string kontrolü ile)
                        if (!string.IsNullOrEmpty(record["date"]) && DateTime.TryParse(record["date"], out DateTime parsedDate))
                        {
                            pdksRecord.Date = parsedDate;
                        }
                        else
                        {
                            Console.WriteLine($"[PDKS Load] Uyarı: Geçersiz tarih formatı: '{record["date"]}' - Varsayılan tarih kullanılıyor");
                            pdksRecord.Date = DateTime.Now.Date; // Varsayılan tarih
                        }

                        // Check-in/out zamanlarını parse et
                        if (TimeSpan.TryParse(record["checkin"], out TimeSpan checkIn))
                            pdksRecord.CheckInTime = checkIn;
                        if (TimeSpan.TryParse(record["checkout"], out TimeSpan checkOut))
                            pdksRecord.CheckOutTime = checkOut;

                        // Çalışma saati burada hesaplanmayacak - FM hesaplama kısmında molayı çıkararak hesaplanacak
                        // Şimdilik toplam süreyi sakla
                        if (pdksRecord.CheckOutTime > pdksRecord.CheckInTime)
                        {
                            pdksRecord.WorkedHours = (pdksRecord.CheckOutTime - pdksRecord.CheckInTime).TotalHours;
                        }

                        pdksRecords.Add(pdksRecord);
                        Console.WriteLine($"[PDKS Load] Loaded: '{pdksRecord.PersonnelName}' ({pdksRecord.PersonnelCode}) - {pdksRecord.Date:yyyy-MM-dd} - {pdksRecord.ShiftType}");
                    }
                }
                else
                {
                    throw new Exception($"PDKS verisi okunurken hata: {error}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"PDKS verisi işlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Yatay puantaj (satır başına personel, sütunlarda gün bazında çalışma saati) şablonundan PDKS kayıtları üretir.
        /// </summary>
        public void LoadHorizontalDailyHours(string filePath, DataTemplate template, int year, int month, Action<string>? logAction = null, CompanyConfig companyConfig = null)
        {
            pdksRecords.Clear();

            try
            {
                OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

                using var package = new ExcelPackage(new FileInfo(filePath));
                var sheet = package.Workbook.Worksheets.FirstOrDefault();
                if (sheet == null || sheet.Dimension == null)
                {
                    throw new Exception("Excel dosyasında çalışma sayfası bulunamadı.");
                }

                var log = logAction ?? ((string msg) => Console.WriteLine(msg));
                log($"[Horizontal PDKS] Şablon: {template.Name} (Tür: {template.TemplateType}) - Dosya: {Path.GetFileName(filePath)}");
                if (template.SymbolHourMap != null && template.SymbolHourMap.Count > 0)
                {
                    log($"[Horizontal PDKS] Sembolik saat eşleştirmeleri ({template.SymbolHourMap.Count} adet):");
                    foreach (var kvp in template.SymbolHourMap)
                    {
                        log($"  '{kvp.Key}' => {kvp.Value} saat");
                    }
                    // Comparer kontrolü
                    var testKey = "X";
                    if (template.SymbolHourMap.TryGetValue(testKey, out var testValue))
                    {
                        log($"[Horizontal PDKS] Test: 'X' sembolü bulundu => {testValue} saat");
                    }
                    else
                    {
                        log($"[Horizontal PDKS] Test: 'X' sembolü bulunamadı. Comparer çalışmıyor olabilir.");
                    }
                }
                else
                {
                    log("[Horizontal PDKS] Uyarı: Şablonda sembol eşleştirmesi tanımlı değil!");
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;

                // Gün başlıklarının bulunduğu satırı tespit et:
                // 1..31 arasında artan en az 3 ardışık sayı içeren ilk satır gün satırı kabul edilir.
                // Tüm ardışık günleri topla, sadece 3'te durma.
                int headerRow = -1;
                var dayColumns = new List<(int Day, int Col)>();

                int maxScanRow = Math.Min(lastRow, 20); // üst taraftaki meta bilgi satırlarını taramak için yeterli
                for (int row = 1; row <= maxScanRow && headerRow == -1; row++)
                {
                    var candidateCols = new List<(int Day, int Col)>();
                    int? previousDay = null;

                    for (int col = 1; col <= lastCol; col++)
                    {
                        var text = sheet.Cells[row, col].Text?.Trim();
                        if (int.TryParse(text, out int day) && day is >= 1 and <= 31)
                        {
                            // Ardışık gün kontrolü
                            if (previousDay != null && day == previousDay + 1)
                            {
                                candidateCols.Add((day, col));
                            }
                            else if (previousDay == null || day > previousDay + 1)
                            {
                                // Yeni bir ardışık seri başlıyor
                                candidateCols.Clear();
                                candidateCols.Add((day, col));
                            }
                            // day < previousDay durumunda (geriye gidiyorsa) seriyi sıfırla
                            else if (day < previousDay)
                            {
                                candidateCols.Clear();
                                candidateCols.Add((day, col));
                            }

                            previousDay = day;
                        }
                        else
                        {
                            // Ardışıklık bozuldu, ama eğer yeterli gün topladıysak kabul et
                            if (candidateCols.Count >= 3)
                            {
                                headerRow = row;
                                dayColumns = new List<(int Day, int Col)>(candidateCols);
                                break;
                            }
                            previousDay = null;
                        }
                    }

                    // Satır sonunda da kontrol et
                    if (headerRow == -1 && candidateCols.Count >= 3)
                    {
                        headerRow = row;
                        dayColumns = new List<(int Day, int Col)>(candidateCols);
                    }
                }

                if (headerRow == -1 || dayColumns.Count == 0)
                {
                    throw new Exception("Gün sütunları tespit edilemedi. Başlıklarda 1..31 gün numaraları ardışık olarak bekleniyor.");
                }

                int firstDataRow = headerRow + 1;

                // Başlık satırlarında "SIRA NO", "PERSONELİN ADI SOYADI" gibi tam başlıkları ara
                int codeCol = -1;
                int nameCol = -1;
                int firstDayCol = dayColumns.Min(d => d.Col);

                // Gün satırının kendisi ve üstündeki satırlarda başlık ara (max 10 satır yukarı)
                // Ayrıca gün satırının kendisini de kontrol et
                for (int searchRow = Math.Max(1, headerRow - 10); searchRow <= headerRow; searchRow++)
                {
                    // Tüm sütunları tara, sadece firstDayCol'dan öncekileri değil
                    // Ama firstDayCol'dan sonraki sütunlar gün sütunları olduğu için onları atla
                    for (int col = 1; col < firstDayCol; col++)
                    {
                        var text = sheet.Cells[searchRow, col].Text?.Trim().ToUpperInvariant();
                        if (string.IsNullOrWhiteSpace(text))
                            continue;

                        // Önce tam eşleşmeleri kontrol et (daha spesifik)
                        // SIRA NO, SICIL, SICIL NO gibi
                        if (codeCol == -1)
                        {
                            if (text == "SIRA NO" || text == "SIRA" || text.Contains("SIRA NO") || 
                                text.Contains("SICIL NO") || (text.Contains("SIRA") && !text.Contains("PERSONEL") && !text.Contains("İŞYERİ")))
                            {
                                codeCol = col;
                                log($"[Horizontal PDKS] Sicil sütunu bulundu: Satır {searchRow}, Sütun {col}, Başlık: '{sheet.Cells[searchRow, col].Text?.Trim()}'");
                            }
                        }

                        // PERSONELİN ADI SOYADI, ADI SOYADI, PERSONEL ADI gibi
                        if (nameCol == -1)
                        {
                            if (text.Contains("PERSONELİN ADI SOYADI") || text.Contains("PERSONEL ADI SOYADI") ||
                                text.Contains("ADI SOYADI") || (text.Contains("PERSONEL") && text.Contains("ADI") && text.Contains("SOYADI")))
                            {
                                nameCol = col;
                                log($"[Horizontal PDKS] Ad Soyad sütunu bulundu: Satır {searchRow}, Sütun {col}, Başlık: '{sheet.Cells[searchRow, col].Text?.Trim()}'");
                            }
                        }

                        // Eğer tam eşleşme bulunamadıysa, daha genel arama yap
                        if (codeCol == -1 && (text == "SIRA" || text == "SICIL" || (text.Contains("SICIL") && !text.Contains("İŞYERİ"))))
                        {
                            codeCol = col;
                            log($"[Horizontal PDKS] Sicil sütunu (genel) bulundu: Satır {searchRow}, Sütun {col}, Başlık: '{sheet.Cells[searchRow, col].Text?.Trim()}'");
                        }

                        if (nameCol == -1 && ((text.Contains("PERSONEL") && text.Contains("ADI")) || 
                            (text.Contains("ADI") && text.Contains("SOYADI"))))
                        {
                            nameCol = col;
                            log($"[Horizontal PDKS] Ad Soyad sütunu (genel) bulundu: Satır {searchRow}, Sütun {col}, Başlık: '{sheet.Cells[searchRow, col].Text?.Trim()}'");
                        }

                        if (codeCol != -1 && nameCol != -1)
                            break;
                    }
                    if (codeCol != -1 && nameCol != -1)
                            break;
                }

                // Bulunamazsa varsayılan: gün başlıklarının solunda kalan ilk iki sütun
                if (codeCol == -1)
                {
                    codeCol = Math.Max(1, firstDayCol - 2);
                    log($"[Horizontal PDKS] Sicil sütunu başlık satırlarında bulunamadı, varsayılan sütun {codeCol} kullanılıyor.");
                }
                if (nameCol == -1)
                {
                    nameCol = Math.Max(1, firstDayCol - 1);
                    log($"[Horizontal PDKS] Ad Soyad sütunu başlık satırlarında bulunamadı, varsayılan sütun {nameCol} kullanılıyor.");
                }

                log($"[Horizontal PDKS] Gün satırı: {headerRow}, veri başlangıç satırı: {firstDataRow}, ilk gün sütunu: {firstDayCol}");
                log($"[Horizontal PDKS] {dayColumns.Count} gün sütunu bulundu. Satır aralığı: {firstDataRow}-{lastRow}, Sütun aralığı: 1-{lastCol}");
                log($"[Horizontal PDKS] Sicil sütunu: {codeCol}, Ad Soyad sütunu: {nameCol}");
                
                // Başlık satırlarını debug için logla
                if (codeCol > 0 && codeCol <= lastCol && nameCol > 0 && nameCol <= lastCol)
                {
                    for (int debugRow = Math.Max(1, headerRow - 5); debugRow < headerRow; debugRow++)
                    {
                        var codeHeader = sheet.Cells[debugRow, codeCol].Text?.Trim();
                        var nameHeader = sheet.Cells[debugRow, nameCol].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(codeHeader) || !string.IsNullOrWhiteSpace(nameHeader))
                        {
                            log($"[Horizontal PDKS] Satır {debugRow} - Sicil başlığı: '{codeHeader}', Ad Soyad başlığı: '{nameHeader}'");
                        }
                    }
                }

                int processedRows = 0;
                int skippedEmptyRows = 0;
                int recordsCreated = 0;

                for (int row = firstDataRow; row <= lastRow; row++)
                {
                    var code = sheet.Cells[row, codeCol].Text?.Trim();
                    var name = sheet.Cells[row, nameCol].Text?.Trim();

                    if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name))
                    {
                        skippedEmptyRows++;
                        continue;
                    }

                    processedRows++;
                    if (processedRows <= 3)
                    {
                        log($"[Horizontal PDKS] Satır {row} - Sicil: '{code}', Ad: '{name}'");
                    }

                    int dayRecordsForRow = 0;
                    foreach (var (day, col) in dayColumns)
                    {
                        var cell = sheet.Cells[row, col];
                        var rawValue = cell.Text?.Trim();
                        if (string.IsNullOrWhiteSpace(rawValue))
                        {
                            // Eğer şablon tatilleri içeriyorsa, boş hücreler de tatil olabilir (normal çalışma yok)
                            // Ama boş hücreleri atlayalım, sadece işaretlenmiş tatilleri alalım
                            continue;
                        }

                        if (processedRows <= 2 && day <= 5)
                        {
                            log($"[Horizontal PDKS] Satır {row}, Gün {day} (sütun {col}): '{rawValue}'");
                        }

                        DateTime date;
                        try
                        {
                            date = new DateTime(year, month, day);
                        }
                        catch
                        {
                            continue;
                        }

                        double hours = 0;
                        bool isHoliday = false;
                        bool workedOnOfficialHoliday = false;

                        // Resmi tatil kontrolü
                        var holidayLookup = companyConfig != null ? BuildHolidayLookup(companyConfig) : new Dictionary<DateTime, HolidayInfo>();
                        bool isOfficialHoliday = holidayLookup.TryGetValue(date.Date, out var holidayInfo);

                        // Firma bazlı veya şablon bazlı tatil kontrolü
                        // Firma bazlı ayar önceliklidir
                        bool hasHolidaysInTemplate = companyConfig?.HorizontalTemplateSettings?.HasHolidaysInTemplate ?? template.HasHolidaysInTemplate;

                        // Resmi tatil günlerinde özel kontrol: RT, R, T harfleri çalışma göstergesidir (RT hakedişi verilmeyecek)
                        if (isOfficialHoliday)
                        {
                            // Resmi tatil günlerinde çalışma göstergeleri (RT, R, T - çalışmış demek, RT hakedişi VERİLMEMELİ)
                            // Ayarlardan al, yoksa varsayılan değerleri kullan
                            var workIndicatorsStr = companyConfig?.HorizontalTemplateSettings?.OfficialHolidayWorkIndicators ?? "RT,R,T";
                            var workIndicatorsOnHoliday = new HashSet<string>(
                                workIndicatorsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(s => s.Trim()),
                                StringComparer.OrdinalIgnoreCase
                            );
                            
                            if (workIndicatorsOnHoliday.Contains(rawValue))
                            {
                                // Bu resmi tatil gününde çalışma göstergesi (RT hakedişi verilmeyecek)
                                workedOnOfficialHoliday = true;
                                // Çalışma saati olarak işle - sembol eşleştirmesine bak veya numeric parse et
                                if (double.TryParse(rawValue.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                                {
                                    hours = numeric;
                                }
                                else if (template.SymbolHourMap != null && template.SymbolHourMap.TryGetValue(rawValue, out var mappedValue))
                                {
                                    hours = mappedValue;
                                }
                                else
                                {
                                    // RT, R, T harfleri için varsayılan çalışma saati (örneğin standart saat)
                                    // Veya şablondaki eşleştirmeye bakılabilir
                                    hours = 0; // Eğer eşleştirme yoksa 0 kabul edelim, ProcessHorizontalTemplateRecord'da düzenlenebilir
                                }
                                
                                if (processedRows <= 2)
                                {
                                    log($"[Horizontal PDKS] Resmi tatil gününde çalışma göstergesi: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> Çalışma saati: {hours}h (RT hakedişi VERİLMEYECEK)");
                                }
                            }
                                else
                                {
                                    // Resmi tatil günlerinde tatil göstergeleri (X, 7.5 - tatil hak etmiş demek, RT hakedişi VERİLECEK)
                                    // Ayarlardan al, yoksa varsayılan değerleri kullan
                                    var restIndicatorsStr = companyConfig?.HorizontalTemplateSettings?.OfficialHolidayRestIndicators ?? "X,7.5";
                                    var restIndicatorsOnHoliday = new HashSet<string>(
                                        restIndicatorsStr.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                            .Select(s => s.Trim()),
                                        StringComparer.OrdinalIgnoreCase
                                    );
                                    
                                    // Resmi tatil günlerinde tatil göstergeleri kontrolü
                                    bool isRestIndicator = restIndicatorsOnHoliday.Contains(rawValue);
                                    
                                    // Normal tatil sembolleri kontrolü
                                    var holidaySymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T", "tatil", "TATİL", "Tatil", "İ", "izın", "İZİN", "İzin" };
                                    
                                    if (isRestIndicator || holidaySymbols.Contains(rawValue))
                                    {
                                        // Bu bir tatil günü (RT hakedişi verilecek)
                                        isHoliday = true;
                                        hours = 0;
                                        
                                        if (processedRows <= 2)
                                        {
                                            log($"[Horizontal PDKS] Resmi tatil gününde tatil göstergesi: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> Tatil (RT hakedişi VERİLECEK)");
                                        }
                                    }
                                    else
                                    {
                                        // Numeric veya sembolik değer kontrolü
                                        if (double.TryParse(rawValue.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                                        {
                                            hours = numeric;
                                            
                                            // Eğer saat 0 ise veya çok küçükse, tatil olarak işle (RT hakedişi verilecek)
                                            if (hours == 0)
                                            {
                                                isHoliday = true;
                                                
                                                if (processedRows <= 2)
                                                {
                                                    log($"[Horizontal PDKS] Resmi tatil gününde 0 saat: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> Tatil (RT hakedişi VERİLECEK)");
                                                }
                                            }
                                            else
                                            {
                                                // Resmi tatil gününde çalışma saati var (RT hakedişi verilmeyecek)
                                                workedOnOfficialHoliday = true;
                                                
                                                if (processedRows <= 2)
                                                {
                                                    log($"[Horizontal PDKS] Resmi tatil gününde çalışma saati: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> {hours}h (RT hakedişi VERİLMEYECEK)");
                                                }
                                            }
                                        }
                                        else
                                        {
                                            // Sembolik değer kontrolü (örn. X)
                                            double? mapped = null;
                                            if (template.SymbolHourMap != null)
                                            {
                                                if (template.SymbolHourMap.TryGetValue(rawValue, out var exactMatch))
                                                {
                                                    mapped = exactMatch;
                                                }
                                            }

                                            if (mapped.HasValue)
                                            {
                                                hours = mapped.Value;
                                                
                                                // Eğer saat 0 ise, tatil olarak işle (RT hakedişi verilecek)
                                                if (hours == 0)
                                                {
                                                    isHoliday = true;
                                                    
                                                    if (processedRows <= 2)
                                                    {
                                                        log($"[Horizontal PDKS] Resmi tatil gününde 0 saat sembol: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> Tatil (RT hakedişi VERİLECEK)");
                                                    }
                                                }
                                                else
                                                {
                                                    // Resmi tatil gününde çalışma saati var (RT hakedişi verilmeyecek)
                                                    workedOnOfficialHoliday = true;
                                                    
                                                    if (processedRows <= 2)
                                                    {
                                                        log($"[Horizontal PDKS] Resmi tatil gününde çalışma saati sembol: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> {hours}h (RT hakedişi VERİLMEYECEK)");
                                                    }
                                                }
                                            }
                                            else
                                            {
                                                // Bilinmeyen sembol - resmi tatil gününde tatil olarak kabul et (RT hakedişi verilecek)
                                                if (hasHolidaysInTemplate)
                                                {
                                                    isHoliday = true;
                                                    hours = 0;
                                                    
                                                    if (processedRows <= 2)
                                                    {
                                                        log($"[Horizontal PDKS] Resmi tatil gününde bilinmeyen sembol (tatil): Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> Tatil (RT hakedişi VERİLECEK)");
                                                    }
                                                }
                                                else
                                                {
                                                    // Bilinmeyen sembol - 0 saat kabul et ama logla
                                                    if (processedRows <= 2)
                                                    {
                                                        var definedSymbols = template.SymbolHourMap?.Keys != null ? string.Join(", ", template.SymbolHourMap.Keys) : "yok";
                                                        log($"[Horizontal PDKS] Uyarı: Bilinmeyen sembol '{rawValue}' (satır {row}, sütun {col}) 0 saat olarak kabul edildi. Şablonda tanımlı semboller: {definedSymbols}");
                                                    }
                                                    continue;
                                                }
                                            }
                                        }
                                    }
                                }
                        }
                        else
                        {
                            // Resmi tatil değil, normal işleme
                            // Eğer şablon tatilleri içeriyorsa, tatil sembollerini kontrol et
                            if (hasHolidaysInTemplate)
                            {
                                // Tatil sembolleri (küçük/büyük harfe duyarsız)
                                var holidaySymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T", "tatil", "TATİL", "Tatil", "İ", "izın", "İZİN", "İzin" };
                                
                                if (holidaySymbols.Contains(rawValue))
                                {
                                    // Bu bir tatil günü
                                    isHoliday = true;
                                    hours = 0;
                                }
                            }

                            // Eğer tatil değilse, normal işleme devam et
                            if (!isHoliday)
                            {
                                // Numeric ise direkt saat olarak al
                                if (double.TryParse(rawValue.Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                                {
                                    hours = numeric;
                                    
                                    // Eğer şablon tatilleri içeriyorsa ve saat 0 ise, bu da tatil olabilir
                                    if (hasHolidaysInTemplate && hours == 0)
                                    {
                                        isHoliday = true;
                                    }
                                }
                                else
                                {
                                    // Sembolik (örn. x, X) ise şablondaki eşleştirmeye bak
                                    // SymbolHourMap zaten StringComparer.OrdinalIgnoreCase ile oluşturuldu
                                    double? mapped = null;
                                    if (template.SymbolHourMap != null)
                                    {
                                        // TryGetValue zaten StringComparer.OrdinalIgnoreCase kullanıyor
                                        if (template.SymbolHourMap.TryGetValue(rawValue, out var exactMatch))
                                        {
                                            mapped = exactMatch;
                                        }
                                    }

                                    if (mapped.HasValue)
                                    {
                                        hours = mapped.Value;
                                        
                                        // Eğer şablon tatilleri içeriyorsa ve saat 0 ise, bu da tatil olabilir
                                        if (hasHolidaysInTemplate && hours == 0)
                                        {
                                            isHoliday = true;
                                        }
                                    }
                                    else
                                    {
                                        // Eğer şablon tatilleri içeriyorsa ve sembol eşleştirmesinde yoksa, tatil olabilir
                                        if (hasHolidaysInTemplate)
                                        {
                                            isHoliday = true;
                                            hours = 0;
                                        }
                                        else
                                        {
                                            // Bilinmeyen sembol - 0 saat kabul et ama logla
                                            if (processedRows <= 2)
                                            {
                                                var definedSymbols = template.SymbolHourMap?.Keys != null ? string.Join(", ", template.SymbolHourMap.Keys) : "yok";
                                                log($"[Horizontal PDKS] Uyarı: Bilinmeyen sembol '{rawValue}' (satır {row}, sütun {col}) 0 saat olarak kabul edildi. Şablonda tanımlı semboller: {definedSymbols}");
                                            }
                                            continue;
                                        }
                                    }
                                }
                            }
                        }

                        var pdksRecord = new PDKSDataModel
                        {
                            PersonnelCode = code ?? string.Empty,
                            PersonnelName = name ?? string.Empty,
                            Date = date,
                            WorkedHours = hours,
                            ShiftType = "YatayPuantaj",
                            VacationDays = isHoliday ? 1 : 0,
                            IsOfficialHoliday = isOfficialHoliday,
                            WorkedOnOfficialHoliday = workedOnOfficialHoliday,
                            HolidayName = holidayInfo?.Name ?? string.Empty,
                            IsHalfDayHoliday = holidayInfo?.IsHalfDay ?? false
                        };

                        pdksRecords.Add(pdksRecord);
                        dayRecordsForRow++;
                        recordsCreated++;

                        if (isHoliday && processedRows <= 2)
                        {
                            log($"[Horizontal PDKS] Tatil tespit edildi: Satır {row}, Gün {day} (sütun {col}): '{rawValue}' -> Tatil");
                        }
                    }

                    if (processedRows <= 2)
                    {
                        log($"[Horizontal PDKS] Satır {row} için {dayRecordsForRow} günlük kayıt üretildi.");
                    }
                }

                log($"[Horizontal PDKS] İşlenen satır sayısı: {processedRows}, Boş atlanan satır: {skippedEmptyRows}");

                log($"[Horizontal PDKS] Toplam {pdksRecords.Count} günlük kayıt üretildi.");
            }
            catch (Exception ex)
            {
                throw new Exception($"Yatay puantaj verisi işlenirken hata: {ex.Message}");
            }
        }


        public List<PDKSDataModel> GetAllRecords()
        {
            return pdksRecords;
        }

        public void MatchPersonnelRecords(List<PersonnelRecord> personnelRecords)
        {
            Console.WriteLine($"[Eşleştirme] Başlıyor - PDKS'de {pdksRecords.Count} kayıt, ERP'de {personnelRecords.Count} personel");

            // Debug: İlk 10 PDKS kaydını göster
            Console.WriteLine("[DEBUG] İlk 10 PDKS kaydı:");
            foreach (var record in pdksRecords.Take(10))
            {
                Console.WriteLine($"  PDKS: '{record.PersonnelName}' (Sicil: {record.PersonnelCode})");
            }

            // Debug: İlk 10 ERP personelini göster
            Console.WriteLine("[DEBUG] İlk 10 ERP personeli:");
            foreach (var personnel in personnelRecords.Take(10))
            {
                Console.WriteLine($"  ERP: '{personnel.Name}' (Sicil: {personnel.PersonnelCode})");
            }

            int matchCount = 0;
            int noMatchCount = 0;

            foreach (var pdksRecord in pdksRecords)
            {
                // Birebir isim eşleştirmesi - PDKS'deki B sütunu ile ERP'deki B sütunu karşılaştırılır
                var matched = personnelRecords.FirstOrDefault(p =>
                    p.Name.Trim().ToLower() == pdksRecord.PersonnelName.Trim().ToLower());

                if (matched != null)
                {
                    pdksRecord.IsMatched = true;
                    pdksRecord.MatchedPersonnel = matched;
                    pdksRecord.MatchedPersonnelCode = matched.PersonnelCode;
                    pdksRecord.MatchedPersonnelName = matched.Name;
                    pdksRecord.MatchType = "Birebir İsim Eşleşmesi";
                    matchCount++;

                    Console.WriteLine($"[Eşleşti] PDKS: '{pdksRecord.PersonnelName}' ↔ ERP: '{matched.Name}' (Sicil: {matched.PersonnelCode})");
                }
                else
                {
                    noMatchCount++;
                    Console.WriteLine($"[Eşleşmedi] PDKS: '{pdksRecord.PersonnelName}' - ERP'de bulunamadı");
                }
            }

            Console.WriteLine($"[Eşleştirme] Tamamlandı - Eşleşen: {matchCount}, Eşleşmeyen: {noMatchCount}");
        }

        public void CalculateOvertimeAndAttendance(PDKSConfig config)
        {
            Console.WriteLine($"[FM Hesaplama] Başlıyor - Toplam {pdksRecords.Count(r => r.IsMatched)} eşleşen kayıt");
            // File.AppendAllText("debug_log.txt", $"[FM Hesaplama] Başlıyor - Toplam {pdksRecords.Count(r => r.IsMatched)} eşleşen kayıt\n"); // Kaldırıldı

            // Seçili firmayı bul
            var selectedCompany = config.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == config.SelectedCompanyCode);
            if (selectedCompany == null)
            {
                Console.WriteLine($"[FM Hesaplama] HATA: Seçili firma ({config.SelectedCompanyCode}) bulunamadı!");
                return;
            }

            Console.WriteLine($"[FM Hesaplama] Firma: {selectedCompany.CompanyName} ({selectedCompany.CompanyCode})");

            var holidayLookup = BuildHolidayLookup(selectedCompany);

            // Personel bazında gruplandırma yap
            var personnelGroups = pdksRecords.Where(r => r.IsMatched)
                                           .GroupBy(r => r.PersonnelCode);

            Console.WriteLine($"[FM Hesaplama] {personnelGroups.Count()} farklı personel için hesaplama yapılacak");
            currentCarryOverStates.Clear();

            foreach (var personnelGroup in personnelGroups)
            {
                var personnelCode = personnelGroup.Key;
                // Personelin tüm kayıtlarını al
                var personnelRecords = personnelGroup.ToList();

                Console.WriteLine($"[FM Hesaplama] Personel {personnelCode}: {personnelRecords.Count} kayıt");

                // İlk olarak tüm kayıtlar için çalışma saatlerini ve fazla mesaiyi hesapla
                foreach (var record in personnelRecords)
                {
                    // Yatay şablon kayıtları için özel işlem (vardiya bilgisi olmadan)
                    if (record.ShiftType == "YatayPuantaj" && selectedCompany.HorizontalTemplateSettings?.ApplyRulesWithoutShift == true)
                    {
                        // Yatay şablonlarda vardiya bilgisi yok, sadece WorkedHours var
                        // Üst üste günlere göre kuralları uygula
                        ProcessHorizontalTemplateRecord(record, personnelRecords, selectedCompany, holidayLookup);
                        continue;
                    }

                    // Çalışma saatini molayı çıkararak yeniden hesapla
                    if (record.CheckOutTime > record.CheckInTime)
                    {
                        // Firma bazlı: ShiftRuleConfig'den uygun kuralı bul
                        var shiftConfig = GetShiftConfigForRecord(selectedCompany, record.ShiftType);

                        Console.WriteLine($"[ANA HESAPLAMA] BAŞLATILDI - Personel: {record.PersonnelName}, Vardiya: {record.ShiftType}, Firma: {selectedCompany.CompanyName}");
                        // File.AppendAllText("debug_log.txt", $"[ANA HESAPLAMA] BAŞLATILDI - Personel: {record.PersonnelName}, Vardiya: {record.ShiftType}, Firma: {selectedCompany.CompanyName}\n"); // Kaldırıldı
                        double breakHours = shiftConfig?.BreakHours ?? 1.0; // Varsayılan 1 saat mola
                        double standardHours = shiftConfig?.StandardHours ?? 7.5; // Varsayılan 7.5 saat

                        string groupName = shiftConfig?.GroupName ?? "BULUNAMADI";
                        Console.WriteLine($"[VARDİYA KURAL] {record.PersonnelName} - {record.ShiftType} → {groupName} (Standart: {standardHours}h, Mola: {breakHours}h)");
                        // File.AppendAllText("debug_log.txt", $"[ANA HESAPLAMA] BAŞLATILDI - Personel: {record.PersonnelName}, Vardiya: {record.ShiftType}, Firma: {selectedCompany.CompanyName}\n"); // Kaldırıldı

                        // Net çalışma saati = toplam süre - mola
                        double totalHours = (record.CheckOutTime - record.CheckInTime).TotalHours;
                        record.WorkedHours = Math.Max(0, totalHours - breakHours);

                        Console.WriteLine($"[Çalışma Saati] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {record.CheckInTime:hh\\:mm}-{record.CheckOutTime:hh\\:mm} = {totalHours:F1}h toplam, {breakHours:F1}h mola, {record.WorkedHours:F1}h net çalışma");

                        holidayLookup.TryGetValue(record.Date.Date, out var holidayInfo);
                        record.IsOfficialHoliday = holidayInfo != null;
                        record.HolidayName = holidayInfo?.Name ?? string.Empty;
                        record.IsHalfDayHoliday = holidayInfo?.IsHalfDay ?? false;
                        if (record.IsOfficialHoliday && record.WorkedHours > 0)
                        {
                            record.WorkedOnOfficialHoliday = true;
                            double specialHours = record.WorkedHours;
                            if (holidayInfo?.IsHalfDay == true)
                            {
                                double halfStandardHours = Math.Max(0, standardHours / 2.0);
                                if (halfStandardHours > 0)
                                {
                                    specialHours = Math.Min(record.WorkedHours, halfStandardHours);
                                }
                                Console.WriteLine($"[Resmi Tatil] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Yarım gün tatil - FM saatleri {specialHours:F2} olarak ayarlandı (Standart {standardHours:F2}h)");
                                File.AppendAllText("debug_log.txt", $"[Resmi Tatil] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Yarım gün tatil - FM saatleri {specialHours:F2} olarak ayarlandı (Standart {standardHours:F2}h)\n");
                            }
                            ApplySpecialOvertime(record,
                                                 selectedCompany.SpecialOvertimeSettings?.HolidayWorkColumnName,
                                                 selectedCompany.SpecialOvertimeSettings,
                                                 specialHours,
                                                 "Resmi Tatil Çalışması");
                        }
                        else
                        {
                            record.WorkedOnOfficialHoliday = false;
                        }

                        // Eksik saat hesaplaması - sadece çalışılan günlerde eksik saat varsa hesapla
                        // Çalışılmayan günlerde (WorkedHours = 0) eksik saat hesaplanmaz
                        if (record.WorkedHours > 0 && record.WorkedHours < standardHours)
                        {
                            record.AbsentHours = standardHours - record.WorkedHours;
                            Console.WriteLine($"[Eksik Saat] {record.PersonnelName}: {record.ShiftType} vardiya - {record.WorkedHours:F1}h çalışma, {standardHours:F1}h standart, {record.AbsentHours:F1}h eksik saat");
                        }

                        // Fazla mesai hesaplaması - firma bazlı kural sistemi ile
                        if (shiftConfig != null && record.WorkedHours > standardHours)
                        {
                            var overtimeResult = CalculateOvertimeWithRules(record.WorkedHours, record.CheckOutTime, shiftConfig);
                            record.FMNormalHours = overtimeResult.NormalHours;
                            record.FM50PercentHours = overtimeResult.SpecialHours;
                            record.FMColumnHours = overtimeResult.ColumnHours;

                            Console.WriteLine($"[FM Hesaplama] {record.PersonnelName}: {record.ShiftType} vardiya ({shiftConfig.GroupName}) - {record.WorkedHours:F1}h çalışma, {standardHours:F1}h standart, {record.FMNormalHours:F1}h FM Normal, {record.FM50PercentHours:F1}h FM %50");
                            File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [FM Hesaplama] {record.PersonnelName}: {record.Date:dd.MM} ({record.ShiftType}) -> FM Normal: {record.FMNormalHours:F2}h, FM %50: {record.FM50PercentHours:F2}h\n");
                        }
                        else
                        {
                            Console.WriteLine($"[FM Hesaplama] {record.PersonnelName}: {record.ShiftType} - Fazla çalışma yok ({record.WorkedHours:F1}h < {standardHours:F1}h)");
                        }
                    }
                }

                // Ardışık çalışma günlerini hesapla ve tatil haklarını dağıt
                // Eğer "tatiller şablonda" açıksa, tatil hakedişi hesaplaması yapılmaz
                bool hasHolidaysInTemplate = selectedCompany.HorizontalTemplateSettings?.HasHolidaysInTemplate ?? false;
                bool hasHorizontalTemplateRecords = personnelRecords.Any(r => r.ShiftType == "YatayPuantaj");
                
                if (!hasHolidaysInTemplate || !hasHorizontalTemplateRecords)
                {
                    // Normal tatil hakedişi hesaplaması yap
                    CalculateConsecutiveWorkDays(personnelRecords, selectedCompany);
                }
                else
                {
                    Console.WriteLine($"[Tatil Hesaplama] {personnelRecords.First().PersonnelName}: Tatiller şablonda işaretli olduğu için tatil hakedişi hesaplaması atlandı");
                }
            }

            Console.WriteLine($"[FM Hesaplama] Tamamlandı");

            PersistCarryOverStates(selectedCompany);
        }

        private void PersistCarryOverStates(CompanyConfig companyConfig)
        {
            if (companyConfig == null)
            {
                return;
            }

            int year = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            int month = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;

            carryOverStateService.SaveCurrentMonthStates(
                companyConfig.CompanyCode,
                year,
                month,
                currentCarryOverStates.Values);
        }

        private (int year, int month) GetPreviousPeriod(int year, int month)
        {
            if (month <= 1)
            {
                return (year - 1, 12);
            }

            return (year, month - 1);
        }

        /// <summary>
        /// Yatay şablon kayıtları için vardiya bilgisi olmadan çalışma mantığı
        /// Üst üste günlere göre vardiya gözetmeksizin hakediş kurallarını uygular
        /// </summary>
        private void ProcessHorizontalTemplateRecord(PDKSDataModel record, List<PDKSDataModel> allPersonnelRecords, CompanyConfig companyConfig, Dictionary<DateTime, HolidayInfo> holidayLookup)
        {
            // Eğer kayıt zaten şablondan gelen bir tatil ise (VacationDays > 0),
            // bu kayıt için işlem yapma (tatiller zaten şablonda işaretli)
            if (record.VacationDays > 0)
            {
                Console.WriteLine($"[Yatay Şablon - Tatil] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Şablondan gelen tatil, işlem atlandı");
                return;
            }

            // Yatay şablonlarda vardiya bilgisi yok, sadece WorkedHours var
            // Seçili vardiya grubunu kullan veya ilk vardiya kuralını varsayılan olarak kullan
            ShiftRuleConfig? shiftConfig = null;
            var horizontalSettings = companyConfig.HorizontalTemplateSettings;
            
            if (horizontalSettings != null && !string.IsNullOrWhiteSpace(horizontalSettings.SelectedShiftRuleGroupName))
            {
                // Seçili vardiya grubunu bul
                shiftConfig = companyConfig.ShiftRuleConfigs?.FirstOrDefault(c => 
                    c.GroupName.Equals(horizontalSettings.SelectedShiftRuleGroupName, StringComparison.OrdinalIgnoreCase));
                
                if (shiftConfig == null)
                {
                    Console.WriteLine($"[Yatay Şablon] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Seçili vardiya grubu '{horizontalSettings.SelectedShiftRuleGroupName}' bulunamadı, ilk grup kullanılıyor");
                }
            }
            
            // Seçili grup bulunamadıysa veya seçim yapılmamışsa ilk grubu kullan
            if (shiftConfig == null)
            {
                shiftConfig = companyConfig.ShiftRuleConfigs?.FirstOrDefault();
            }
            
            if (shiftConfig == null)
            {
                Console.WriteLine($"[Yatay Şablon] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Vardiya kuralı bulunamadı, varsayılan değerler kullanılıyor");
                return;
            }
            
            // Üst üste gün sayısı override kontrolü
            int consecutiveDaysForVacation = shiftConfig.ConsecutiveDaysForVacation;
            if (horizontalSettings != null && horizontalSettings.OverrideConsecutiveDaysForVacation > 0)
            {
                consecutiveDaysForVacation = horizontalSettings.OverrideConsecutiveDaysForVacation;
                Console.WriteLine($"[Yatay Şablon] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Üst üste gün sayısı override edildi: {consecutiveDaysForVacation} (Vardiya kuralı: {shiftConfig.ConsecutiveDaysForVacation})");
            }

            double standardHours = shiftConfig.StandardHours;
            double breakHours = shiftConfig.BreakHours;

            // WorkedHours zaten yatay şablondan geliyor, mola çıkarılmış olabilir veya olmayabilir
            // Eğer mola çıkarılmamışsa, çıkar
            if (record.WorkedHours > standardHours + breakHours)
            {
                record.WorkedHours = Math.Max(0, record.WorkedHours - breakHours);
            }

            Console.WriteLine($"[Yatay Şablon] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {record.WorkedHours:F1}h çalışma (Standart: {standardHours:F1}h)");

            // Resmi tatil ve hafta sonu kontrolü
            // Not: IsOfficialHoliday ve WorkedOnOfficialHoliday zaten LoadHorizontalDailyHours'da ayarlanmış olabilir
            // Sadece henüz ayarlanmamışsa ayarla
            if (!record.IsOfficialHoliday)
            {
                holidayLookup.TryGetValue(record.Date.Date, out var holidayInfo);
                record.IsOfficialHoliday = holidayInfo != null;
                record.HolidayName = holidayInfo?.Name ?? string.Empty;
                record.IsHalfDayHoliday = holidayInfo?.IsHalfDay ?? false;
            }
            
            // Hafta sonu kontrolü (Cumartesi = 6, Pazar = 0)
            bool isWeekend = record.Date.DayOfWeek == DayOfWeek.Saturday || record.Date.DayOfWeek == DayOfWeek.Sunday;

            // Resmi tatil ise: 
            // - Eğer WorkedOnOfficialHoliday = true ise -> Çalışmış demek, RT hakedişi VERİLMEMELİ, resmi tatil kolonuna gitmeli
            // - Eğer WorkedOnOfficialHoliday = false ve VacationDays > 0 ise -> Tatil hak etmiş demek, RT hakedişi VERİLMELİ
            if (record.IsOfficialHoliday && record.WorkedOnOfficialHoliday && record.WorkedHours > 0)
            {
                // Resmi tatil gününde çalışmış (RT hakedişi VERİLMEYECEK)
                double specialHours = record.WorkedHours;
                if (record.IsHalfDayHoliday)
                {
                    double halfStandardHours = Math.Max(0, standardHours / 2.0);
                    if (halfStandardHours > 0)
                    {
                        specialHours = Math.Min(record.WorkedHours, halfStandardHours);
                    }
                }
                ApplySpecialOvertime(record,
                                     companyConfig.SpecialOvertimeSettings?.HolidayWorkColumnName,
                                     companyConfig.SpecialOvertimeSettings,
                                     specialHours,
                                     "Resmi Tatil Çalışması");
                
                Console.WriteLine($"[Yatay Şablon - Resmi Tatil] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {specialHours:F1}h resmi tatil çalışması (RT hakedişi VERİLMEYECEK - çalışmış)");
                // Resmi tatilde normal fazla mesai hesaplanmaz, sadece resmi tatil kolonuna gider
                return;
            }
            else if (record.IsOfficialHoliday && !record.WorkedOnOfficialHoliday && record.VacationDays > 0)
            {
                // Resmi tatil gününde tatil hak etmiş (RT hakedişi VERİLECEK)
                Console.WriteLine($"[Yatay Şablon - Resmi Tatil] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Tatil hak etmiş (RT hakedişi VERİLECEK - çalışmamış)");
                // Bu kayıt CalculateConsecutiveWorkDays'de RT hakedişi için kullanılacak
                // Burada işlem yapmaya gerek yok, sadece log
            }
            else if (record.IsOfficialHoliday && record.WorkedHours > 0 && !record.WorkedOnOfficialHoliday)
            {
                // Eğer LoadHorizontalDailyHours'da ayarlanmamışsa, burada ayarla
                // Ama bu durum normalde olmamalı çünkü LoadHorizontalDailyHours'da ayarlanmış olmalı
                record.WorkedOnOfficialHoliday = true;
                double specialHours = record.WorkedHours;
                if (record.IsHalfDayHoliday)
                {
                    double halfStandardHours = Math.Max(0, standardHours / 2.0);
                    if (halfStandardHours > 0)
                    {
                        specialHours = Math.Min(record.WorkedHours, halfStandardHours);
                    }
                }
                ApplySpecialOvertime(record,
                                     companyConfig.SpecialOvertimeSettings?.HolidayWorkColumnName,
                                     companyConfig.SpecialOvertimeSettings,
                                     specialHours,
                                     "Resmi Tatil Çalışması");
                
                Console.WriteLine($"[Yatay Şablon - Resmi Tatil] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {specialHours:F1}h resmi tatil çalışması (LoadHorizontalDailyHours'da ayarlanmamış, burada ayarlandı)");
                return;
            }

            // Eksik saat hesaplaması
            if (record.WorkedHours > 0 && record.WorkedHours < standardHours)
            {
                record.AbsentHours = standardHours - record.WorkedHours;
                Console.WriteLine($"[Yatay Şablon - Eksik Saat] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {record.WorkedHours:F1}h < {standardHours:F1}h standart, {record.AbsentHours:F1}h eksik");
            }

            // Fazla mesai hesaplaması - vardiya gözetmeksizin
            // Üst üste günlere göre kuralları uygula
            if (record.WorkedHours > standardHours)
            {
                double overtimeHours = record.WorkedHours - standardHours;
                
                // Üst üste günleri tespit et
                var sortedRecords = allPersonnelRecords.Where(r => r.ShiftType == "YatayPuantaj")
                                                       .OrderBy(r => r.Date)
                                                       .ToList();
                
                int consecutiveDays = GetConsecutiveWorkDays(record, sortedRecords);
                
                Console.WriteLine($"[Yatay Şablon] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Üst üste {consecutiveDays} gün çalışma, Hafta sonu: {isWeekend}");

                // Hafta sonu ise ve resmi tatil değilse: Hafta sonu kurallarına göre işle
                if (isWeekend)
                {
                    // Hafta sonu fazla mesai - özel kolona git
                    ApplySpecialOvertime(record,
                                         companyConfig.SpecialOvertimeSettings?.WeekendWorkColumnName,
                                         companyConfig.SpecialOvertimeSettings,
                                         overtimeHours,
                                         "Hafta Sonu Çalışması");
                    Console.WriteLine($"[Yatay Şablon - Hafta Sonu FM] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {overtimeHours:F1}h hafta sonu fazla mesai");
                }
                else
                {
                    // Normal gün: Vardiya gözetmeksizin fazla mesai hesapla
                    // CheckOutTime yok, bu yüzden TimeSpan.Zero kullan
                    var overtimeResult = CalculateOvertimeWithRules(record.WorkedHours, TimeSpan.Zero, shiftConfig);
                    record.FMNormalHours = overtimeResult.NormalHours;
                    record.FM50PercentHours = overtimeResult.SpecialHours;
                    record.FMColumnHours = overtimeResult.ColumnHours;

                    Console.WriteLine($"[Yatay Şablon - FM] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {record.WorkedHours:F1}h çalışma, {standardHours:F1}h standart, {record.FMNormalHours:F1}h FM Normal, {record.FM50PercentHours:F1}h FM %50");
                }
            }
        }

        /// <summary>
        /// Belirli bir kayıt için üst üste çalışılan gün sayısını hesapla
        /// </summary>
        private int GetConsecutiveWorkDays(PDKSDataModel record, List<PDKSDataModel> sortedRecords)
        {
            int consecutiveDays = 1;
            var recordIndex = sortedRecords.FindIndex(r => r.Date == record.Date && r.PersonnelCode == record.PersonnelCode);
            
            if (recordIndex < 0)
                return 1;

            // Geriye doğru say
            for (int i = recordIndex - 1; i >= 0; i--)
            {
                var prevRecord = sortedRecords[i];
                if (prevRecord.PersonnelCode != record.PersonnelCode)
                    break;
                
                if (prevRecord.WorkedHours > 0 && (record.Date - prevRecord.Date).TotalDays == 1)
                {
                    consecutiveDays++;
                }
                else
                {
                    break;
                }
            }

            // İleriye doğru say
            for (int i = recordIndex + 1; i < sortedRecords.Count; i++)
            {
                var nextRecord = sortedRecords[i];
                if (nextRecord.PersonnelCode != record.PersonnelCode)
                    break;
                
                if (nextRecord.WorkedHours > 0 && (nextRecord.Date - record.Date).TotalDays == 1)
                {
                    consecutiveDays++;
                }
                else
                {
                    break;
                }
            }

            return consecutiveDays;
        }

        private void CalculateConsecutiveWorkDays(List<PDKSDataModel> personnelRecords, CompanyConfig companyConfig)
        {
            if (personnelRecords == null || personnelRecords.Count == 0)
            {
                return;
            }

            Console.WriteLine($"[TATİL HAK EDİŞ BAŞLADI] {personnelRecords.Count} kayıt için hesaplanıyor");
            File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [TATİL HAK EDİŞ] {personnelRecords.First().PersonnelName}: Başlatıldı ({personnelRecords.Count} kayıt)\n");

            // Mevcut ayın kayıtlarını al
            int currentYear = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            int currentMonth = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;
            var currentMonthRecords = personnelRecords.Where(r => r.Date.Year == currentYear && r.Date.Month == currentMonth).OrderBy(r => r.Date).ToList();
            
            // Bir önceki ayın kayıtlarını al (devir için)
            var prevPeriod = GetPreviousPeriod(currentYear, currentMonth);
            var previousMonthRecords = personnelRecords.Where(r => r.Date.Year == prevPeriod.year && r.Date.Month == prevPeriod.month).OrderBy(r => r.Date).ToList();
            
            var sortedRecords = currentMonthRecords;
            var restDayQueue = new Queue<EarnedRestDayDetail>();
            var personnelCode = sortedRecords.FirstOrDefault()?.PersonnelCode ?? personnelRecords.First().PersonnelCode;
            PersonnelCarryOverState? carryOverState = null;

            // Bir önceki ayın son günlerine bakarak devir eden günleri hesapla
            int consecutiveWorkDays = 0;
            DateTime? lastWorkDate = null;
            DateTime? lastActualWorkDate = null;
            string lastWorkedShiftType = string.Empty;
            double lastActualWorkedHours = 0;
            
            if (includePreviousMonthCarryOver && !string.IsNullOrWhiteSpace(personnelCode) && previousMonthRecords.Any())
            {
                // Bir önceki ayın son günlerinden başlayarak ardışık çalışma günlerini say
                // Mevcut ayın ilk gününe kadar devam eden seriyi bul
                var prevMonthSorted = previousMonthRecords.OrderByDescending(r => r.Date).ToList();
                var carryOverDays = new List<PDKSDataModel>();
                
                // Bir önceki ayın son gününden başlayarak geriye doğru ardışık çalışma günlerini topla
                for (int i = 0; i < prevMonthSorted.Count; i++)
                {
                    var record = prevMonthSorted[i];
                    bool isWorkDay = record.WorkedHours > 0 && 
                        !(record.ShiftType == "YatayPuantaj" && record.VacationDays > 0 && !record.IsOfficialHoliday);
                    
                    if (isWorkDay)
                    {
                        carryOverDays.Insert(0, record); // Başa ekle (tarih sırasına göre)
                        
                        // Eğer bir önceki gün yoksa veya çalışma günü değilse dur
                        if (i + 1 < prevMonthSorted.Count)
                        {
                            var prevRecord = prevMonthSorted[i + 1];
                            bool prevIsWorkDay = prevRecord.WorkedHours > 0 && 
                                !(prevRecord.ShiftType == "YatayPuantaj" && prevRecord.VacationDays > 0 && !prevRecord.IsOfficialHoliday);
                            
                            if (prevIsWorkDay && (record.Date - prevRecord.Date).TotalDays == 1)
                            {
                                continue; // Devam et
                            }
                            else
                            {
                                break; // Seri bitti
                            }
                        }
                        else
                        {
                            break; // Bir önceki ayın sonuna ulaşıldı
                        }
                    }
                    else
                    {
                        break; // Çalışma günü değil, seri bitti
                    }
                }
                
                if (carryOverDays.Any())
                {
                    consecutiveWorkDays = carryOverDays.Count;
                    lastWorkDate = carryOverDays.Last().Date;
                    lastActualWorkDate = carryOverDays.Last().Date;
                    lastWorkedShiftType = carryOverDays.Last().ShiftType ?? string.Empty;
                    lastActualWorkedHours = carryOverDays.Last().WorkedHours;
                    
                    Console.WriteLine($"[Tatil Devir] {personnelRecords.First().PersonnelName}: Bir önceki ayın son {consecutiveWorkDays} günü ({carryOverDays.First().Date:dd.MM.yyyy} - {carryOverDays.Last().Date:dd.MM.yyyy}) devralındı");
                    File.AppendAllText("debug_log.txt", $"[Tatil Devir] {personnelRecords.First().PersonnelName}: Bir önceki ayın son {consecutiveWorkDays} günü devralındı ({carryOverDays.First().Date:dd.MM.yyyy} - {carryOverDays.Last().Date:dd.MM.yyyy})\n");
                }
            }
            
            // Eğer carry-over state varsa ama yukarıdaki mantık çalışmadıysa, eski mantığı kullan
            if (consecutiveWorkDays == 0 && includePreviousMonthCarryOver && !string.IsNullOrWhiteSpace(personnelCode))
            {
                previousCarryOverStates.TryGetValue(personnelCode, out carryOverState);
                if (carryOverState != null)
                {
                    consecutiveWorkDays = carryOverState.ConsecutiveWorkDays;
                    lastWorkDate = carryOverState.LastWorkDate;
                    lastActualWorkDate = carryOverState.LastWorkDate;
                    lastWorkedShiftType = carryOverState.LastShiftType ?? string.Empty;
                    lastActualWorkedHours = carryOverState.LastWorkedHours;
                    
                    Console.WriteLine($"[Tatil Devir] {sortedRecords.First().PersonnelName}: {carryOverState.ConsecutiveWorkDays} günlük seri {carryOverState.LastWorkDate:dd.MM.yyyy} tarihinden devralındı (eski mantık)");
                    File.AppendAllText("debug_log.txt", $"[Tatil Devir] {sortedRecords.First().PersonnelName}: {carryOverState.ConsecutiveWorkDays} günlük seri devralındı ({carryOverState.LastWorkDate:dd.MM.yyyy}) - eski mantık\n");
                }
            }

            foreach (var record in sortedRecords)
            {
                // Resmi tatil günlerinde RT hakedişi mantığı:
                // - WorkedOnOfficialHoliday = true ise -> Çalışmış demek, RT hakedişi VERİLMEMELİ
                // - WorkedOnOfficialHoliday = false ve VacationDays > 0 ise -> Tatil hak etmiş demek, RT hakedişi VERİLMELİ
                // Resmi tatil günlerinde RT hakedişi, ardışık çalışma günlerine bağlı değil, doğrudan verilmeli
                if (record.IsOfficialHoliday && !record.WorkedOnOfficialHoliday && record.VacationDays > 0)
                {
                    // Resmi tatil gününde tatil hak etmiş (RT hakedişi VERİLECEK)
                    // Bu kayıt zaten VacationDays = 1 olarak işaretlenmiş (LoadHorizontalDailyHours'da)
                    // RT hakedişi için özel bir işlem yapmaya gerek yok, kayıt zaten tatil olarak işaretlenmiş
                    Console.WriteLine($"[RT Hakedişi] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Resmi tatil gününde tatil hak etmiş (RT hakedişi VERİLECEK)");
                    File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [RT HAKEDİŞ] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Resmi tatil gününde tatil hak etmiş (RT hakedişi VERİLECEK)\n");
                    // Bu kayıt çalışma günü sayılmaz ve ardışık gün sayacını bozmaz
                    continue;
                }
                
                // Eğer kayıt zaten şablondan gelen bir tatil ise (VacationDays > 0 ve YatayPuantaj),
                // bu kayıt çalışma günü sayılmaz ve tatil hesaplaması yapılmaz
                // Ama resmi tatil günlerinde çalışmış (WorkedOnOfficialHoliday = true) kayıtlar hariç
                bool isHolidayFromTemplate = record.ShiftType == "YatayPuantaj" && record.VacationDays > 0 && !record.IsOfficialHoliday;
                // Resmi tatil günlerinde çalışmış (WorkedOnOfficialHoliday = true) kayıtlar çalışma günü sayılmalı
                bool isWorkDay = record.WorkedHours > 0 && (!isHolidayFromTemplate || record.WorkedOnOfficialHoliday);

                if (isWorkDay)
                {
                    if (lastWorkDate.HasValue && (record.Date - lastWorkDate.Value).TotalDays == 1)
                    {
                        consecutiveWorkDays++;
                    }
                    else
                    {
                        consecutiveWorkDays = 1;
                    }

                    lastWorkDate = record.Date;
                    lastActualWorkDate = record.Date;
                    lastWorkedShiftType = record.ShiftType ?? string.Empty;
                    lastActualWorkedHours = record.WorkedHours;

                    // Eğer şablondan gelen tatil varsa, tatil hesaplaması yapma
                    // (tatiller zaten şablonda işaretli)
                    if (isHolidayFromTemplate)
                    {
                        Console.WriteLine($"[Tatil Kontrolü] {record.PersonnelName} {record.Date:dd.MM.yyyy}: Şablondan gelen tatil, tatil hesaplaması atlandı");
                        continue;
                    }

                    var shiftConfig = GetShiftConfigForRecord(companyConfig, record.ShiftType);
                    if (shiftConfig != null &&
                        shiftConfig.ConsecutiveDaysForVacation > 0 &&
                        consecutiveWorkDays >= shiftConfig.ConsecutiveDaysForVacation &&
                        consecutiveWorkDays % shiftConfig.ConsecutiveDaysForVacation == 0)
                    {
                        // Bir önceki aydan devir eden günlerle tatil hakkı kazanıldı mı kontrol et
                        int carryOverDays = carryOverState?.ConsecutiveWorkDays ?? 0;
                        
                        // Eğer devir eden günler varsa ve bu günlerle birlikte tatil hakkı kazanıldıysa log ekle
                        // consecutiveWorkDays zaten carryOverDays'den başlıyor, yani eğer carryOverDays > 0 ise
                        // ve tatil hakkı kazanıldıysa, bu devir eden günlerle birlikte kazanıldığı anlamına gelir
                        if (carryOverDays > 0)
                        {
                            var dayName = record.Date.ToString("dddd", new System.Globalization.CultureInfo("tr-TR"));
                            int currentMonthDays = consecutiveWorkDays - carryOverDays;
                            var logMessage = $"{record.PersonnelName}: {dayName} bir önceki ayda kalmıştı. Bir önceki aydan devir eden {carryOverDays} çalışma günüyle birlikte toplam {consecutiveWorkDays} gün üst üste çalışarak {shiftConfig.VacationDays} gün tatil hakkı kazandı.";
                            AddCarryOverVacationLog(logMessage);
                            Console.WriteLine($"[Tatil Devir Log] {logMessage}");
                            File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [TATİL DEVİR LOG] {logMessage}\n");
                        }
                        
                        for (int i = 1; i <= shiftConfig.VacationDays; i++)
                        {
                            var restDate = record.Date.AddDays(i);
                            var rangeStart = record.Date.AddDays(-(shiftConfig.ConsecutiveDaysForVacation - 1));
                            var detail = new EarnedRestDayDetail
                            {
                                RestDate = restDate,
                                RangeStart = rangeStart,
                                RangeEnd = record.Date,
                                RuleName = string.IsNullOrWhiteSpace(shiftConfig.GroupName) ? "Varsayılan Kural" : shiftConfig.GroupName,
                                RequiredConsecutiveDays = shiftConfig.ConsecutiveDaysForVacation
                            };

                            restDayQueue.Enqueue(detail);
                        }

                        Console.WriteLine($"[Ardışık Çalışma] {record.PersonnelName}: {consecutiveWorkDays} gün üst üste çalışma - {shiftConfig.VacationDays} gün tatil hakkı kazanıldı");
                        File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [TATİL HAK EDİŞ] {record.PersonnelName} ({record.Date:dd.MM.yyyy}): {consecutiveWorkDays} gün üst üste çalışma, {shiftConfig.VacationDays} gün tatil kazandı.\n");
                    }
                }
                else
                {
                    // Eğer şablondan gelen tatil ise, ardışık gün sayacını sıfırlama
                    // (tatiller çalışma günü sayılmaz ama ardışık gün sayacını bozmaz)
                    if (!isHolidayFromTemplate)
                    {
                        consecutiveWorkDays = 0;
                    }
                    lastWorkDate = record.Date;
                }
            }

            DistributeVacationDays(personnelRecords, restDayQueue, companyConfig);
            TrackCarryOverForPersonnel(sortedRecords, consecutiveWorkDays, lastActualWorkDate, lastWorkedShiftType, lastActualWorkedHours);
        }

        private void TrackCarryOverForPersonnel(List<PDKSDataModel> sortedRecords, int currentStreak, DateTime? lastActualWorkDate, string lastWorkedShiftType, double lastWorkedHours)
        {
            if (sortedRecords == null || sortedRecords.Count == 0)
            {
                return;
            }

            var finalRecordDate = sortedRecords.Last().Date.Date;
            var personnelCode = sortedRecords.First().PersonnelCode;

            if (!lastActualWorkDate.HasValue ||
                lastActualWorkDate.Value.Date != finalRecordDate ||
                currentStreak <= 0)
            {
                currentCarryOverStates.Remove(personnelCode);
                return;
            }

            currentCarryOverStates[personnelCode] = new PersonnelCarryOverState
            {
                PersonnelCode = personnelCode,
                PersonnelName = sortedRecords.First().PersonnelName,
                LastShiftType = lastWorkedShiftType ?? string.Empty,
                LastWorkDate = lastActualWorkDate.Value,
                ConsecutiveWorkDays = currentStreak,
                LastWorkedHours = lastWorkedHours
            };

            Console.WriteLine($"[Tatil Devir] {sortedRecords.First().PersonnelName}: {currentStreak} günlük seri {lastActualWorkDate.Value:dd.MM.yyyy} tarihinde devredildi");
            File.AppendAllText("debug_log.txt", $"[Tatil Devir] {sortedRecords.First().PersonnelName}: {currentStreak} günlük seri devre hazır ({lastActualWorkDate.Value:dd.MM.yyyy})\n");
        }

        private void DistributeVacationDays(List<PDKSDataModel> personnelRecords, Queue<EarnedRestDayDetail> restDayDetails, CompanyConfig companyConfig)
        {
            if (restDayDetails == null || restDayDetails.Count == 0 || personnelRecords == null || personnelRecords.Count == 0)
            {
                return;
            }

            var sampleRecord = personnelRecords.First();
            var recordsByDate = personnelRecords
                .GroupBy(r => r.Date.Date)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.WorkedHours).ToList());
            var workingDayCandidates = personnelRecords
                .Where(r => r.WorkedHours > 0)
                .OrderByDescending(r => r.Date)
                .ToList();
            int workingCandidateIndex = 0;
            var handledRecords = new HashSet<PDKSDataModel>();

            while (restDayDetails.Count > 0)
            {
                var detail = restDayDetails.Dequeue();
                var restDate = detail.RestDate.Date;

                if (recordsByDate.TryGetValue(restDate, out var sameDayRecords) && sameDayRecords.Count > 0)
                {
                    var targetRecord = sameDayRecords[0];

                    if (handledRecords.Contains(targetRecord))
                    {
                        continue;
                    }

                    if (targetRecord.WorkedHours <= 0)
                    {
                        targetRecord.VacationDays = 1;
                        AssignEarnedRestMetadata(targetRecord, detail);
                        Console.WriteLine($"[Tatil Dağılımı] {targetRecord.PersonnelName} {restDate:dd.MM.yyyy}: Tatil günü olarak işaretlendi");
                        File.AppendAllText("debug_log.txt", $"[Tatil Dağılımı] {targetRecord.PersonnelName} {restDate:dd.MM.yyyy}: Tatil günü olarak işaretlendi\n");
                        handledRecords.Add(targetRecord);
                    }
                    else
                    {
                        ApplyWorkedVacationDayOvertime(targetRecord, companyConfig, detail);
                        Console.WriteLine($"[Tatil Dağılımı] {targetRecord.PersonnelName} {restDate:dd.MM.yyyy}: Hak edilen tatil gününde çalıştı, {targetRecord.WorkedHours:F2} saat özel FM'ye aktarıldı");
                        File.AppendAllText("debug_log.txt", $"[Tatil Dağılımı] {targetRecord.PersonnelName} {restDate:dd.MM.yyyy}: Hak edilen tatil gününde çalıştı, {targetRecord.WorkedHours:F2} saat özel FM'ye aktarıldı\n");
                        handledRecords.Add(targetRecord);
                    }
                }
                else
                {
                    PDKSDataModel fallbackRecord = null;
                    while (workingCandidateIndex < workingDayCandidates.Count && fallbackRecord == null)
                    {
                        var candidate = workingDayCandidates[workingCandidateIndex++];
                        if (!handledRecords.Contains(candidate))
                        {
                            fallbackRecord = candidate;
                        }
                    }

                    if (fallbackRecord != null)
                    {
                        ApplyWorkedVacationDayOvertime(fallbackRecord, companyConfig, detail);
                        handledRecords.Add(fallbackRecord);
                        Console.WriteLine($"[Tatil Dağılımı] {fallbackRecord.PersonnelName} {fallbackRecord.Date:dd.MM.yyyy}: Hak edilen tatil gününde çalıştığı varsayıldı, {fallbackRecord.WorkedHours:F2} saat özel FM'ye aktarıldı (fallback)");
                        File.AppendAllText("debug_log.txt", $"[Tatil Dağılımı] {fallbackRecord.PersonnelName} {fallbackRecord.Date:dd.MM.yyyy}: Hak edilen tatil gününde çalıştığı varsayıldı, {fallbackRecord.WorkedHours:F2} saat özel FM'ye aktarıldı (fallback)\n");
                    }
                    else
                    {
                        Console.WriteLine($"[Tatil Dağılımı] Uyarı: {sampleRecord.PersonnelName} için hak edilen tatil günü paylaştırılamadı (yeterli kayıt yok)");
                        File.AppendAllText("debug_log.txt", $"[Tatil Dağılımı] Uyarı: {sampleRecord.PersonnelName} için hak edilen tatil günü paylaştırılamadı (yeterli kayıt yok)\n");
                    }
                }
            }
        }

        private void AssignEarnedRestMetadata(PDKSDataModel record, EarnedRestDayDetail detail)
        {
            if (record == null || detail == null)
            {
                return;
            }

            record.EarnedRestSourceRange = $"{detail.RangeStart:dd.MM.yyyy} - {detail.RangeEnd:dd.MM.yyyy}";
            record.EarnedRestRuleName = detail.RuleName ?? string.Empty;
            record.EarnedRestRequiredDays = detail.RequiredConsecutiveDays;
            record.SpecialOvertimeEffectiveDate = detail.RestDate;
        }

        private (int StartDay, int EndDay) GetEffectiveEmploymentRange(List<PDKSDataModel> records, int monthDays)
        {
            int startDay = 1;
            int endDay = monthDays;

            if (records == null || records.Count == 0)
            {
                return (startDay, endDay);
            }

            int? entryDay = records.Select(r => r.MatchedPersonnel?.EntryDay)
                                   .FirstOrDefault(d => d.HasValue && d.Value > 0);
            int? exitDay = records.Select(r => r.MatchedPersonnel?.ExitDay)
                                  .FirstOrDefault(d => d.HasValue && d.Value > 0);

            if (entryDay.HasValue)
            {
                startDay = Math.Min(Math.Max(entryDay.Value, 1), monthDays);
            }

            if (exitDay.HasValue)
            {
                endDay = Math.Min(Math.Max(exitDay.Value, startDay), monthDays);
            }

            return (startDay, endDay);
        }

        private void ApplyWorkedVacationDayOvertime(PDKSDataModel record, CompanyConfig companyConfig, EarnedRestDayDetail detail)
        {
            if (record == null || companyConfig?.SpecialOvertimeSettings == null)
            {
                return;
            }

            if (record.IsOfficialHoliday && record.WorkedOnOfficialHoliday)
            {
                // Resmi tatil için ayrı oran uygulanıyor, tekrar etmeyelim
                return;
            }

            AssignEarnedRestMetadata(record, detail);

            record.WorkedOnEarnedRestDay = true;
            ApplySpecialOvertime(record,
                                 companyConfig.SpecialOvertimeSettings.EarnedRestDayColumnName,
                                 companyConfig.SpecialOvertimeSettings,
                                 record.WorkedHours,
                                 "Hak edilen tatil çalışması");
        }

        private void AddHoursToFmColumn(PDKSDataModel record, string columnName, double hours)
        {
            if (record == null || string.IsNullOrWhiteSpace(columnName) || hours <= 0)
            {
                return;
            }

            if (!record.FMColumnHours.ContainsKey(columnName))
            {
                record.FMColumnHours[columnName] = 0;
            }

            record.FMColumnHours[columnName] += hours;
        }

        private void ApplySpecialOvertime(PDKSDataModel record, string columnName, SpecialOvertimeSettings settings, double hours, string context)
        {
            if (record == null || hours <= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(columnName))
            {
                Console.WriteLine($"[Özel FM] {context}: Kolon adı tanımlı değil, işlem atlandı.");
                return;
            }

            if (!record.SpecialOvertimeEffectiveDate.HasValue)
            {
                record.SpecialOvertimeEffectiveDate = record.Date;
            }

            AddHoursToFmColumn(record, columnName, hours);

            string columnLetter = null;
            if (settings != null)
            {
                if (string.Equals(columnName, settings.EarnedRestDayColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    columnLetter = settings.EarnedRestDayColumnLetter;
                }
                else if (string.Equals(columnName, settings.HolidayWorkColumnName, StringComparison.OrdinalIgnoreCase))
                {
                    columnLetter = settings.HolidayWorkColumnLetter;
                }
            }

            if (!string.IsNullOrWhiteSpace(columnLetter))
            {
                Console.WriteLine($"[Özel FM] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {context} için {hours:F2} saat -> {columnName} ({columnLetter} sütunu)");
                File.AppendAllText("debug_log.txt", $"[Özel FM] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {context} için {hours:F2} saat -> {columnName} ({columnLetter} sütunu)\n");
            }
            else
            {
                Console.WriteLine($"[Özel FM] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {context} için {hours:F2} saat -> {columnName}");
                File.AppendAllText("debug_log.txt", $"[Özel FM] {record.PersonnelName} {record.Date:dd.MM.yyyy}: {context} için {hours:F2} saat -> {columnName}\n");
            }
        }

        private Dictionary<DateTime, HolidayInfo> BuildHolidayLookup(CompanyConfig companyConfig)
        {
            var result = new Dictionary<DateTime, HolidayInfo>();

            if (companyConfig == null)
            {
                return result;
            }

            int year = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            var holidays = calendarService.GetHolidaysForYear(year);
            var applicableHolidays = holidays.ToList();

            if (companyConfig.ActiveOfficialHolidayDates != null &&
                companyConfig.ActiveOfficialHolidayDates.TryGetValue(year, out var activeDates) &&
                activeDates != null)
            {
                if (activeDates.Count == 0)
                {
                    applicableHolidays = new List<HolidayInfo>();
                }
                else
                {
                    var activeSet = new HashSet<DateTime>();
                    var halfDayOverrides = new Dictionary<DateTime, bool>();
                    var invalidEntries = new List<string>();

                    foreach (var dateString in activeDates)
                    {
                        if (string.IsNullOrWhiteSpace(dateString))
                        {
                            continue;
                        }

                        if (HolidaySelectionSerializer.TryDeserialize(dateString, out var parsedDate, out var treatAsHalfDay))
                        {
                            var dateOnly = parsedDate.Date;
                            activeSet.Add(dateOnly);
                            if (treatAsHalfDay.HasValue)
                            {
                                halfDayOverrides[dateOnly] = treatAsHalfDay.Value;
                            }
                            continue;
                        }

                        if (DateTime.TryParseExact(dateString.Trim(),
                                                   "yyyy-MM-dd",
                                                   CultureInfo.InvariantCulture,
                                                   DateTimeStyles.None,
                                                   out var legacyDate))
                        {
                            activeSet.Add(legacyDate.Date);
                        }
                        else
                        {
                            invalidEntries.Add(dateString);
                        }
                    }

                    if (invalidEntries.Count > 0)
                    {
                        Console.WriteLine($"[Resmi Tatil] Uyarı: Firma {companyConfig.CompanyCode} için geçersiz tatil tarihleri: {string.Join(", ", invalidEntries)}");
                    }

                    if (activeSet.Count > 0)
                    {
                        applicableHolidays = holidays
                            .Where(h => activeSet.Contains(h.Date.Date))
                            .Select(h =>
                            {
                                var dateOnly = h.Date.Date;
                                var adjusted = new HolidayInfo
                                {
                                    Date = h.Date,
                                    Name = h.Name,
                                    IsHalfDay = halfDayOverrides.TryGetValue(dateOnly, out var overrideValue) ? overrideValue : h.IsHalfDay
                                };
                                return adjusted;
                            })
                            .ToList();
                    }
                    else
                    {
                        applicableHolidays = new List<HolidayInfo>();
                    }
                }
            }

            foreach (var holiday in applicableHolidays)
            {
                var date = holiday.Date.Date;
                if (!result.ContainsKey(date))
                {
                    result[date] = holiday;
                }
            }

            return result;
        }

        /// <summary>
        /// Bir kaydın ShiftType'ına göre uygun ShiftRuleConfig'i bulur (firma bazlı)
        /// </summary>
        private ShiftRuleConfig GetShiftConfigForRecord(CompanyConfig companyConfig, string shiftType)
        {
            if (companyConfig.ShiftRuleConfigs == null || companyConfig.ShiftRuleConfigs.Count == 0)
            {
                return null;
            }

            string normalizedShift = NormalizeShiftString(shiftType);

            Console.WriteLine($"[VARDİYA EŞLEŞME] Personel: {companyConfig.CompanyName}, ShiftType: '{shiftType}' -> Normalized: '{normalizedShift}'");
            // File.AppendAllText("debug_log.txt", $"[VARDİYA EŞLEŞME] Personel: {companyConfig.CompanyName}, ShiftType: '{shiftType}' -> Normalized: '{normalizedShift}'\n"); // Kaldırıldı

                foreach (var shiftConfig in companyConfig.ShiftRuleConfigs)
                {
                foreach (var pattern in shiftConfig.ShiftPatterns)
                {
                    if (pattern == "*")
                        continue;

                    string normalizedPattern = NormalizeShiftString(pattern);
                    Console.WriteLine($"[VARDİYA EŞLEŞME] Pattern kontrol: '{pattern}' -> '{normalizedPattern}' == '{normalizedShift}' ? {string.Equals(normalizedPattern, normalizedShift, StringComparison.OrdinalIgnoreCase)}");
                    if (string.Equals(normalizedPattern, normalizedShift, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"[VARDİYA EŞLEŞME] ✅ EŞLEŞTİ: {shiftConfig.GroupName} grubu seçildi");
                        return shiftConfig;
                    }
                }

                if (shiftConfig.ShiftPatternMappings != null)
                {
                    foreach (var mapping in shiftConfig.ShiftPatternMappings)
                    {
                        if (string.Equals(NormalizeShiftString(mapping), normalizedShift, StringComparison.OrdinalIgnoreCase))
                        {
                            return shiftConfig;
                        }
                    }
                }
            }

                foreach (var shiftConfig in companyConfig.ShiftRuleConfigs)
                {
                    if (shiftConfig.ShiftPatterns.Contains("*"))
                    {
                        return shiftConfig;
                }
            }

            return null;
        }

        private string NormalizeShiftString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            string cleaned = input.Trim();
            int parenIndex = cleaned.IndexOf('(');
            if (parenIndex >= 0)
            {
                cleaned = cleaned.Substring(0, parenIndex);
            }

            cleaned = cleaned.Replace("–", "-").Replace("—", "-");
            cleaned = cleaned.Replace("*", "-").Replace("/", "-").Replace("\\", "-");
            cleaned = cleaned.Replace(" to ", "-").Replace("TO", "-");
            cleaned = cleaned.Replace(" ", "");

            if (cleaned.Contains("-"))
            {
                var parts = cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    if (TryNormalizeTimeSegment(parts[0], out var startKey) && TryNormalizeTimeSegment(parts[1], out var endKey))
                    {
                        return $"{startKey}-{endKey}";
                    }
                }
            }

            return cleaned.ToLowerInvariant();
        }

        private bool TryNormalizeTimeSegment(string segment, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }

            string cleaned = segment.Trim();
            string originalCleaned = cleaned;
            cleaned = cleaned.Replace(".", ":");

            try
            {
                if (TimeSpan.TryParse(cleaned, out var time))
                {
                    normalized = time.ToString(@"HHmm");
                    return true;
                }
            }
            catch (Exception ex)
            {
                // File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [SAAT PARSE HATA] TimeSpan.TryParse('{originalCleaned}') hatası: {ex.Message}\n"); // Kaldırıldı
            }

            string digitsOnly = new string(cleaned.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digitsOnly))
            {
                // File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [SAAT PARSE] '{originalCleaned}' -> digitsOnly: '{digitsOnly}' (length: {digitsOnly.Length})\n"); // Kaldırıldı

                if (digitsOnly.Length == 4 &&
                    int.TryParse(digitsOnly.Substring(0, 2), out var hourFromFour) &&
                    int.TryParse(digitsOnly.Substring(2, 2), out var minuteFromFour) &&
                    hourFromFour >= 0 && hourFromFour < 24 &&
                    minuteFromFour >= 0 && minuteFromFour < 60)
                {
                    normalized = $"{hourFromFour:00}{minuteFromFour:00}";
                    // File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [SAAT PARSE] '{originalCleaned}' -> 4 digit OK: '{normalized}'\n"); // Kaldırıldı
                    return true;
                }

                if (digitsOnly.Length == 3 &&
                    int.TryParse(digitsOnly.Substring(0, 1), out var hourFromThree) &&
                    int.TryParse(digitsOnly.Substring(1, 2), out var minuteFromThree) &&
                    hourFromThree >= 0 && hourFromThree < 24 &&
                    minuteFromThree >= 0 && minuteFromThree < 60)
                {
                    normalized = $"{hourFromThree:00}{minuteFromThree:00}";
                    // File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [SAAT PARSE] '{originalCleaned}' -> 3 digit OK: '{normalized}'\n"); // Kaldırıldı
                    return true;
                }
            }

            try
            {
                if (int.TryParse(cleaned, out var hour) && hour >= 0 && hour < 24)
                {
                    normalized = $"{hour:00}00";
                    return true;
                }
            }
            catch (Exception ex)
            {
                // File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [SAAT PARSE HATA] int.TryParse('{originalCleaned}') hatası: {ex.Message}\n"); // Kaldırıldı
            }

            // File.AppendAllText("debug_log.txt", $"[{DateTime.Now:HH:mm:ss}] [SAAT PARSE] '{originalCleaned}' -> PARSING BAŞARISIZ\n"); // Kaldırıldı
            return false;
        }

        /// <summary>
        /// Yeni OvertimeRules sistemi ile fazla mesai hesaplar
        /// Yeni mantık: Normal çalışma saati - Vardiya aralığı bazlı dağıtım
        /// </summary>
        private OvertimeAllocationResult CalculateOvertimeWithRules(double workedHours, TimeSpan checkOutTime, ShiftRuleConfig shiftConfig)
        {
            double totalOvertimeHours = workedHours - shiftConfig.StandardHours;

            if (totalOvertimeHours <= 0)
            {
                return new OvertimeAllocationResult();
            }

            if (shiftConfig.OvertimeRules == null || shiftConfig.OvertimeRules.Count == 0)
            {
                return new OvertimeAllocationResult
                {
                    NormalHours = Math.Max(0, totalOvertimeHours)
                };
            }

            // Yeni FM mantığı: Vardiya aralığı bazlı kontrol
            // Eğer kurallarda vardiya aralığı bilgisi varsa yeni mantığı kullan
            if (IsNewOvertimeLogic(shiftConfig.OvertimeRules))
            {
                return CalculateOvertimeWithNewLogic(workedHours, checkOutTime, shiftConfig);
            }

            // Eski bucket kuralları sistemi
            bool useBucketRules = shiftConfig.OvertimeRules.Any(r => r.DurationHours.HasValue || r.IsCatchAll);

            if (useBucketRules)
            {
                return CalculateOvertimeUsingBuckets(totalOvertimeHours, shiftConfig.OvertimeRules);
            }

            return CalculateOvertimeLegacy(workedHours, checkOutTime, shiftConfig);
        }

        /// <summary>
        /// Yeni FM mantığını kullanıp kullanmadığımızı kontrol eder
        /// </summary>
        private bool IsNewOvertimeLogic(List<OvertimeRule> rules)
        {
            if (rules == null || rules.Count == 0)
                return false;

            // Yeni mantık kurallarında description'da saat aralığı bilgileri bulunur
            return rules.Any(r => r.Description.Contains("-") && r.Description.Contains("arası"));
        }

        /// <summary>
        /// Yeni FM mantığı ile hesaplama:
        /// - Normal çalışma saati aşımı (7.5h üzeri) → FM Normal
        /// - Vardiya aralığı aşımı (9h üzeri) → FM %50
        /// </summary>
        private OvertimeAllocationResult CalculateOvertimeWithNewLogic(double workedHours, TimeSpan checkOutTime, ShiftRuleConfig shiftConfig)
        {
            var result = new OvertimeAllocationResult();

            // Normal çalışma saati aşımını hesapla
            double normalOvertime = Math.Max(0, workedHours - shiftConfig.StandardHours);

            if (normalOvertime <= 0)
                return result;

            // Kurallardan vardiya aralığı bilgisini çıkar
            double shiftDuration = ExtractShiftDurationFromRules(shiftConfig);

            if (shiftDuration <= shiftConfig.StandardHours)
            {
                // Vardiya aralığı geçerli değilse, tüm fazla çalışmayı FM Normal'e aktar
                result.NormalHours = normalOvertime;
                PopulateColumnHoursForNewLogic(result, shiftConfig);
                return result;
            }

            // Vardiya aralığı aşımı kontrolü
            double shiftDurationOvertime = Math.Max(0, workedHours - shiftDuration);

            if (shiftDurationOvertime > 0)
            {
                // Vardiya aralığı aşılmış - FM %50'ye aktar
                result.SpecialHours = shiftDurationOvertime;

                // Kalan kısmı FM Normal'e aktar
                result.NormalHours = Math.Max(0, normalOvertime - shiftDurationOvertime);
            }
            else
            {
                // Sadece normal çalışma saati aşılmış - tümünü FM Normal'e aktar
                result.NormalHours = normalOvertime;
            }

            PopulateColumnHoursForNewLogic(result, shiftConfig);

            Console.WriteLine($"[Yeni FM Mantığı] Çalışma: {workedHours:F1}h, Normal: {shiftConfig.StandardHours:F1}h, Vardiya: {shiftDuration:F1}h, FM Normal: {result.NormalHours:F1}h, FM %50: {result.SpecialHours:F1}h");

            return result;
        }

        private void PopulateColumnHoursForNewLogic(OvertimeAllocationResult result, ShiftRuleConfig shiftConfig)
        {
            if (result == null || shiftConfig?.OvertimeRules == null)
            {
                return;
            }

            if (result.NormalHours > 0)
            {
                var normalRule = shiftConfig.OvertimeRules
                    .FirstOrDefault(r => r.Rate < 1.5 && !string.IsNullOrWhiteSpace(r.ColumnName));

                string columnName = normalRule?.ColumnName ?? "B04 Fazla Mesai Normal";

                result.ColumnHours[columnName] = result.NormalHours;
                Console.WriteLine($"[Yeni FM Mantığı] Kolon ataması - {columnName}: {result.NormalHours:F2} saat (FM Normal)");
                File.AppendAllText("debug_log.txt", $"[Yeni FM Mantığı] Kolon ataması - {columnName}: {result.NormalHours:F2} saat (FM Normal)\n");
            }

            if (result.SpecialHours > 0)
            {
                var specialRule = shiftConfig.OvertimeRules
                    .FirstOrDefault(r => r.Rate >= 1.5 && !string.IsNullOrWhiteSpace(r.ColumnName));

                string columnName = specialRule?.ColumnName ?? "B01 %50 Fazla Mesai";

                if (result.ColumnHours.ContainsKey(columnName))
                {
                    result.ColumnHours[columnName] += result.SpecialHours;
                }
                else
                {
                    result.ColumnHours[columnName] = result.SpecialHours;
                }

                Console.WriteLine($"[Yeni FM Mantığı] Kolon ataması - {columnName}: {result.SpecialHours:F2} saat (FM %50)");
                File.AppendAllText("debug_log.txt", $"[Yeni FM Mantığı] Kolon ataması - {columnName}: {result.SpecialHours:F2} saat (FM %50)\n");
            }
        }

        /// <summary>
        /// FM kurallarından vardiya aralığı bilgisini çıkarır
        /// </summary>
        private double ExtractShiftDurationFromRules(ShiftRuleConfig shiftConfig)
        {
            if (shiftConfig.OvertimeRules == null || shiftConfig.OvertimeRules.Count == 0)
                return 9.0; // Varsayılan

            // Eğer shiftConfig.ShiftDuration tanımlıysa onu kullan
            if (shiftConfig.ShiftDuration > 0)
            {
                return shiftConfig.ShiftDuration;
            }

            // İlk kuralın açıklamasından saat aralığını çıkar
            var firstRule = shiftConfig.OvertimeRules.FirstOrDefault();
            if (firstRule != null && !string.IsNullOrEmpty(firstRule.Description))
            {
                // "İlk 1.5 saat FM Normal (7.5-9.0h arası)" gibi bir açıklamadan 9.0 değerini çıkar
                var match = System.Text.RegularExpressions.Regex.Match(firstRule.Description, @"(\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)h");
                if (match.Success && match.Groups.Count >= 3)
                {
                    if (double.TryParse(match.Groups[2].Value, out double shiftDuration))
                    {
                        return shiftDuration;
                    }
                }
            }

            return 9.0; // Varsayılan vardiya aralığı
        }

        private OvertimeAllocationResult CalculateOvertimeUsingBuckets(double totalOvertimeHours, List<OvertimeRule> rules)
        {
            double remaining = Math.Max(0, totalOvertimeHours);
            var result = new OvertimeAllocationResult();

            if (remaining <= 0)
            {
                return result;
            }

            var orderedRules = rules
                .OrderBy(r => r.IsCatchAll)
                .ThenBy(r => r.DurationHours.HasValue ? r.DurationHours.Value : double.MaxValue)
                .ToList();

            foreach (var rule in orderedRules)
            {
                if (remaining <= 0)
                {
                    break;
                }

                double allocation;
                if (rule.DurationHours.HasValue && !rule.IsCatchAll)
                {
                    allocation = Math.Min(remaining, Math.Max(0, rule.DurationHours.Value));
                }
                else
                {
                    allocation = remaining;
                }

                if (allocation <= 0)
                {
                    continue;
                }

                ApplyAllocation(result, rule, allocation);

                remaining -= allocation;
            }

            if (remaining > 0)
            {
                var lastRule = orderedRules.LastOrDefault();
                ApplyAllocation(result, lastRule, remaining);
            }

            return result;
        }

        private OvertimeAllocationResult CalculateOvertimeLegacy(double workedHours, TimeSpan checkOutTime, ShiftRuleConfig shiftConfig)
        {
            var result = new OvertimeAllocationResult();

            if (shiftConfig.OvertimeRules == null || shiftConfig.OvertimeRules.Count == 0)
            {
                double totalOvertime = workedHours - shiftConfig.StandardHours;
                result.NormalHours = Math.Max(0, totalOvertime);
                return result;
            }

            double totalOvertimeHours = workedHours - shiftConfig.StandardHours;
            var sortedRules = shiftConfig.OvertimeRules
                .Where(r => r.StartTime.HasValue)
                .OrderBy(r => r.StartTime.Value)
                .ToList();

            foreach (var rule in sortedRules)
            {
                double ruleStartHour = rule.StartTime.Value.TotalHours;
                double nextRuleStartHour = double.MaxValue;

                var nextRule = sortedRules.FirstOrDefault(r => r.StartTime > rule.StartTime);
                if (nextRule != null && nextRule.StartTime.HasValue)
                {
                    nextRuleStartHour = nextRule.StartTime.Value.TotalHours;
                }

                double ruleOvertimeHours = 0;

                if (totalOvertimeHours > 0)
                {
                    double overtimeStartHour = shiftConfig.StandardHours;

                    if (ruleStartHour >= overtimeStartHour)
                    {
                        double ruleEndHour = Math.Min(nextRuleStartHour, workedHours);
                        double ruleStart = Math.Max(overtimeStartHour, ruleStartHour);

                        if (ruleEndHour > ruleStart)
                        {
                            ruleOvertimeHours = ruleEndHour - ruleStart;
                        }
                    }
                }

                if (ruleOvertimeHours > 0)
                {
                    ApplyAllocation(result, rule, ruleOvertimeHours);
                }
            }

            return result;
        }

        private void ApplyAllocation(OvertimeAllocationResult result, OvertimeRule rule, double hours)
        {
            if (result == null || rule == null || hours <= 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(rule.ColumnName))
            {
                if (result.ColumnHours.ContainsKey(rule.ColumnName))
                {
                    result.ColumnHours[rule.ColumnName] += hours;
                }
                else
                {
                    result.ColumnHours[rule.ColumnName] = hours;
                }
            }

            if (rule.Rate >= 1.5)
            {
                result.SpecialHours += hours;
            }
            else
            {
                result.NormalHours += hours;
            }
        }

        public void CalculateAbsenteeism(int monthDays)
        {
            // Personel bazında devamsızlık hesapla
            var personnelGroups = pdksRecords.Where(r => r.IsMatched)
                                           .GroupBy(r => r.PersonnelCode);

            foreach (var personnelGroup in personnelGroups)
            {
                var personnelCode = personnelGroup.Key;
                var personnelRecords = personnelGroup.ToList();

                var range = GetEffectiveEmploymentRange(personnelRecords, monthDays);
                int effectiveDays = Math.Max(0, range.EndDay - range.StartDay + 1);

                var workDaySet = new HashSet<int>(
                    personnelRecords
                        .Where(r => r.WorkedHours > 0)
                        .Select(r => (r.SpecialOvertimeEffectiveDate ?? r.Date).Day));
                workDaySet.RemoveWhere(d => d < range.StartDay || d > range.EndDay);

                var vacationDaySet = new HashSet<int>(
                    personnelRecords
                        .Where(r => r.VacationDays > 0)
                        .Select(r => (r.SpecialOvertimeEffectiveDate ?? r.Date).Day));
                vacationDaySet.RemoveWhere(d => d < range.StartDay || d > range.EndDay);

                int absentDays = Math.Max(0, effectiveDays - workDaySet.Count - vacationDaySet.Count);

                // Tüm kayıtlara aynı devamsızlık değerini ata
                foreach (var record in personnelRecords)
                {
                    record.AbsentDays = absentDays;
                }

                Console.WriteLine($"[Devamsızlık] Personel {personnelCode}: Aralık {range.StartDay}-{range.EndDay} ({effectiveDays} gün) - Çalışılan {workDaySet.Count} gün, Tatil {vacationDaySet.Count} gün => Devamsızlık {absentDays} gün");
            }
        }

        public int GetMatchedCount()
        {
            // Tekil personel sayısını döndür (aynı personelin birden fazla kaydı olabilir)
            return pdksRecords.Where(r => r.IsMatched)
                              .GroupBy(r => r.PersonnelCode)
                              .Count();
        }

        public int GetTotalCount()
        {
            return pdksRecords.Count;
        }

        public Dictionary<string, int> GetMatchingSummary()
        {
            return new Dictionary<string, int>
            {
                { "Toplam Kayıt", GetTotalCount() },
                { "Eşleşen Kayıt", GetMatchedCount() },
                { "Eşleşmeyen Kayıt", GetTotalCount() - GetMatchedCount() }
            };
        }

        public List<PersonnelSummary> CalculatePersonnelSummaries(CompanyConfig companyConfig)
        {
            // Ay filtrelemesi ekle
            int year = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            int month = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;
            
            // Sadece seçili ayın kayıtlarını işle
            var filteredRecords = pdksRecords.Where(r => r.IsMatched && r.Date.Year == year && r.Date.Month == month).ToList();
            
            Console.WriteLine($"[Yekün Hesaplamalar] Başlıyor - {year}/{month:D2} ayı için {filteredRecords.GroupBy(r => r.PersonnelCode).Count()} personel, toplam {filteredRecords.Count} kayıt");

            var personnelSummaries = new List<PersonnelSummary>();

            // Personel bazında gruplandırma (ay filtrelenmiş kayıtlarla)
            var personnelGroups = filteredRecords.GroupBy(r => r.PersonnelCode);

            foreach (var personnelGroup in personnelGroups)
            {
                var personnelCode = personnelGroup.Key;
                var personnelRecords = personnelGroup.ToList();

                var summary = new PersonnelSummary
                {
                    PersonnelCode = personnelCode,
                    PersonnelName = personnelRecords.First().PersonnelName,
                    TCNo = personnelRecords.First().TCNo
                };

                // Günlük kayıtları tarihe göre sırala
                var sortedRecords = personnelRecords.OrderBy(r => r.Date).ToList();

                // Ardışık çalışma günlerini takip et
                int consecutiveWorkDays = 0;
                int maxConsecutiveDays = 0;
                DateTime? lastWorkDate = null;
                int earnedVacationDays = 0;
                var shiftGroupHours = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

                foreach (var record in sortedRecords)
                {
                    // Çalışma günü mü kontrol et
                    bool isWorkDay = record.WorkedHours > 0;

                    if (isWorkDay)
                    {
                        var shiftConfig = GetShiftConfigForRecord(companyConfig, record.ShiftType);
                        if (shiftConfig != null)
                        {
                            string groupName = string.IsNullOrWhiteSpace(shiftConfig.GroupName)
                                ? "Varsayılan Vardiya"
                                : shiftConfig.GroupName;
                            if (!shiftGroupHours.ContainsKey(groupName))
                            {
                                shiftGroupHours[groupName] = 0;
                            }
                            shiftGroupHours[groupName] += record.WorkedHours;
                        }

                        // Toplam çalışma saatlerini topla
                        summary.TotalWorkedHours += record.WorkedHours;
                        summary.TotalAbsentHours += record.AbsentHours;
                        summary.TotalWorkDays++;

                        Console.WriteLine($"[PERSONEL TOPLAMI] {record.PersonnelName} - Ekleniyor: Çalışma: {record.WorkedHours:F2}h, Eksik: {record.AbsentHours:F2}h, FM Normal: {record.FMNormalHours:F2}h, FM %50: {record.FM50PercentHours:F2}h");

                        // Vardiya bazlı saat toplamları
                        if (!summary.ShiftTypeHours.ContainsKey(record.ShiftType))
                            summary.ShiftTypeHours[record.ShiftType] = 0;
                        summary.ShiftTypeHours[record.ShiftType] += record.WorkedHours;

                        // Fazla mesai saatlerini topla
                        summary.TotalFMNormalHours += record.FMNormalHours;
                        summary.TotalFM50PercentHours += record.FM50PercentHours;

                        Console.WriteLine($"[PERSONEL TOPLAMI] {record.PersonnelName} - Güncel Toplam: Çalışma: {summary.TotalWorkedHours:F2}h, Eksik: {summary.TotalAbsentHours:F2}h, FM Normal: {summary.TotalFMNormalHours:F2}h, FM %50: {summary.TotalFM50PercentHours:F2}h");
                        File.AppendAllText("debug_log.txt", $"[PERSONEL TOPLAMI] {record.PersonnelName} - Güncel Toplam: Çalışma: {summary.TotalWorkedHours:F2}h, Eksik: {summary.TotalAbsentHours:F2}h, FM Normal: {summary.TotalFMNormalHours:F2}h, FM %50: {summary.TotalFM50PercentHours:F2}h\n");
                        if (record.FMColumnHours != null)
                        {
                            foreach (var kv in record.FMColumnHours)
                            {
                                if (!summary.FMColumnTotals.ContainsKey(kv.Key))
                                {
                                    summary.FMColumnTotals[kv.Key] = 0;
                                }
                                summary.FMColumnTotals[kv.Key] += kv.Value;
                            }
                        }

                        // Ardışık çalışma hesabı
                        if (lastWorkDate.HasValue && record.Date == lastWorkDate.Value.AddDays(1))
                        {
                            consecutiveWorkDays++;
                            if (consecutiveWorkDays > maxConsecutiveDays)
                                maxConsecutiveDays = consecutiveWorkDays;
                        }
                        else
                        {
                            consecutiveWorkDays = 1;
                        }

                        lastWorkDate = record.Date;

                        // Tatil kazanma kontrolü
                        if (shiftConfig != null && consecutiveWorkDays >= shiftConfig.ConsecutiveDaysForVacation &&
                            consecutiveWorkDays % shiftConfig.ConsecutiveDaysForVacation == 0)
                        {
                            earnedVacationDays += shiftConfig.VacationDays;
                        }
                    }
                    else
                    {
                        // Çalışma günü değilse ardışık sayacı sıfırla
                        consecutiveWorkDays = 0;
                    }
                }

                // Hak edilen ve kullanılan tatil günlerini ata
                int actualVacationDays = personnelRecords.Count(r => r.VacationDays > 0);
                summary.TotalConsecutiveDays = maxConsecutiveDays;
                if (earnedVacationDays != actualVacationDays)
                {
                    Console.WriteLine($"[Tatil Kullanımı] {summary.PersonnelName}: {earnedVacationDays} gün hak edildi, {actualVacationDays} gün kullanıldı");
                    File.AppendAllText("debug_log.txt", $"[Tatil Kullanımı] {summary.PersonnelName}: {earnedVacationDays} gün hak edildi, {actualVacationDays} gün kullanıldı\n");
                }

                // Eksik gün hesaplaması - EN ÖNEMLİ KRİTER (kullanıcının belirttiği formül)
                // eksik gün = ayın toplam günü - (çalışılan gün sayısı + tatil gün sayısı)
                int monthDays;
                if (companyConfig != null)
                {
                    int payrollYear = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
                    int payrollMonth = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;
                    monthDays = DateTime.DaysInMonth(payrollYear, payrollMonth);
                }
                else
                {
                    monthDays = 30;
                }
                var range = GetEffectiveEmploymentRange(personnelRecords, monthDays);
                int effectiveDays = Math.Max(0, range.EndDay - range.StartDay + 1);

                var workDaySet = new HashSet<int>(
                    personnelRecords
                        .Where(r => r.WorkedHours > 0)
                        .Select(r => (r.SpecialOvertimeEffectiveDate ?? r.Date).Day));
                workDaySet.RemoveWhere(d => d < range.StartDay || d > range.EndDay);

                var vacationDaySet = new HashSet<int>(
                    personnelRecords
                        .Where(r => r.VacationDays > 0)
                        .Select(r => (r.SpecialOvertimeEffectiveDate ?? r.Date).Day));
                vacationDaySet.RemoveWhere(d => d < range.StartDay || d > range.EndDay);

                summary.TotalWorkDays = workDaySet.Count;
                summary.TotalVacationDays = vacationDaySet.Count;
                summary.TotalAbsentDays = Math.Max(0, effectiveDays - (summary.TotalWorkDays + summary.TotalVacationDays));

                if (shiftGroupHours.Count > 0)
                {
                    var dominant = shiftGroupHours.OrderByDescending(kv => kv.Value).First();
                    summary.PrimaryShiftGroupName = dominant.Key;
                }

                // Detaylı eksik gün hesaplaması log
                Console.WriteLine($"[Eksik Gün Hesaplama - KRİTİK] {summary.PersonnelName} ({personnelCode}):");
                Console.WriteLine($"  Personel dönemi: {range.StartDay}. gün - {range.EndDay}. gün (Etkin gün: {effectiveDays})");
                Console.WriteLine($"  Çalışılan gün sayısı: {summary.TotalWorkDays}");
                Console.WriteLine($"  Tatil gün sayısı: {summary.TotalVacationDays}");
                Console.WriteLine($"  Formül: {effectiveDays} - ({summary.TotalWorkDays} + {summary.TotalVacationDays}) = eksik gün");
                Console.WriteLine($"  HESAPLANAN EKSİK GÜN: {summary.TotalAbsentDays} gün");

                File.AppendAllText("debug_log.txt", $"[Eksik Gün Hesaplama - KRİTİK] {summary.PersonnelName} ({personnelCode}):\n");
                File.AppendAllText("debug_log.txt", $"  Personel dönemi: {range.StartDay}. gün - {range.EndDay}. gün (Etkin gün: {effectiveDays})\n");
                File.AppendAllText("debug_log.txt", $"  Çalışılan gün sayısı: {summary.TotalWorkDays}\n");
                File.AppendAllText("debug_log.txt", $"  Tatil gün sayısı: {summary.TotalVacationDays}\n");
                File.AppendAllText("debug_log.txt", $"  Formül: {effectiveDays} - ({summary.TotalWorkDays} + {summary.TotalVacationDays}) = eksik gün\n");
                File.AppendAllText("debug_log.txt", $"  HESAPLANAN EKSİK GÜN: {summary.TotalAbsentDays} gün\n");

                // Ortalamaları hesapla
                summary.CalculateAverages();

                personnelSummaries.Add(summary);

                Console.WriteLine($"[Yekün Hesaplama] {summary.PersonnelName} ({personnelCode}): {summary.TotalWorkDays} gün çalışma");
                Console.WriteLine($"  - Toplam Çalışma: {summary.TotalWorkedHours:F1}h");
                Console.WriteLine($"  - FM Normal: {summary.TotalFMNormalHours:F1}h, FM %50: {summary.TotalFM50PercentHours:F1}h");
                Console.WriteLine($"  - Tatil: {summary.TotalVacationDays} gün, Eksik Gün: {summary.TotalAbsentDays} gün");
            }

            Console.WriteLine($"[Yekün Hesaplamalar] Tamamlandı - {personnelSummaries.Count} personel özeti oluşturuldu");
            return personnelSummaries;
        }
    }

    /// <summary>
    /// Grup bazlı vardiya kuralları için yapılandırma
    /// </summary>
    public class ShiftRuleConfig
    {
        public string GroupName { get; set; } // Grup adı (örn: "8/18 Vardiyaları", "Diğer Vardiyalar")
        public List<string> ShiftPatterns { get; set; } // Bu gruba dahil olan vardiya paternleri (örn: ["8/18", "8/19"])
        public List<string> ShiftPatternMappings { get; set; } // Vardiya eşleşmesi için ek alternatifler
        public TimeSpan DefaultStartTime { get; set; } // Varsayılan başlangıç saati
        public TimeSpan DefaultEndTime { get; set; } // Varsayılan bitiş saati
        public double StandardHours { get; set; } // Standart çalışma saati
        public double ShiftDuration { get; set; } // Vardiya Aralığı (mola dahil)
        public double BreakHours { get; set; } // Mola süresi (saat)
        public bool AssignNormalFM { get; set; } // Normal FM atanıp atanmayacağını belirtir
        public int ConsecutiveDaysForVacation { get; set; } // Kaç gün üst üste çalışınca tatil
        public int VacationDays { get; set; } // Kaç gün tatil verilecek
        public string AbsentDaysColumnLetter { get; set; } = "K"; // Eksik günlerin yazılacağı Excel kolonu

        // Fazla mesai kuralları - esnek yapı
        public List<OvertimeRule> OvertimeRules { get; set; } // Çoklu fazla mesai kuralları

        public ShiftRuleConfig()
        {
            ShiftPatterns = new List<string>();
            OvertimeRules = new List<OvertimeRule>();
            ShiftPatternMappings = new List<string>();
            BreakHours = 1.0;
            ConsecutiveDaysForVacation = 6;
            VacationDays = 1;
            AbsentDaysColumnLetter = "K";
        }
    }

    /// <summary>
    /// Fazla mesai kuralı - hangi saat aralığında ne kadar fazla mesai
    /// </summary>
    public class OvertimeRule
    {
        public TimeSpan? StartTime { get; set; } // Eski sürümlerle uyumluluk için (güncel hesapta kullanılmaz)
        public double Rate { get; set; } // Fazla mesai katsayısı (1.0 = normal, 1.5 = %50, 2.0 = %100)
        public string Description { get; set; } // Açıklama
        public string ColumnName { get; set; } // ERP şablonunda yazılacak kolon
        public double? DurationHours { get; set; } // Kaç saatlik fazla mesai bu kolona yazılacak (null ise kalan tüm saatler)
        public bool IsCatchAll { get; set; } // Kalan tüm saatleri bu kurala aktar

        public OvertimeRule()
        {
            Rate = 1.0;
            Description = "";
            ColumnName = "";
            DurationHours = null;
            IsCatchAll = false;
        }
    }

    public class SpecialOvertimeSettings : System.ComponentModel.INotifyPropertyChanged
    {
        private string _earnedRestDayColumnName;
        private string _earnedRestDayColumnLetter;
        private string _holidayWorkColumnName;
        private string _holidayWorkColumnLetter;
        private string _weekendWorkColumnName;
        private string _weekendWorkColumnLetter;
        private bool _useManualEarnedRestLetter;
        private bool _useManualHolidayLetter;
        private bool _useManualWeekendLetter;

        public string EarnedRestDayColumnName
        {
            get => _earnedRestDayColumnName;
            set
            {
                if (_earnedRestDayColumnName == value) return;
                _earnedRestDayColumnName = value;
                OnPropertyChanged(nameof(EarnedRestDayColumnName));
            }
        }

        public string EarnedRestDayColumnLetter
        {
            get => _earnedRestDayColumnLetter;
            set
            {
                if (_earnedRestDayColumnLetter == value) return;
                _earnedRestDayColumnLetter = value;
                OnPropertyChanged(nameof(EarnedRestDayColumnLetter));
            }
        }

        public string HolidayWorkColumnName
        {
            get => _holidayWorkColumnName;
            set
            {
                if (_holidayWorkColumnName == value) return;
                _holidayWorkColumnName = value;
                OnPropertyChanged(nameof(HolidayWorkColumnName));
            }
        }

        public string HolidayWorkColumnLetter
        {
            get => _holidayWorkColumnLetter;
            set
            {
                if (_holidayWorkColumnLetter == value) return;
                _holidayWorkColumnLetter = value;
                OnPropertyChanged(nameof(HolidayWorkColumnLetter));
            }
        }

        public bool UseManualEarnedRestLetter
        {
            get => _useManualEarnedRestLetter;
            set
            {
                if (_useManualEarnedRestLetter == value) return;
                _useManualEarnedRestLetter = value;
                OnPropertyChanged(nameof(UseManualEarnedRestLetter));
            }
        }

        public bool UseManualHolidayLetter
        {
            get => _useManualHolidayLetter;
            set
            {
                if (_useManualHolidayLetter == value) return;
                _useManualHolidayLetter = value;
                OnPropertyChanged(nameof(UseManualHolidayLetter));
            }
        }

        public string WeekendWorkColumnName
        {
            get => _weekendWorkColumnName;
            set
            {
                if (_weekendWorkColumnName == value) return;
                _weekendWorkColumnName = value;
                OnPropertyChanged(nameof(WeekendWorkColumnName));
            }
        }

        public string WeekendWorkColumnLetter
        {
            get => _weekendWorkColumnLetter;
            set
            {
                if (_weekendWorkColumnLetter == value) return;
                _weekendWorkColumnLetter = value;
                OnPropertyChanged(nameof(WeekendWorkColumnLetter));
            }
        }

        public bool UseManualWeekendLetter
        {
            get => _useManualWeekendLetter;
            set
            {
                if (_useManualWeekendLetter == value) return;
                _useManualWeekendLetter = value;
                OnPropertyChanged(nameof(UseManualWeekendLetter));
            }
        }

        public SpecialOvertimeSettings()
        {
            _earnedRestDayColumnName = "B02 %100 Fazla Mesai";
            _earnedRestDayColumnLetter = "Z";
            _holidayWorkColumnName = "B03 %150 Fazla Mesai";
            _holidayWorkColumnLetter = "AA";
            _weekendWorkColumnName = "B01 %50 Fazla Mesai";
            _weekendWorkColumnLetter = "Y";
            _useManualEarnedRestLetter = false;
            _useManualHolidayLetter = false;
            _useManualWeekendLetter = false;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }


    /// <summary>
    /// Yatay şablonlarda vardiya bilgisi olmadan çalışma ayarları
    /// </summary>
    public class HorizontalTemplateSettings
    {
        /// <summary>
        /// Vardiya bilgisi olmadan çalışma modunu etkinleştir
        /// Yatay şablonlarda vardiya bilgisi yoksa, üst üste olan günlere göre vardiya gözetmeksizin hakediş kuralını işlet
        /// </summary>
        public bool ApplyRulesWithoutShift { get; set; } = false;

        /// <summary>
        /// Üst üste günler için minimum çalışma saati (vardiya gözetmeksizin)
        /// </summary>
        public double MinConsecutiveDaysHours { get; set; } = 0;

        /// <summary>
        /// Yatay şablonlar için kullanılacak vardiya grubu adı
        /// Boş ise ilk vardiya grubu kullanılır
        /// </summary>
        public string SelectedShiftRuleGroupName { get; set; } = string.Empty;

        /// <summary>
        /// Yatay şablonlar için üst üste gün sayısı override değeri
        /// 0 ise seçili vardiya grubunun ConsecutiveDaysForVacation değeri kullanılır
        /// </summary>
        public int OverrideConsecutiveDaysForVacation { get; set; } = 0;

        /// <summary>
        /// Yatay şablonlarda tatillerin zaten şablonda işaretli olup olmadığı
        /// True ise, tatiller şablonda geliyor ve tatil hesaplaması yapılmaz
        /// </summary>
        public bool HasHolidaysInTemplate { get; set; } = false;

        /// <summary>
        /// Resmi tatil günlerinde çalışma göstergeleri (RT hakedişi VERİLMEYECEK)
        /// Virgülle ayrılmış değerler: "RT,R,T"
        /// Bu değerler görülürse o günde çalışılmış demektir ve RT hakedişi verilmez
        /// </summary>
        public string OfficialHolidayWorkIndicators { get; set; } = "RT,R,T";

        /// <summary>
        /// Resmi tatil günlerinde tatil göstergeleri (RT hakedişi VERİLECEK)
        /// Virgülle ayrılmış değerler: "X,7.5"
        /// Bu değerler görülürse o günde tatil hak edilmiş demektir ve RT hakedişi verilir
        /// </summary>
        public string OfficialHolidayRestIndicators { get; set; } = "X,7.5";
    }

    /// <summary>
    /// Koşullu kazanç kuralı - Belirli koşullara göre ERP şablonunda ek hakediş sütunlarına değer yazma
    /// </summary>
    public class ConditionalEarningsRule
    {
        /// <summary>
        /// Koşul tipi: DevamsızlıkVar, EksikGunVar, Koşulsuz, vb.
        /// </summary>
        public string ConditionType { get; set; } = string.Empty;

        /// <summary>
        /// Koşul operatörü: >, >=, <, <=, ==, != (Koşulsuz ise kullanılmaz)
        /// </summary>
        public string ConditionOperator { get; set; } = ">";

        /// <summary>
        /// Koşul değeri (örneğin: 0, 1, 5) (Koşulsuz ise kullanılmaz)
        /// ConditionValueSource "Sabit" ise bu değer kullanılır, aksi halde dinamik değer kullanılır
        /// </summary>
        public double ConditionValue { get; set; } = 0;

        /// <summary>
        /// Koşul değeri kaynağı: "Sabit" veya dinamik seçeneklerden biri
        /// Örnekler: "Sabit", "Devamsızlık Günü Kadar", "Çalışılan Gün Kadar", "Eksik Saat Kadar"
        /// </summary>
        public string ConditionValueSource { get; set; } = "Sabit";

        /// <summary>
        /// Sütun türü: "Yan Ödeme" veya "Kesintiler"
        /// </summary>
        public string ColumnType { get; set; } = "Yan Ödeme";

        /// <summary>
        /// Hedef sütun adı (örneğin: "C01 İkramiye", "C02 Yakacak Yardımı")
        /// </summary>
        public string TargetColumnName { get; set; } = string.Empty;

        /// <summary>
        /// Hedef sütun harfi (örneğin: "AH", "AI", "AJ")
        /// </summary>
        public string TargetColumnLetter { get; set; } = string.Empty;

        /// <summary>
        /// Yazılacak değer (sabit değer veya hesaplanan değer)
        /// </summary>
        public double EarningsValue { get; set; } = 0;

        /// <summary>
        /// Değer tipi: Sabit, Hesaplanan (örneğin: devamsızlık günü kadar)
        /// </summary>
        public string ValueType { get; set; } = "Sabit";

        /// <summary>
        /// Kuralın geçerli olacağı başlangıç tarihi (null ise sınırsız)
        /// </summary>
        public DateTime? StartDate { get; set; } = null;

        /// <summary>
        /// Kuralın geçerli olacağı bitiş tarihi (null ise sınırsız)
        /// </summary>
        public DateTime? EndDate { get; set; } = null;

        /// <summary>
        /// Kuralın aktif olup olmadığı
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Kural açıklaması
        /// </summary>
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// Manuel olarak eklenen sütun (Excel'den veya elle girilen)
    /// </summary>
    public class ManualColumn
    {
        /// <summary>
        /// Sütun adı (Excel'deki başlık)
        /// </summary>
        public string ColumnName { get; set; } = string.Empty;

        /// <summary>
        /// Excel sütun harfi (örn: "CI", "CJ", "DC")
        /// </summary>
        public string ColumnLetter { get; set; } = string.Empty;

        /// <summary>
        /// Sütun türü: "Yan Ödeme" veya "Kesintiler"
        /// </summary>
        public string ColumnType { get; set; } = "Yan Ödeme";
    }

    /// <summary>
    /// Koşullu kazanç ayarları - ERP şablonunda koşullara göre ek hakedişler
    /// </summary>
    public class ConditionalEarningsSettings
    {
        /// <summary>
        /// Koşullu kazanç kuralları listesi
        /// </summary>
        public List<ConditionalEarningsRule> Rules { get; set; } = new List<ConditionalEarningsRule>();

        /// <summary>
        /// Manuel olarak eklenen sütunlar (Excel'den veya elle girilen)
        /// </summary>
        public List<ManualColumn> ManualColumns { get; set; } = new List<ManualColumn>();
    }

    /// <summary>
    /// Firma bazlı yapılandırma
    /// </summary>
    public class CompanyConfig
    {
        public string CompanyCode { get; set; } // Firma kodu (örn: "GULERYUZ", "DIGER")
        public string CompanyName { get; set; } // Firma adı
        public string LogoPath { get; set; } // Firma logosu dosya yolu
        public string ErpTemplatePath { get; set; } // Firma özel ERP şablon yolu
        public List<ShiftRuleConfig> ShiftRuleConfigs { get; set; } // Firma özel vardiya kuralları
        public int MonthDays { get; set; } // Firma özel ay gün sayısı
        public int PayrollYear { get; set; } // İşlenecek dönemin yılı
        public int PayrollMonth { get; set; } // İşlenecek dönemin ayı (1-12)
        public string Description { get; set; } // Açıklama
        public SpecialOvertimeSettings SpecialOvertimeSettings { get; set; } // Hak edilen tatil / resmi tatil FM ayarları
        public Dictionary<int, List<string>> ActiveOfficialHolidayDates { get; set; } // Yıla göre aktif resmi tatiller (yyyy-MM-dd)
        
        /// <summary>
        /// Yatay şablonlarda vardiya bilgisi olmadan çalışma ayarları
        /// </summary>
        public HorizontalTemplateSettings HorizontalTemplateSettings { get; set; } = new HorizontalTemplateSettings();

        /// <summary>
        /// Koşullu kazanç ayarları - Belirli koşullara göre ERP şablonunda ek hakedişler
        /// </summary>
        public ConditionalEarningsSettings ConditionalEarningsSettings { get; set; } = new ConditionalEarningsSettings();

        public CompanyConfig()
        {
            ShiftRuleConfigs = new List<ShiftRuleConfig>();
            var now = DateTime.Now;
            PayrollYear = now.Year;
            PayrollMonth = now.Month;
            MonthDays = DateTime.DaysInMonth(PayrollYear, PayrollMonth);
            SpecialOvertimeSettings = new SpecialOvertimeSettings();
            ActiveOfficialHolidayDates = new Dictionary<int, List<string>>();
            HorizontalTemplateSettings = new HorizontalTemplateSettings();
            ConditionalEarningsSettings = new ConditionalEarningsSettings();
        }
    }

    public class PDKSConfig
    {
        public List<CompanyConfig> CompanyConfigs { get; set; } // Firma bazlı yapılandırmalar
        public string SelectedCompanyCode { get; set; } // Seçili firma kodu
        public bool UseDefaultSettings { get; set; } // Öntanımlı ayarları kullan
        public string ConfigName { get; set; } // Konfigürasyon adı
        public DateTime LastModified { get; set; } // Son düzenleme tarihi

        public PDKSConfig()
        {
            CompanyConfigs = new List<CompanyConfig>();
            UseDefaultSettings = true;
            ConfigName = "Firma Bazlı Ayarlar";
            LastModified = DateTime.Now;
            // InitializeDefaultSettings(); // Bu satırı kaldırıyorum
        }

        // JSON deserialization sonrası null değerleri düzeltmek için
        public void EnsureValidState()
        {
            if (CompanyConfigs == null)
                CompanyConfigs = new List<CompanyConfig>();

            if (string.IsNullOrEmpty(SelectedCompanyCode) && CompanyConfigs.Count > 0)
                SelectedCompanyCode = CompanyConfigs[0].CompanyCode;

            if (string.IsNullOrEmpty(ConfigName))
                ConfigName = "Firma Bazlı Ayarlar";

            if (LastModified == default(DateTime))
                LastModified = DateTime.Now;

            foreach (var company in CompanyConfigs)
            {
                if (company.PayrollYear <= 0)
                    company.PayrollYear = DateTime.Now.Year;

                if (company.PayrollMonth <= 0 || company.PayrollMonth > 12)
                    company.PayrollMonth = DateTime.Now.Month;

                company.MonthDays = DateTime.DaysInMonth(company.PayrollYear, company.PayrollMonth);
                if (company.SpecialOvertimeSettings == null)
                    company.SpecialOvertimeSettings = new SpecialOvertimeSettings();
                if (company.ActiveOfficialHolidayDates == null)
                    company.ActiveOfficialHolidayDates = new Dictionary<int, List<string>>();
                if (company.HorizontalTemplateSettings == null)
                    company.HorizontalTemplateSettings = new HorizontalTemplateSettings();
                if (company.ConditionalEarningsSettings == null)
                    company.ConditionalEarningsSettings = new ConditionalEarningsSettings();

                if (company.ShiftRuleConfigs != null)
                {
                    foreach (var shiftRule in company.ShiftRuleConfigs)
                    {
                        if (shiftRule.ShiftPatterns == null)
                            shiftRule.ShiftPatterns = new List<string>();
                        if (shiftRule.OvertimeRules == null)
                            shiftRule.OvertimeRules = new List<OvertimeRule>();
                        if (shiftRule.ShiftPatternMappings == null)
                            shiftRule.ShiftPatternMappings = new List<string>();
                    }
                }
            }
        }

        // InitializeDefaultSettings metodunu tamamen kaldırıyorum.
    }

    public class PDKSConfigService
    {
        private readonly string configFilePath = "pdks-config.json";

        public PDKSConfig LoadConfig()
        {
            try
            {
                if (File.Exists(configFilePath))
                {
                    string json = File.ReadAllText(configFilePath);
                    var config = System.Text.Json.JsonSerializer.Deserialize<PDKSConfig>(json);
                    if (config != null)
                    {
                        config.EnsureValidState();
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Konfigürasyon yüklenirken hata: {ex.Message}");
            }

            // Hata durumunda veya dosya yoksa sıfırdan yeni bir config oluştur ve varsayılan firmaları ekle
            var newConfig = new PDKSConfig();
            newConfig.CompanyConfigs.AddRange(new List<CompanyConfig>
            {
                new CompanyConfig
                {
                    CompanyCode = "GULERYUZ",
                    CompanyName = "Güleryüz Grup",
                    ErpTemplatePath = "erpsablon.xlsx",
                    PayrollYear = DateTime.Now.Year,
                    PayrollMonth = DateTime.Now.Month,
                    MonthDays = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month),
                    Description = "Güleryüz Grup firması için PDKS kuralları",
                    ShiftRuleConfigs = new List<ShiftRuleConfig>
                    {
                        new ShiftRuleConfig
                        {
                            GroupName = "8/18 Vardiyaları",
                            ShiftPatterns = new List<string> { "8/18", "8/19" },
                            DefaultStartTime = TimeSpan.Parse("08:00"),
                            DefaultEndTime = TimeSpan.Parse("18:00"),
                            StandardHours = 7.5,
                            ShiftDuration = 9.0,
                            BreakHours = 1.0,
                            AssignNormalFM = true, // Varsayılan olarak işaretli
                            ConsecutiveDaysForVacation = 5,
                            VacationDays = 1,
                            OvertimeRules = new List<OvertimeRule>
                            {
                                new OvertimeRule
                                {
                                    DurationHours = 1.5,
                                    Rate = 1.0,
                                    Description = "İlk 1.5 saat B04 Fazla Mesai Normal",
                                    ColumnName = "B04 Fazla Mesai Normal"
                                },
                                new OvertimeRule
                                {
                                    DurationHours = null,
                                    Rate = 1.5,
                                    Description = "Kalan tüm saatler B01 %50 Fazla Mesai",
                                    ColumnName = "B01 %50 Fazla Mesai",
                                    IsCatchAll = true
                                }
                            },
                            ShiftPatternMappings = new List<string>()
                        },
                        new ShiftRuleConfig
                        {
                            GroupName = "Diğer Vardiyalar",
                            ShiftPatterns = new List<string> { "*" },
                            DefaultStartTime = TimeSpan.Parse("08:00"),
                            DefaultEndTime = TimeSpan.Parse("17:00"),
                            StandardHours = 7.5,
                            ShiftDuration = 9.0,
                            BreakHours = 1.0,
                            AssignNormalFM = false, // Varsayılan olarak işaretsiz
                            ConsecutiveDaysForVacation = 6,
                            VacationDays = 1,
                            OvertimeRules = new List<OvertimeRule>
                            {
                                new OvertimeRule
                                {
                                    DurationHours = null,
                                    Rate = 1.0,
                                    Description = "Tüm fazla çalışma B04 Fazla Mesai Normal",
                                    ColumnName = "B04 Fazla Mesai Normal",
                                    IsCatchAll = true
                                }
                            },
                            ShiftPatternMappings = new List<string>()
                        }
                    }
                },
                new CompanyConfig
                {
                    CompanyCode = "DIGER",
                    CompanyName = "Diğer Firmalar",
                    ErpTemplatePath = "erpsablon.xlsx",
                    PayrollYear = DateTime.Now.Year,
                    PayrollMonth = DateTime.Now.Month,
                    MonthDays = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month),
                    Description = "Diğer firmalar için genel PDKS kuralları",
                    ShiftRuleConfigs = new List<ShiftRuleConfig>
                    {
                        new ShiftRuleConfig
                        {
                            GroupName = "Standart Vardiyalar",
                            ShiftPatterns = new List<string> { "*" },
                            DefaultStartTime = TimeSpan.Parse("08:00"),
                            DefaultEndTime = TimeSpan.Parse("17:00"),
                            StandardHours = 8.0,
                            ShiftDuration = 9.0,
                            BreakHours = 1.0,
                            AssignNormalFM = true, // Varsayılan olarak işaretli
                            ConsecutiveDaysForVacation = 6,
                            VacationDays = 1,
                            OvertimeRules = new List<OvertimeRule>
                            {
                                new OvertimeRule
                                {
                                    DurationHours = null,
                                    Rate = 1.0,
                                    Description = "Tüm fazla çalışma B04 Fazla Mesai Normal",
                                    ColumnName = "B04 Fazla Mesai Normal",
                                    IsCatchAll = true
                                }
                            },
                            ShiftPatternMappings = new List<string>()
                        }
                    }
                }
            });
            // İlk firmayı seçili olarak ayarla
            if (newConfig.CompanyConfigs.Count > 0 && string.IsNullOrEmpty(newConfig.SelectedCompanyCode))
            {
                newConfig.SelectedCompanyCode = newConfig.CompanyConfigs[0].CompanyCode;
            }
            newConfig.EnsureValidState(); // Yeni config'in geçerli durumda olduğundan emin ol
            return newConfig;
        }

        public void SaveConfig(PDKSConfig config)
        {
            try
            {
                config.LastModified = DateTime.Now;
                string json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Konfigürasyon kaydedilirken hata: {ex.Message}");
            }
        }

        public List<string> GetAvailableConfigs()
        {
            // Farklı konfigürasyon dosyalarını listele
            var configFiles = Directory.GetFiles(".", "pdks-config*.json");
            return configFiles.Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToList();
        }
    }

    public class SettingsModal : Window
    {
        private PDKSConfig currentConfig;
        private PDKSConfigService configService;
        private TextBox companyFilterBox;
        private StackPanel companyListPanel;

        public SettingsModal(PDKSConfig config, PDKSConfigService service)
        {
            currentConfig = config;
            configService = service;

            Console.WriteLine($"[SettingsModal] Ayarlar modal'ı açılıyor");

            InitializeModal();
        }

        private void InitializeModal()
        {
            Title = "Firma Ayarları";
            Width = 800;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowState = WindowState.Maximized;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA));
            
            var poppinsFont = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");
            FontFamily = poppinsFont;

            // Global font stilleri ekle - tüm kontroller için varsayılan font
            var textBlockStyle = new Style(typeof(TextBlock));
            textBlockStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(TextBlock), textBlockStyle);
            
            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(Button), buttonStyle);
            
            var textBoxStyle = new Style(typeof(TextBox));
            textBoxStyle.Setters.Add(new Setter(TextBox.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(TextBox), textBoxStyle);
            
            var labelStyle = new Style(typeof(Label));
            labelStyle.Setters.Add(new Setter(Label.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(Label), labelStyle);
            
            var comboBoxStyle = new Style(typeof(ComboBox));
            comboBoxStyle.Setters.Add(new Setter(ComboBox.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(ComboBox), comboBoxStyle);
            
            var tabItemStyle = new Style(typeof(TabItem));
            tabItemStyle.Setters.Add(new Setter(TabItem.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(TabItem), tabItemStyle);

            // Ana TabControl
            var tabControl = new TabControl();
            tabControl.SelectionChanged += TabControl_SelectionChanged;

            // Ayarlar Tab
            var settingsTab = new TabItem();
            settingsTab.Header = "Firmalar ve Ayarları";
            settingsTab.FontSize = 14;
            settingsTab.FontFamily = poppinsFont;
            settingsTab.Content = CreateSettingsTab();

            tabControl.Items.Add(settingsTab);

            Content = tabControl;
        }

        private void TabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            // Artık sadece ayarlar tab'ı var, loglar ayrı modal'da
        }

        private UIElement CreateSettingsTab()
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Başlık
            var titleBlock = new TextBlock
            {
                Text = "Firma Bazlı PDKS Ayarları",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Bold.ttf#Poppins"),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                Margin = new Thickness(20, 20, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(titleBlock, 0);

            // Ayarlar İçeriği
            var settingsContent = CreateCompanySettingsContent();
            Grid.SetRow(settingsContent, 1);

            // Butonlar
            var buttonPanel = CreateSettingsButtons();
            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(titleBlock);
            grid.Children.Add(settingsContent);
            grid.Children.Add(buttonPanel);

            return grid;
        }

        private ControlTemplate CreateRoundedButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            borderFactory.AppendChild(contentPresenter);
            template.VisualTree = borderFactory;

            return template;
        }

        private Button CreateIconButton(
            string text,
            string iconGlyph,
            Color backgroundColor,
            Color hoverColor,
            Thickness margin,
            double width = double.NaN,
            double height = 40,
            bool includeText = true)
        {
            var defaultBrush = new SolidColorBrush(backgroundColor);
            var hoverBrush = new SolidColorBrush(hoverColor);

            var button = new Button
            {
                Height = height,
                Margin = margin,
                Background = defaultBrush,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = includeText ? new Thickness(16, 8, 16, 8) : new Thickness(10, 8, 10, 8)
            };

            if (!double.IsNaN(width))
            {
                button.Width = width;
            }

            button.Template = CreateRoundedButtonTemplate();

            var contentGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (includeText)
            {
                contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }

            var iconText = new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Margin = includeText ? new Thickness(0, 0, 8, 0) : new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brushes.White
            };
            contentGrid.Children.Add(iconText);

            if (includeText)
            {
                var labelText = new TextBlock
                {
                    Text = text,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    FontSize = 14
                };
                Grid.SetColumn(labelText, 1);
                contentGrid.Children.Add(labelText);
            }

            button.Content = contentGrid;

            button.MouseEnter += (s, e) => button.Background = hoverBrush;
            button.MouseLeave += (s, e) => button.Background = defaultBrush;

            return button;
        }

        private UIElement CreateCompanySettingsContent()
        {
            var scrollViewer = new ScrollViewer();
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Üst kısım (Yeni Firma Ekle butonu)
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Firma listesi

            // Üst kısım - filtre + yeni firma butonu
            var topPanel = new Grid
            {
                Margin = new Thickness(20, 20, 20, 15)
            };
            topPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            if (companyFilterBox != null)
            {
                companyFilterBox.TextChanged -= CompanyFilterBox_TextChanged;
            }

            companyFilterBox = new TextBox
            {
                Height = 38,
                Margin = new Thickness(0, 0, 12, 0),
                Padding = new Thickness(12, 8, 12, 8),
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0xD1, 0xE0)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontSize = 14,
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                ToolTip = "Firma adı veya koduna göre filtrele"
            };
            companyFilterBox.TextChanged += CompanyFilterBox_TextChanged;
            Grid.SetColumn(companyFilterBox, 0);
            topPanel.Children.Add(companyFilterBox);

            var btnAddCompany = CreateIconButton(
                "Yeni Firma Ekle",
                "\uE710",
                Color.FromRgb(0x4C, 0xAF, 0x50),
                Color.FromRgb(0x45, 0xA0, 0x49),
                new Thickness(0),
                width: 180);
            btnAddCompany.FontWeight = FontWeights.SemiBold;
            btnAddCompany.FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins");
            btnAddCompany.Click += (s, e) =>
            {
                var companyModal = new CompanyManagementModal(currentConfig);
                if (companyModal.ShowDialog() == true)
                {
                    RefreshCompanyList();

                    var configService = new PDKSConfigService();
                    if (Application.Current.MainWindow is PDKSWizardWindow parentWindow)
                    {
                        parentWindow.ReloadConfig();
                    }
                }
            };
            Grid.SetColumn(btnAddCompany, 1);
            topPanel.Children.Add(btnAddCompany);
            Grid.SetRow(topPanel, 0);

            // Firma listesi - tablo görünümü için Grid
            var tableContainer = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(20, 0, 20, 20)
            };
            Grid.SetRow(tableContainer, 1);

            companyListPanel = new StackPanel
            {
                Orientation = Orientation.Vertical
            };
            tableContainer.Child = companyListPanel;

            mainGrid.Children.Add(topPanel);
            mainGrid.Children.Add(tableContainer);

            scrollViewer.Content = mainGrid;
            ApplyCompanyFilter();
            return scrollViewer;
        }


        private UIElement CreateCompanyTableRow(CompanyConfig company)
        {
            var rowBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 14, 16, 14),
                Cursor = Cursors.Arrow
            };

            // Hover efekti
            rowBorder.MouseEnter += (s, e) =>
            {
                rowBorder.Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC));
            };
            rowBorder.MouseLeave += (s, e) =>
            {
                rowBorder.Background = Brushes.White;
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Logo
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Firma Adı
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Firma Kodu
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Vardiya Kuralları
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // İşlemler

            // Logo (td)
            var logoCell = new Border
            {
                Width = 50,
                Height = 50,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            if (!string.IsNullOrWhiteSpace(company.LogoPath))
            {
                try
                {
                    // Göreceli veya mutlak yol kontrolü
                    string fullLogoPath = company.LogoPath;
                    if (!System.IO.Path.IsPathRooted(fullLogoPath))
                    {
                        // Göreceli yol ise, kök dizine ekle
                        string appBaseDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
                        fullLogoPath = System.IO.Path.Combine(appBaseDirectory, company.LogoPath);
                    }

                    if (File.Exists(fullLogoPath))
                    {
                        var logoImage = new Image
                        {
                            Stretch = Stretch.Uniform,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            MaxWidth = 46,
                            MaxHeight = 46
                        };
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(fullLogoPath, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        logoImage.Source = bitmap;
                        logoCell.Child = logoImage;
                    }
                }
                catch
                {
                    // Logo yüklenemezse boş bırak
                    logoCell.Child = null;
                }
            }

            Grid.SetColumn(logoCell, 0);
            rowGrid.Children.Add(logoCell);

            // Firma Adı (td)
            var companyNameCell = new TextBlock
            {
                Text = company.CompanyName ?? "",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-SemiBold.ttf#Poppins"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(companyNameCell, 1);
            rowGrid.Children.Add(companyNameCell);

            // Firma Kodu (td)
            var companyCodeCell = new TextBlock
            {
                Text = company.CompanyCode ?? "",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(companyCodeCell, 2);
            rowGrid.Children.Add(companyCodeCell);

            // Vardiya Kuralları (td) - tıklanabilir
            var shiftRulesCell = new TextBlock
            {
                Text = company.ShiftRuleConfigs?.Count > 0 
                    ? $"{company.ShiftRuleConfigs.Count} grup" 
                    : "0 grup",
                FontSize = 13,
                Foreground = company.ShiftRuleConfigs?.Count > 0 
                    ? new SolidColorBrush(Color.FromRgb(0x22, 0x6E, 0xD0))
                    : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins"),
                VerticalAlignment = VerticalAlignment.Center,
                TextDecorations = company.ShiftRuleConfigs?.Count > 0 ? TextDecorations.Underline : null,
                Cursor = company.ShiftRuleConfigs?.Count > 0 ? Cursors.Hand : Cursors.Arrow,
                FontWeight = FontWeights.Medium
            };
            shiftRulesCell.ToolTip = CreateShiftRulesTooltipText(company);
            shiftRulesCell.MouseDown += (s, e) =>
            {
                if (company.ShiftRuleConfigs?.Count > 0)
                {
                    OpenShiftRulesModal(company);
                }
                e.Handled = true;
            };
            Grid.SetColumn(shiftRulesCell, 3);
            rowGrid.Children.Add(shiftRulesCell);

            // İşlemler (td) - Düzenle ve Sil butonları
            var actionsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Düzenle butonu
            var btnEdit = CreateIconButton(
                string.Empty,
                "\uE70F",
                Color.FromRgb(0x3B, 0x82, 0xF6),
                Color.FromRgb(0x25, 0x6E, 0xEB),
                new Thickness(0, 0, 8, 0),
                width: 36,
                height: 36,
                includeText: false);
            btnEdit.ToolTip = "Firmayı Düzenle";
            btnEdit.Click += (s, e) => EditCompany(company);
            actionsPanel.Children.Add(btnEdit);

            // Sil butonu
            var btnDelete = CreateIconButton(
                string.Empty,
                "\uE74D",
                Color.FromRgb(0xEF, 0x44, 0x44),
                Color.FromRgb(0xDC, 0x26, 0x26),
                new Thickness(0),
                width: 36,
                height: 36,
                includeText: false);
            btnDelete.ToolTip = "Firmayı Sil";
            btnDelete.Click += (s, e) => DeleteCompany(company);
            actionsPanel.Children.Add(btnDelete);

            Grid.SetColumn(actionsPanel, 4);
            rowGrid.Children.Add(actionsPanel);

            rowBorder.Child = rowGrid;
            return rowBorder;
        }

        // Eski metod - geriye uyumluluk için (başka yerlerde kullanılıyor olabilir)
        private UIElement CreateCompanyItem(CompanyConfig company)
        {
            return CreateCompanyTableRow(company);
        }

        private void CompanyFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyCompanyFilter();
        }

        private void ApplyCompanyFilter()
        {
            if (companyListPanel == null)
            {
                return;
            }

            companyListPanel.Children.Clear();

            // Tablo başlığı (th - table header)
            var tableHeader = CreateTableHeader();
            companyListPanel.Children.Add(tableHeader);

            if (currentConfig?.CompanyConfigs == null || currentConfig.CompanyConfigs.Count == 0)
            {
                var emptyRow = CreateEmptyTableRow("Tanımlı firma bulunmuyor.");
                companyListPanel.Children.Add(emptyRow);
                return;
            }

            var filterText = companyFilterBox?.Text?.Trim() ?? string.Empty;
            var normalizedFilter = filterText.ToLowerInvariant();

            var filteredCompanies = currentConfig.CompanyConfigs
                .Where(company =>
                    string.IsNullOrEmpty(normalizedFilter) ||
                    (!string.IsNullOrEmpty(company.CompanyName) &&
                     company.CompanyName.ToLowerInvariant().Contains(normalizedFilter)) ||
                    (!string.IsNullOrEmpty(company.CompanyCode) &&
                     company.CompanyCode.ToLowerInvariant().Contains(normalizedFilter)))
                .OrderBy(company => company.CompanyName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(company => company.CompanyCode ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            if (filteredCompanies.Count == 0)
            {
                var emptyRow = CreateEmptyTableRow("Filtreye uyan firma bulunamadı.");
                companyListPanel.Children.Add(emptyRow);
                return;
            }

            foreach (var company in filteredCompanies)
            {
                var companyItem = CreateCompanyTableRow(company);
                companyListPanel.Children.Add(companyItem);
            }
        }

        private UIElement CreateTableHeader()
        {
            var headerBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFB)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(0, 0, 0, 2),
                Padding = new Thickness(16, 14, 16, 14)
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Logo
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) }); // Firma Adı
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Firma Kodu
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Vardiya Kuralları
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // İşlemler

            var headerStyle = new Style(typeof(TextBlock));
            headerStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 13.0));
            headerStyle.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            headerStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37))));
            headerStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("pack://application:,,,/Fonts/Poppins-SemiBold.ttf#Poppins")));

            // Logo başlığı (boş)
            var logoHeader = new TextBlock { Text = "", Style = headerStyle, VerticalAlignment = VerticalAlignment.Center, Width = 62 };
            Grid.SetColumn(logoHeader, 0);
            headerGrid.Children.Add(logoHeader);

            var firmNameHeader = new TextBlock { Text = "Firma Adı", Style = headerStyle, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(firmNameHeader, 1);
            headerGrid.Children.Add(firmNameHeader);

            var firmCodeHeader = new TextBlock { Text = "Firma Kodu", Style = headerStyle, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(firmCodeHeader, 2);
            headerGrid.Children.Add(firmCodeHeader);

            var shiftRulesHeader = new TextBlock { Text = "Vardiya Kuralları", Style = headerStyle, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(shiftRulesHeader, 3);
            headerGrid.Children.Add(shiftRulesHeader);

            var actionsHeader = new TextBlock { Text = "İşlemler", Style = headerStyle, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(actionsHeader, 4);
            headerGrid.Children.Add(actionsHeader);

            headerBorder.Child = headerGrid;
            return headerBorder;
        }

        private UIElement CreateEmptyTableRow(string message)
        {
            var rowBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 20, 16, 20)
            };

            var emptyText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)),
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            rowBorder.Child = emptyText;
            return rowBorder;
        }

        private void EditCompany(CompanyConfig company)
        {
            Console.WriteLine($"[EditCompany] Firma düzenleme modal'ı açılıyor: {company.CompanyName}");
            var companyModal = new CompanyManagementModal(currentConfig, company);
            var result = companyModal.ShowDialog();
            Console.WriteLine($"[EditCompany] Modal kapatıldı, result: {result}");

            if (result == true)
            {
                Console.WriteLine($"[EditCompany] Firma güncellendi, listeyi yenileme ve parent window güncelleme işlemi başlatılıyor");
                // Firma güncellendi, listeyi yenile
                RefreshCompanyList();

                // Ana pencerede config'i yeniden yükle (UI güncellemesi için)
                var parentWindow = Application.Current.MainWindow as PDKSWizardWindow;
                if (parentWindow != null)
                {
                    Console.WriteLine($"[EditCompany] Parent window bulundu, ReloadConfig çağrılıyor");
                    parentWindow.ReloadConfig();
                }
                else
                {
                    Console.WriteLine($"[EditCompany] Parent window bulunamadı!");
                }
            }
            else
            {
                Console.WriteLine($"[EditCompany] Modal false ile kapatıldı, işlem iptal edildi");
            }
        }

        private void DeleteCompany(CompanyConfig company)
        {
            Console.WriteLine($"[DeleteCompany] Firma silme işlemi başlatılıyor: {company.CompanyName}");
            var result = MessageBox.Show(
                $"'{company.CompanyName}' firmasını silmek istediğinizden emin misiniz?\n\nBu işlem geri alınamaz ve firmanın tüm vardiya kuralları silinecektir.",
                "Firma Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Console.WriteLine($"[DeleteCompany] Kullanıcı onay verdi, firma siliniyor");
                currentConfig.CompanyConfigs.Remove(company);
                configService.SaveConfig(currentConfig);
                Console.WriteLine($"[DeleteCompany] Config kaydedildi, liste yenileniyor");
                RefreshCompanyList();

                // Ana pencerede config'i yeniden yükle (UI güncellemesi için)
                var parentWindow = Application.Current.MainWindow as PDKSWizardWindow;
                if (parentWindow != null)
                {
                    Console.WriteLine($"[DeleteCompany] Parent window bulundu, ReloadConfig çağrılıyor");
                    parentWindow.ReloadConfig();
                }
                else
                {
                    Console.WriteLine($"[DeleteCompany] Parent window bulunamadı!");
                }

                Console.WriteLine($"[Settings] Firma silindi: {company.CompanyName} ({company.CompanyCode})");
            }
            else
            {
                Console.WriteLine($"[DeleteCompany] Kullanıcı vazgeçti");
            }
        }

        private void RefreshCompanyList()
        {
            // Firma listesi değişikliklerini yansıtmak için config'i yeniden yükle ve UI'ı güncelle
            try
            {
                var configService = new PDKSConfigService();
                var updatedConfig = configService.LoadConfig();

                // Ana config'i güncelle
                currentConfig.CompanyConfigs.Clear();
                currentConfig.CompanyConfigs.AddRange(updatedConfig.CompanyConfigs);
                currentConfig.LastModified = updatedConfig.LastModified;

                Console.WriteLine($"[RefreshCompanyList] Firma listesi yenilendi - {currentConfig.CompanyConfigs.Count} firma");

                // UI'ı güncellemek için settings modal'ını bul ve content'i yeniden oluştur
                RefreshSettingsModalUI();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RefreshCompanyList] Config yeniden yüklenirken hata: {ex.Message}");
            }
        }

        private void RefreshSettingsModalUI()
        {
            try
            {
                // Açık olan settings modal'ını bul
                var openSettingsModal = Application.Current.Windows
                    .OfType<SettingsModal>()
                    .FirstOrDefault();

                if (openSettingsModal != null)
                {
                    Console.WriteLine($"[RefreshSettingsModalUI] Settings modal bulundu, UI yenileniyor");
                    openSettingsModal.RefreshCompanySettingsContent();
                }
                else
                {
                    Console.WriteLine($"[RefreshSettingsModalUI] Açık settings modal bulunamadı");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RefreshSettingsModalUI] Hata: {ex.Message}");
            }
        }

        private T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T t)
                    return t;

                var result = FindVisualChild<T>(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private string CreateShiftRulesTooltipText(CompanyConfig company)
        {
            if (company.ShiftRuleConfigs == null || company.ShiftRuleConfigs.Count == 0)
            {
                return "Vardiya kuralı yok";
            }

            var tooltipText = $"Vardiya Kuralları ({company.ShiftRuleConfigs.Count}):\n";
            foreach (var rule in company.ShiftRuleConfigs)
            {
                tooltipText += $"\n• {rule.GroupName}: {rule.ShiftPatterns.Count} desen";
            }
            tooltipText += "\n\nTıklayınca ayarları açılır";

            return tooltipText;
        }

        private void OpenShiftRulesModal(CompanyConfig company)
        {
            try
            {
                // ShiftRuleGroupModal'ı aç - mevcut firmayı düzenleme modunda
                var shiftRuleModal = new ShiftRuleGroupModal(company);
                if (shiftRuleModal.ShowDialog() == true)
                {
                    // Kurallar güncellendi, listeyi yenile
                    RefreshCompanyList();
                    Console.WriteLine($"[Settings] {company.CompanyName} için vardiya kuralları güncellendi");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Vardiya kuralları modal'ı açılırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"[Settings] Vardiya kuralları modal açma hatası: {ex.Message}");
            }
        }

        private UIElement CreateSettingsButtons()
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var btnReset = CreateIconButton(
                "Öntanımlılara Dön",
                "\uE72C",
                Color.FromRgb(0xFB, 0x8C, 0x00),
                Color.FromRgb(0xF5, 0x7C, 0x00),
                new Thickness(0, 0, 10, 0),
                width: 190);

            var btnSave = CreateIconButton(
                "Kaydet",
                "\uE74E",
                Color.FromRgb(0x2E, 0x7D, 0x32),
                Color.FromRgb(0x1B, 0x5E, 0x20),
                new Thickness(0, 0, 10, 0),
                width: 130);

            var btnClose = CreateIconButton(
                "Kapat",
                "\uE8BB",
                Color.FromRgb(0xD3, 0x2F, 0x2F),
                Color.FromRgb(0xB7, 0x1C, 0x1C),
                new Thickness(0),
                width: 120);

            btnReset.Click += (s, e) =>
            {
                currentConfig.CompanyConfigs.Clear(); // Mevcut firmaları temizle
                // Varsayılan firmaları manuel olarak ekle
                currentConfig.CompanyConfigs.AddRange(new List<CompanyConfig>
                {
                    new CompanyConfig
                    {
                        CompanyCode = "GULERYUZ",
                        CompanyName = "Güleryüz Grup",
                        ErpTemplatePath = "erpsablon.xlsx",
                        PayrollYear = DateTime.Now.Year,
                        PayrollMonth = DateTime.Now.Month,
                        MonthDays = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month),
                        Description = "Güleryüz Grup firması için PDKS kuralları",
                        ShiftRuleConfigs = new List<ShiftRuleConfig>
                        {
                            new ShiftRuleConfig
                            {
                                GroupName = "8/18 Vardiyaları",
                                ShiftPatterns = new List<string> { "8/18", "8/19" },
                                DefaultStartTime = TimeSpan.Parse("08:00"),
                                DefaultEndTime = TimeSpan.Parse("18:00"),
                                StandardHours = 7.5,
                                ShiftDuration = 9.0,
                                BreakHours = 1.0,
                                AssignNormalFM = true, // Varsayılan olarak işaretli
                                ConsecutiveDaysForVacation = 5,
                                VacationDays = 1,
                                OvertimeRules = new List<OvertimeRule>
                                {
                                    new OvertimeRule
                                    {
                                        DurationHours = 1.5,
                                        Rate = 1.0,
                                        Description = "İlk 1.5 saat B04 Fazla Mesai Normal",
                                        ColumnName = "B04 Fazla Mesai Normal"
                                    },
                                    new OvertimeRule
                                    {
                                        DurationHours = null,
                                        Rate = 1.5,
                                        Description = "Kalan tüm saatler B01 %50 Fazla Mesai",
                                        ColumnName = "B01 %50 Fazla Mesai",
                                        IsCatchAll = true
                                    }
                                },
                                ShiftPatternMappings = new List<string>()
                            },
                            new ShiftRuleConfig
                            {
                                GroupName = "Diğer Vardiyalar",
                                ShiftPatterns = new List<string> { "*" },
                                DefaultStartTime = TimeSpan.Parse("08:00"),
                                DefaultEndTime = TimeSpan.Parse("17:00"),
                                StandardHours = 7.5,
                                ShiftDuration = 9.0,
                                BreakHours = 1.0,
                                AssignNormalFM = false, // Varsayılan olarak işaretsiz
                                ConsecutiveDaysForVacation = 6,
                                VacationDays = 1,
                                OvertimeRules = new List<OvertimeRule>
                                {
                                    new OvertimeRule
                                    {
                                        DurationHours = null,
                                        Rate = 1.0,
                                        Description = "Tüm fazla çalışma B04 Fazla Mesai Normal",
                                        ColumnName = "B04 Fazla Mesai Normal",
                                        IsCatchAll = true
                                    }
                                },
                                ShiftPatternMappings = new List<string>()
                            }
                        }
                    },
                    new CompanyConfig
                    {
                        CompanyCode = "DIGER",
                        CompanyName = "Diğer Firmalar",
                        ErpTemplatePath = "erpsablon.xlsx",
                        PayrollYear = DateTime.Now.Year,
                        PayrollMonth = DateTime.Now.Month,
                        MonthDays = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month),
                        Description = "Diğer firmalar için genel PDKS kuralları",
                        ShiftRuleConfigs = new List<ShiftRuleConfig>
                        {
                            new ShiftRuleConfig
                            {
                                GroupName = "Standart Vardiyalar",
                                ShiftPatterns = new List<string> { "*" },
                                DefaultStartTime = TimeSpan.Parse("08:00"),
                                DefaultEndTime = TimeSpan.Parse("17:00"),
                                StandardHours = 8.0,
                                ShiftDuration = 9.0,
                                BreakHours = 1.0,
                                AssignNormalFM = true, // Varsayılan olarak işaretli
                                ConsecutiveDaysForVacation = 6,
                                VacationDays = 1,
                                OvertimeRules = new List<OvertimeRule>
                                {
                                    new OvertimeRule
                                    {
                                        DurationHours = null,
                                        Rate = 1.0,
                                        Description = "Tüm fazla çalışma B04 Fazla Mesai Normal",
                                        ColumnName = "B04 Fazla Mesai Normal",
                                        IsCatchAll = true
                                    }
                                },
                                ShiftPatternMappings = new List<string>()
                            }
                        }
                    }
                });
                // İlk firmayı seçili olarak ayarla
                if (currentConfig.CompanyConfigs.Count > 0 && string.IsNullOrEmpty(currentConfig.SelectedCompanyCode))
                {
                    currentConfig.SelectedCompanyCode = currentConfig.CompanyConfigs[0].CompanyCode;
                }
                configService.SaveConfig(currentConfig); // Değişiklikleri kaydet
                RefreshSettingsUI(); // UI'ı yenile
            };
            btnSave.Click += (s, e) => { configService.SaveConfig(currentConfig); MessageBox.Show("Ayarlar kaydedildi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information); };
            btnClose.Click += (s, e) => Close();

            panel.Children.Add(btnReset);
            panel.Children.Add(btnSave);
            panel.Children.Add(btnClose);

            return panel;
        }


        private void RefreshSettingsUI()
        {
            // Bu metod ayarlar UI'ını yenilemek için kullanılabilir
            // Şimdilik basit bir yaklaşım
            MessageBox.Show("Ayarlar öntanımlı değerlere döndürüldü.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void RefreshCompanySettingsContent()
        {
            try
            {
                Console.WriteLine($"[SettingsModal] Firma ayarları content'i yenileniyor");

                // TabControl'ı bul
                var tabControl = Content as TabControl;
                if (tabControl == null || tabControl.Items.Count == 0)
                {
                    Console.WriteLine($"[SettingsModal] TabControl bulunamadı");
                    return;
                }

                // Settings tab'ını bul (ilk tab)
                var settingsTab = tabControl.Items[0] as TabItem;
                if (settingsTab == null)
                {
                    Console.WriteLine($"[SettingsModal] Settings tab bulunamadı");
                    return;
                }

                // Settings tab content'ini yeniden oluştur
                settingsTab.Content = CreateSettingsTab();
                Console.WriteLine($"[SettingsModal] Firma ayarları content'i başarıyla yenilendi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SettingsModal] Content yenileme hatası: {ex.Message}");
            }
        }

    }

    public class LogsModal : Window
    {
        private List<string> applicationLogs;
        private ListBox logListBox;

        private ControlTemplate CreateButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "Border";
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            borderFactory.SetValue(Border.PaddingProperty, new Thickness(10, 5, 10, 5));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.Name = "ContentPresenter";
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);

            borderFactory.AppendChild(contentPresenter);
            template.VisualTree = borderFactory;

            // Triggers for hover and disabled states
            var trigger1 = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            trigger1.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2))));

            var trigger2 = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            trigger2.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC))));
            trigger2.Setters.Add(new Setter(ContentPresenter.OpacityProperty, 0.5));

            template.Triggers.Add(trigger1);
            template.Triggers.Add(trigger2);

            return template;
        }

        public LogsModal(List<string> logs)
        {
            applicationLogs = logs ?? new List<string>();

            Console.WriteLine($"[LogsModal] Modal açılıyor, Log sayısı: {applicationLogs.Count}");

            // Eğer hiç log yoksa örnek log ekle
            if (applicationLogs.Count == 0)
            {
                applicationLogs.Add("[00:00:00] Sistem başlatıldı");
                applicationLogs.Add("[00:00:01] Log penceresi açıldı");
                applicationLogs.Add("[00:00:02] Log sistemi hazır");
            }

            InitializeModal();
        }

        private void InitializeModal()
        {
            Title = "Uygulama Logları";
            Width = 800;
            Height = 600;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Başlık
            var titleBlock = new TextBlock
            {
                Text = "Uygulama Logları",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(10, 10, 10, 15),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Grid.SetRow(titleBlock, 0);

            // Log Listesi
            logListBox = new ListBox
            {
                Margin = new Thickness(10, 0, 10, 10),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.LightGray,
                BorderThickness = new Thickness(1),
                ItemsSource = applicationLogs
            };

            Grid.SetRow(logListBox, 1);

            // Butonlar
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var btnClearLogs = new Button
            {
                Width = 140,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.Orange,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Template = CreateButtonTemplate()
            };
            var clearLogsGrid = new Grid();
            clearLogsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            clearLogsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var clearIcon = new TextBlock
            {
                Text = "\uE74D",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var clearText = new TextBlock
            {
                Text = "Logları Temizle",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(clearIcon, 0);
            Grid.SetColumn(clearText, 1);
            clearLogsGrid.Children.Add(clearIcon);
            clearLogsGrid.Children.Add(clearText);
            btnClearLogs.Content = clearLogsGrid;
            btnClearLogs.Click += BtnClearLogs_Click;

            var btnCopyLogs = new Button
            {
                Width = 140,
                Height = 35,
                Margin = new Thickness(0, 0, 10, 0),
                Background = System.Windows.Media.Brushes.Blue,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Template = CreateButtonTemplate()
            };
            var copyLogsGrid = new Grid();
            copyLogsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            copyLogsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var copyIcon = new TextBlock
            {
                Text = "\uE8C8",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var copyText = new TextBlock
            {
                Text = "Logları Kopyala",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(copyIcon, 0);
            Grid.SetColumn(copyText, 1);
            copyLogsGrid.Children.Add(copyIcon);
            copyLogsGrid.Children.Add(copyText);
            btnCopyLogs.Content = copyLogsGrid;
            btnCopyLogs.Click += BtnCopyLogs_Click;

            var btnCloseLogs = new Button
            {
                Width = 100,
                Height = 35,
                Background = System.Windows.Media.Brushes.Gray,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Template = CreateButtonTemplate()
            };
            var closeLogsGrid = new Grid();
            closeLogsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            closeLogsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var closeIcon = new TextBlock
            {
                Text = "\uE8BB",
                FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var closeText = new TextBlock
            {
                Text = "Kapat",
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(closeIcon, 0);
            Grid.SetColumn(closeText, 1);
            closeLogsGrid.Children.Add(closeIcon);
            closeLogsGrid.Children.Add(closeText);
            btnCloseLogs.Content = closeLogsGrid;
            btnCloseLogs.Click += BtnCloseLogs_Click;

            buttonPanel.Children.Add(btnClearLogs);
            buttonPanel.Children.Add(btnCopyLogs);
            buttonPanel.Children.Add(btnCloseLogs);

            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(titleBlock);
            grid.Children.Add(logListBox);
            grid.Children.Add(buttonPanel);

            Content = grid;

            // Modal açıldığında en son log'a scroll yap
            Loaded += (s, e) =>
            {
                if (logListBox.Items.Count > 0)
                {
                    logListBox.ScrollIntoView(logListBox.Items[logListBox.Items.Count - 1]);
                }
            };
        }

        private void BtnClearLogs_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Tüm logları temizlemek istediğinizden emin misiniz?", "Logları Temizle",
                              MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                applicationLogs.Clear();
                applicationLogs.Add($"[{DateTime.Now:HH:mm:ss}] Loglar temizlendi");
                logListBox.Items.Refresh();
                Console.WriteLine("[LogsModal] Loglar temizlendi");
            }
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logText = string.Join(Environment.NewLine, applicationLogs);
                System.Windows.Clipboard.SetText(logText);
                MessageBox.Show("Loglar panoya kopyalandı.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                Console.WriteLine("[LogsModal] Loglar panoya kopyalandı");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Loglar kopyalanırken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"[LogsModal] Log kopyalama hatası: {ex.Message}");
            }
        }

        private void BtnCloseLogs_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[LogsModal] Log modal'ı kapatıldı");
            this.Close();
        }
    }
}
