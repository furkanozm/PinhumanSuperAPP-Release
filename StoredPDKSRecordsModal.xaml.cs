using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OfficeOpenXml;

namespace WebScraper
{
    public partial class StoredPDKSRecordsModal : Window
    {
        public string? CompanyCode { get; private set; }
        public int Year { get; private set; }
        public int Month { get; private set; }
        public List<StoredPDKSRecord>? ImportedRecords { get; private set; }

        private readonly JsonSerializerOptions jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private PDKSConfigService configService = new PDKSConfigService();
        private PDKSConfig? currentConfig;

        public class CompanyItem
        {
            public string CompanyCode { get; set; } = string.Empty;
            public string CompanyName { get; set; } = string.Empty;
            public string DisplayName => $"{CompanyCode} - {CompanyName}";
        }

        public StoredPDKSRecordsModal()
        {
            InitializeComponent();
            
            // EPPlus lisans ayarı
            ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
            
            // ComboBox'ları doldur
            PopulateYearComboBox();
            PopulateMonthComboBox();

            // Firmaları yükle
            LoadCompanies();
            
            // Varsayılan değerleri ayarla
            var now = DateTime.Now;
            var prevMonth = now.AddMonths(-1);
            cmbYear.SelectedValue = prevMonth.Year;
            cmbMonth.SelectedValue = prevMonth.Month;
        }

        public StoredPDKSRecordsModal(string? companyCode, int year, int month) : this()
        {
            if (!string.IsNullOrWhiteSpace(companyCode))
            {
                cmbCompanyCode.SelectedValue = companyCode;
            }
            cmbYear.SelectedValue = year;
            cmbMonth.SelectedValue = month;
        }

        private void PopulateYearComboBox()
        {
            var years = Enumerable.Range(2025, 10).ToList();
            cmbYear.ItemsSource = years;
        }

        private void PopulateMonthComboBox()
        {
            var months = new Dictionary<string, int>
            {
                {"Ocak", 1}, {"Şubat", 2}, {"Mart", 3}, {"Nisan", 4},
                {"Mayıs", 5}, {"Haziran", 6}, {"Temmuz", 7}, {"Ağustos", 8},
                {"Eylül", 9}, {"Ekim", 10}, {"Kasım", 11}, {"Aralık", 12}
            };
            cmbMonth.ItemsSource = months;
            cmbMonth.DisplayMemberPath = "Key";
            cmbMonth.SelectedValuePath = "Value";
        }

        private void LoadCompanies()
        {
            try
            {
                currentConfig = configService.LoadConfig();
                if (currentConfig?.CompanyConfigs != null && currentConfig.CompanyConfigs.Count > 0)
                {
                    var companyItems = currentConfig.CompanyConfigs
                        .Select(c => new CompanyItem
                        {
                            CompanyCode = c.CompanyCode ?? string.Empty,
                            CompanyName = c.CompanyName ?? string.Empty
                        })
                        .OrderBy(c => c.CompanyCode)
                        .ToList();
                    
                    cmbCompanyCode.ItemsSource = companyItems;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Firmalar yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnImportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Title = "Excel Dosyasını Seçin",
                    Filter = "Excel Dosyaları (*.xlsx)|*.xlsx|Tüm Dosyalar (*.*)|*.*",
                    InitialDirectory = Directory.GetCurrentDirectory()
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    ImportFromExcel(openFileDialog.FileName);
                }
            }
            catch (Exception ex)
            {
                ShowError($"Excel import hatası: {ex.Message}");
            }
        }

        private void ImportFromExcel(string filePath)
        {
            try
            {
                HideMessages();

                // Python'u bul
                string? pythonCmd = FindPythonExecutable();
                if (pythonCmd == null)
                {
                    ShowError("Python bulunamadı. Lütfen Python'u yükleyin ve PATH'e ekleyin.");
                    return;
                }

                // Python script ile Excel'i oku - Günlük kayıt formatı
                string pythonScript = @"
import pandas as pd
import json
import sys
from datetime import datetime, time

file_path = r'" + filePath.Replace("\\", "\\\\") + @"'

try:
    df = pd.read_excel(file_path)
    
    # Kolon isimlerini normalize et (büyük/küçük harf duyarsız)
    df.columns = df.columns.str.strip()
    
    # Kolon mapping - farklı isimleri destekle
    code_col = None
    name_col = None
    date_col = None
    checkin_col = None
    checkout_col = None
    shift_col = None
    
    for col in df.columns:
        col_lower = col.lower()
        if code_col is None and ('sicil' in col_lower or 'personnelcode' in col_lower or 'kod' in col_lower):
            code_col = col
        elif name_col is None and ('ad' in col_lower or 'soyad' in col_lower or 'personnelname' in col_lower or 'isim' in col_lower or 'name' in col_lower):
            name_col = col
        elif date_col is None and ('tarih' in col_lower or 'date' in col_lower):
            date_col = col
        elif checkin_col is None and ('giriş' in col_lower or 'checkin' in col_lower or 'baslangic' in col_lower or 'başlangıç' in col_lower or 'giris' in col_lower):
            checkin_col = col
        elif checkout_col is None and ('çıkış' in col_lower or 'checkout' in col_lower or 'bitis' in col_lower or 'bitiş' in col_lower or 'cikis' in col_lower):
            checkout_col = col
        elif shift_col is None and ('vardiya' in col_lower or 'shift' in col_lower or 'vardiya tipi' in col_lower):
            shift_col = col
    
    # Eğer kolon isimleri bulunamadıysa, ilk 6 kolonu kullan
    if code_col is None and len(df.columns) > 0:
        code_col = df.columns[0]
    if name_col is None and len(df.columns) > 1:
        name_col = df.columns[1]
    if date_col is None and len(df.columns) > 2:
        date_col = df.columns[2]
    if checkin_col is None and len(df.columns) > 3:
        checkin_col = df.columns[3]
    if checkout_col is None and len(df.columns) > 4:
        checkout_col = df.columns[4]
    if shift_col is None and len(df.columns) > 5:
        shift_col = df.columns[5]
    
    records = []
    for idx, row in df.iterrows():
        try:
            # Sicil
            personnel_code = str(row[code_col]).strip() if pd.notna(row[code_col]) and code_col else ''
            
            # Ad Soyad
            personnel_name = str(row[name_col]).strip() if pd.notna(row[name_col]) and name_col else personnel_code
            
            # Tarih - TR formatına göre normalize et (dd.MM.yyyy, dd/MM/yyyy, yyyy-MM-dd, vb.)
            record_date = None
            if date_col and pd.notna(row[date_col]):
                date_val = row[date_col]
                try:
                    if isinstance(date_val, pd.Timestamp):
                        record_date = date_val.strftime('%Y-%m-%dT00:00:00')
                    elif isinstance(date_val, datetime):
                        record_date = date_val.strftime('%Y-%m-%dT00:00:00')
                    else:
                        date_str = str(date_val).strip()
                        # TR formatlarını dene: dd.MM.yyyy, dd/MM/yyyy, yyyy-MM-dd, vb.
                        # Önce pandas ile parse et (TR locale desteği ile)
                        try:
                            parsed = pd.to_datetime(date_str, dayfirst=True, format='mixed')
                            record_date = parsed.strftime('%Y-%m-%dT00:00:00')
                        except:
                            # Alternatif formatlar dene
                            for fmt in ['%d.%m.%Y', '%d/%m/%Y', '%Y-%m-%d', '%d.%m.%y', '%d/%m/%y']:
                                try:
                                    parsed = datetime.strptime(date_str, fmt)
                                    record_date = parsed.strftime('%Y-%m-%dT00:00:00')
                                    break
                                except:
                                    continue
                            if record_date is None:
                                # Son çare: pandas'ın otomatik parse'ını dene
                                parsed = pd.to_datetime(date_str)
                                record_date = parsed.strftime('%Y-%m-%dT00:00:00')
                except:
                    record_date = None
            
            if record_date is None:
                continue  # Tarih yoksa kaydı atla
            
            # Giriş Saati
            checkin_time = '08:00:00'  # Varsayılan
            if checkin_col and pd.notna(row[checkin_col]):
                try:
                    time_val = row[checkin_col]
                    if isinstance(time_val, pd.Timestamp):
                        checkin_time = time_val.strftime('%H:%M:%S')
                    elif isinstance(time_val, time):
                        checkin_time = time_val.strftime('%H:%M:%S')
                    elif isinstance(time_val, datetime):
                        checkin_time = time_val.strftime('%H:%M:%S')
                    else:
                        time_str = str(time_val).strip()
                        # Saat formatlarını parse et (08:00, 8:00, 0800, vb.)
                        if ':' in time_str:
                            parts = time_str.split(':')
                            if len(parts) >= 2:
                                hour = int(float(parts[0]))
                                minute = int(float(parts[1]))
                                checkin_time = f'{hour:02d}:{minute:02d}:00'
                        elif len(time_str) >= 3:
                            # 0800 formatı
                            hour = int(time_str[:2]) if len(time_str) >= 2 else 8
                            minute = int(time_str[2:4]) if len(time_str) >= 4 else 0
                            checkin_time = f'{hour:02d}:{minute:02d}:00'
                except:
                    checkin_time = '08:00:00'
            
            # Çıkış Saati
            checkout_time = '17:00:00'  # Varsayılan
            if checkout_col and pd.notna(row[checkout_col]):
                try:
                    time_val = row[checkout_col]
                    if isinstance(time_val, pd.Timestamp):
                        checkout_time = time_val.strftime('%H:%M:%S')
                    elif isinstance(time_val, time):
                        checkout_time = time_val.strftime('%H:%M:%S')
                    elif isinstance(time_val, datetime):
                        checkout_time = time_val.strftime('%H:%M:%S')
                    else:
                        time_str = str(time_val).strip()
                        # Saat formatlarını parse et
                        if ':' in time_str:
                            parts = time_str.split(':')
                            if len(parts) >= 2:
                                hour = int(float(parts[0]))
                                minute = int(float(parts[1]))
                                checkout_time = f'{hour:02d}:{minute:02d}:00'
                        elif len(time_str) >= 3:
                            # 1700 formatı
                            hour = int(time_str[:2]) if len(time_str) >= 2 else 17
                            minute = int(time_str[2:4]) if len(time_str) >= 4 else 0
                            checkout_time = f'{hour:02d}:{minute:02d}:00'
                except:
                    checkout_time = '17:00:00'
            
            # Vardiya - kolondan al, C# tarafında firma kurallarına göre normalize edilecek
            shift_type = None
            if shift_col and pd.notna(row[shift_col]):
                shift_type = str(row[shift_col]).strip()
            
            # Eğer vardiya yoksa, giriş/çıkış saatlerinden oluştur
            # (C# tarafında firma kurallarına göre normalize edilecek)
            if not shift_type or shift_type == '':
                # Giriş ve çıkış saatlerinden vardiya formatını oluştur (örn: 8/16)
                try:
                    checkin_hour = int(checkin_time.split(':')[0])
                    checkout_hour = int(checkout_time.split(':')[0])
                    # Gece vardiyası kontrolü (0-8 arası çıkış)
                    if checkout_hour < 8:
                        shift_type = '0/' + str(checkout_hour)
                    else:
                        shift_type = str(checkin_hour) + '/' + str(checkout_hour)
                except:
                    shift_type = 'Bilinmiyor'
            # Eğer vardiya varsa, olduğu gibi kullan (C# tarafında normalize edilecek)
            
            if personnel_code:
                records.append({
                    'PersonnelCode': personnel_code,
                    'PersonnelName': personnel_name,
                    'Date': record_date,
                    'CheckInTime': checkin_time,
                    'CheckOutTime': checkout_time,
                    'ShiftType': shift_type
                })
        except Exception as e:
            continue
    
    print(json.dumps(records, ensure_ascii=False))
except Exception as e:
    print(json.dumps({'error': str(e)}))
    sys.exit(1)
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
                    ShowError("Python işlemi başlatılamadı.");
                    return;
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    ShowError($"Excel okuma hatası: {error}");
                    return;
                }

                // JSON parse et
                var records = JsonSerializer.Deserialize<List<StoredPDKSRecord>>(output, jsonOptions);
                
                if (records == null || records.Count == 0)
                {
                    ShowError("Excel dosyasından veri okunamadı. Lütfen şablon formatını kontrol edin.\n\nBeklenen format: Sicil | Ad Soyad | Tarih | Giriş Saati | Çıkış Saati | Vardiya");
                    return;
                }

                // Tarih ve saat formatlarını düzelt
                var validRecords = new List<StoredPDKSRecord>();
                var errors = new List<string>();

                for (int i = 0; i < records.Count; i++)
                {
                    var record = records[i];
                    
                    if (string.IsNullOrWhiteSpace(record.PersonnelCode))
                    {
                        errors.Add($"Satır {i + 1}: Sicil boş, atlanıyor.");
                        continue;
                    }

                    // Tarih parse - TR formatına göre normalize et
                    DateTime parsedDate;
                    string dateStr = record.Date.ToString();
                    bool dateParsed = false;
                    
                    // TR formatlarını dene: dd.MM.yyyy, dd/MM/yyyy, yyyy-MM-dd, vb.
                    var trCulture = new CultureInfo("tr-TR");
                    if (DateTime.TryParse(dateStr, trCulture, DateTimeStyles.None, out parsedDate))
                    {
                        record.Date = parsedDate.Date;
                        dateParsed = true;
                    }
                    else if (DateTime.TryParseExact(dateStr, new[] { "dd.MM.yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "dd.MM.yy", "dd/MM/yy" }, 
                        trCulture, DateTimeStyles.None, out parsedDate))
                    {
                        record.Date = parsedDate.Date;
                        dateParsed = true;
                    }
                    else if (DateTime.TryParse(dateStr, out parsedDate))
                    {
                        record.Date = parsedDate.Date;
                        dateParsed = true;
                    }
                    
                    if (!dateParsed)
                    {
                        errors.Add($"Satır {i + 1}: Tarih geçersiz ({dateStr}), bugünün tarihi kullanılıyor.");
                        record.Date = DateTime.Now.Date;
                    }

                    // Giriş saati parse
                    if (TimeSpan.TryParse(record.CheckInTime.ToString(), out TimeSpan checkIn))
                    {
                        record.CheckInTime = checkIn;
                    }
                    else
                    {
                        errors.Add($"Satır {i + 1}: Giriş saati geçersiz, varsayılan 08:00 kullanılıyor.");
                        record.CheckInTime = new TimeSpan(8, 0, 0);
                    }

                    // Çıkış saati parse
                    if (TimeSpan.TryParse(record.CheckOutTime.ToString(), out TimeSpan checkOut))
                    {
                        record.CheckOutTime = checkOut;
                    }
                    else
                    {
                        errors.Add($"Satır {i + 1}: Çıkış saati geçersiz, varsayılan 17:00 kullanılıyor.");
                        record.CheckOutTime = new TimeSpan(17, 0, 0);
                    }

                    // Eksik alanları doldur
                    if (string.IsNullOrWhiteSpace(record.PersonnelName))
                    {
                        record.PersonnelName = record.PersonnelCode;
                    }
                    
                    // Vardiya tipini firma tanımındaki kurallara göre normalize et
                    string? companyCode = cmbCompanyCode.SelectedValue?.ToString();
                    if (!string.IsNullOrWhiteSpace(companyCode))
                    {
                        record.ShiftType = NormalizeShiftTypeWithCompanyRules(record.ShiftType, record.CheckInTime, record.CheckOutTime, companyCode);
                    }
                    else
                    {
                        // Firma seçilmemişse, giriş/çıkış saatlerinden oluştur
                        if (string.IsNullOrWhiteSpace(record.ShiftType) || 
                            record.ShiftType.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase))
                        {
                            record.ShiftType = FormatShiftTypeFromTimes(record.CheckInTime, record.CheckOutTime);
                        }
                    }

                    validRecords.Add(record);
                }

                if (validRecords.Count == 0)
                {
                    ShowError("Geçerli kayıt bulunamadı. Lütfen Excel formatını kontrol edin.");
                    return;
                }

                // Uyarıları göster
                if (errors.Count > 0)
                {
                    var warningMsg = "Bazı kayıtlarda uyarılar var:\n" + string.Join("\n", errors.Take(5));
                    if (errors.Count > 5)
                    {
                        warningMsg += $"\n... ve {errors.Count - 5} kayıt daha.";
                    }
                    MessageBox.Show(warningMsg, "Uyarılar", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                ImportedRecords = validRecords;
                ShowSuccess($"{validRecords.Count} günlük kayıt Excel'den başarıyla yüklendi.");
            }
            catch (Exception ex)
            {
                ShowError($"Excel import hatası: {ex.Message}");
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                HideMessages();

                // Firma kodu kontrolü
                if (cmbCompanyCode.SelectedValue == null)
                {
                    ShowError("Lütfen bir firma seçin.");
                    return;
                }

                // Yıl ve ay kontrolü
                if (cmbYear.SelectedValue == null)
                {
                    ShowError("Geçerli bir yıl seçin.");
                    return;
                }
                int year = (int)cmbYear.SelectedValue;

                if (cmbMonth.SelectedValue == null)
                {
                    ShowError("Geçerli bir ay seçin.");
                    return;
                }
                int month = (int)cmbMonth.SelectedValue;

                // Kayıt kontrolü
                if (ImportedRecords == null || ImportedRecords.Count == 0)
                {
                    ShowError("Kaydedilecek veri yok. Lütfen önce Excel'den veri yükleyin.");
                    return;
                }

                // Değerleri kaydet
                CompanyCode = cmbCompanyCode.SelectedValue?.ToString() ?? string.Empty;
                Year = year;
                Month = month;

                // CarryOverStateService'e kaydet
                var carryOverService = new CarryOverStateService();
                carryOverService.SaveStoredPDKSRecords(CompanyCode, Year, Month, ImportedRecords);

                MessageBox.Show(
                    $"{ImportedRecords.Count} günlük kayıt başarıyla kaydedildi.\n" +
                    $"Firma: {CompanyCode}\n" +
                    $"Dönem: {Year}/{Month:D2}",
                    "Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowError($"Kaydetme hatası: {ex.Message}\n\nDetay: {ex.StackTrace}");
            }
        }

        private void btnDownloadTemplate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Yıl ve ay kontrolü
                if (cmbYear.SelectedValue == null)
                {
                    ShowError("Geçerli bir yıl seçin.");
                    return;
                }
                int year = (int)cmbYear.SelectedValue;

                if (cmbMonth.SelectedValue == null)
                {
                    ShowError("Geçerli bir ay seçin.");
                    return;
                }
                int month = (int)cmbMonth.SelectedValue;

                // Firma kodu kontrolü
                string? companyCode = cmbCompanyCode.SelectedValue?.ToString();
                if (string.IsNullOrWhiteSpace(companyCode))
                {
                    ShowError("Lütfen önce bir firma seçin.");
                    return;
                }

                // Kullanıcıya dosya kaydetme yeri sor
                var saveFileDialog = new SaveFileDialog
                {
                    Title = "Excel Şablonunu Kaydet",
                    Filter = "Excel Dosyaları (*.xlsx)|*.xlsx|Tüm Dosyalar (*.*)|*.*",
                    FileName = $"PDKS_Kayit_Sablonu_{companyCode}_{year}_{month:D2}.xlsx",
                    DefaultExt = ".xlsx",
                    AddExtension = true
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    string excelPath = saveFileDialog.FileName;

                    // Geçmiş verileri yükle
                    var carryOverService = new CarryOverStateService();
                    var existingRecords = carryOverService.LoadStoredPDKSRecords(companyCode, year, month);

                    using (var package = new ExcelPackage())
                    {
                        var worksheet = package.Workbook.Worksheets.Add("PDKS Kayıtları");

                        // Başlık satırı
                        worksheet.Cells[1, 1].Value = "Sicil";
                        worksheet.Cells[1, 2].Value = "Ad Soyad";
                        worksheet.Cells[1, 3].Value = "Tarih";
                        worksheet.Cells[1, 4].Value = "Giriş Saati";
                        worksheet.Cells[1, 5].Value = "Çıkış Saati";
                        worksheet.Cells[1, 6].Value = "Vardiya";

                        // Başlık stilini ayarla
                        using (var range = worksheet.Cells[1, 1, 1, 6])
                        {
                            range.Style.Font.Bold = true;
                            range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                            range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(59, 130, 246));
                            range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                            range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                        }

                        // Sütun genişliklerini ayarla
                        worksheet.Column(1).Width = 15; // Sicil
                        worksheet.Column(2).Width = 25; // Ad Soyad
                        worksheet.Column(3).Width = 12; // Tarih
                        worksheet.Column(4).Width = 12; // Giriş Saati
                        worksheet.Column(5).Width = 12; // Çıkış Saati
                        worksheet.Column(6).Width = 15; // Vardiya

                        int row = 2;

                        // Vardiya sütununu metin formatına ayarla (8/16 gibi değerlerin tarih olarak formatlanmaması için)
                        using (var vardiyaRange = worksheet.Cells[2, 6, 1000, 6]) // Yeterince büyük bir aralık
                        {
                            vardiyaRange.Style.Numberformat.Format = "@"; // Metin formatı
                        }

                        // Geçmiş verileri doldur
                        if (existingRecords != null && existingRecords.Count > 0)
                        {
                            foreach (var record in existingRecords.OrderBy(r => r.Date).ThenBy(r => r.PersonnelName))
                            {
                                worksheet.Cells[row, 1].Value = record.PersonnelCode;
                                worksheet.Cells[row, 2].Value = record.PersonnelName;
                                // Tarihi DateTime olarak yaz (formatı daha sonra ayarlayacağız)
                                worksheet.Cells[row, 3].Value = record.Date;
                                worksheet.Cells[row, 4].Value = record.CheckInTime.ToString(@"hh\:mm");
                                worksheet.Cells[row, 5].Value = record.CheckOutTime.ToString(@"hh\:mm");
                                // Vardiya değerini metin olarak yaz
                                worksheet.Cells[row, 6].Value = record.ShiftType;
                                worksheet.Cells[row, 6].Style.Numberformat.Format = "@"; // Metin formatı
                                row++;
                            }
                        }

                        // Örnek veri satırı kaldırıldı - dummy veriler artık eklenmiyor

                        // Tarih sütununu Türkçe formatına ayarla (dd.MM.yyyy)
                        if (row > 2)
                        {
                            using (var range = worksheet.Cells[2, 3, row - 1, 3])
                            {
                                range.Style.Numberformat.Format = "dd.mm.yyyy"; // Türkçe tarih formatı
                            }
                        }
                        
                        // Gelecekte eklenecek satırlar için de tarih sütununu formatla
                        using (var tarihRange = worksheet.Cells[2, 3, 1000, 3])
                        {
                            tarihRange.Style.Numberformat.Format = "dd.mm.yyyy"; // Türkçe tarih formatı
                        }

                        package.SaveAs(new FileInfo(excelPath));
                    }

                    MessageBox.Show(
                        $"Excel şablonu başarıyla oluşturuldu!\n\n" +
                        $"Dosya: {excelPath}\n" +
                        $"{(existingRecords != null && existingRecords.Count > 0 ? $"{existingRecords.Count} geçmiş kayıt eklendi." : "Örnek veri eklendi.")}",
                        "Başarılı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Dosyayı otomatik aç
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = excelPath,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        // Dosya açma başarısız olursa sessizce geç
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"Excel şablonu oluşturulurken hata oluştu: {ex.Message}");
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void ShowError(string message)
        {
            txtErrorMessage.Text = message;
            errorBorder.Visibility = Visibility.Visible;
            successBorder.Visibility = Visibility.Collapsed;
        }

        private void ShowSuccess(string message)
        {
            txtSuccessMessage.Text = message;
            successBorder.Visibility = Visibility.Visible;
            errorBorder.Visibility = Visibility.Collapsed;
        }

        private void HideMessages()
        {
            errorBorder.Visibility = Visibility.Collapsed;
            successBorder.Visibility = Visibility.Collapsed;
        }

        private string? FindPythonExecutable()
        {
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
                        process.WaitForExit(2000);
                        if (process.ExitCode == 0 || process.ExitCode == 1)
                        {
                            return cmd;
                        }
                    }
                }
                catch
                {
                    continue;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Giriş ve çıkış saatlerinden vardiya formatını oluşturur (örn: 8/16, 8/18, 0/8)
        /// </summary>
        private string FormatShiftTypeFromTimes(TimeSpan checkIn, TimeSpan checkOut)
        {
            int checkInHour = checkIn.Hours;
            int checkOutHour = checkOut.Hours;
            
            // Gece vardiyası kontrolü (0-8 arası çıkış)
            if (checkOutHour < 8)
            {
                return $"0/{checkOutHour}";
            }
            
            return $"{checkInHour}/{checkOutHour}";
        }

        /// <summary>
        /// Vardiya tipini firma tanımındaki kurallara göre normalize eder
        /// ShiftPatterns ve ShiftPatternMappings'e göre eşleştirme yapar
        /// </summary>
        private string NormalizeShiftTypeWithCompanyRules(string shiftType, TimeSpan checkIn, TimeSpan checkOut, string companyCode)
        {
            if (string.IsNullOrWhiteSpace(shiftType) || shiftType.Equals("Bilinmiyor", StringComparison.OrdinalIgnoreCase))
            {
                return FormatShiftTypeFromTimes(checkIn, checkOut);
            }

            // Firma config'ini yükle
            if (currentConfig?.CompanyConfigs == null)
            {
                return FormatShiftTypeFromTimes(checkIn, checkOut);
            }

            var company = currentConfig.CompanyConfigs.FirstOrDefault(c => c.CompanyCode == companyCode);
            if (company?.ShiftRuleConfigs == null || company.ShiftRuleConfigs.Count == 0)
            {
                return FormatShiftTypeFromTimes(checkIn, checkOut);
            }

            // Vardiya tipini normalize et (PDKSDataModel'deki mantıkla aynı)
            string normalizedShift = NormalizeShiftString(shiftType);

            // ShiftRuleConfig'lerde eşleşme ara
            foreach (var shiftConfig in company.ShiftRuleConfigs)
            {
                // ShiftPatterns'te ara
                if (shiftConfig.ShiftPatterns != null)
                {
                    foreach (var pattern in shiftConfig.ShiftPatterns)
                    {
                        if (pattern == "*")
                            continue;

                        string normalizedPattern = NormalizeShiftString(pattern);
                        if (string.Equals(normalizedPattern, normalizedShift, StringComparison.OrdinalIgnoreCase))
                        {
                            // Eşleşme bulundu - pattern'in orijinal formatını "8/16" formatına çevir
                            return ConvertPatternToShiftFormat(pattern);
                        }
                    }
                }

                // ShiftPatternMappings'te ara
                if (shiftConfig.ShiftPatternMappings != null)
                {
                    foreach (var mapping in shiftConfig.ShiftPatternMappings)
                    {
                        string normalizedMapping = NormalizeShiftString(mapping);
                        if (string.Equals(normalizedMapping, normalizedShift, StringComparison.OrdinalIgnoreCase))
                        {
                            // Eşleşme bulundu - mapping'in orijinal formatını "8/16" formatına çevir
                            return ConvertPatternToShiftFormat(mapping);
                        }
                    }
                }
            }

            // Eşleşme bulunamadı - giriş/çıkış saatlerinden oluştur
            return FormatShiftTypeFromTimes(checkIn, checkOut);
        }

        /// <summary>
        /// Vardiya string'ini normalize eder (PDKSDataModel'deki mantıkla aynı)
        /// </summary>
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

        /// <summary>
        /// Zaman segmentini normalize eder (HHmm formatına)
        /// </summary>
        private bool TryNormalizeTimeSegment(string segment, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(segment))
            {
                return false;
            }

            string cleaned = segment.Trim();
            cleaned = cleaned.Replace(".", ":");

            try
            {
                if (TimeSpan.TryParse(cleaned, out var time))
                {
                    normalized = time.ToString(@"HHmm");
                    return true;
                }
            }
            catch
            {
                // Parse hatası
            }

            // Sadece rakamları al
            string digitsOnly = new string(cleaned.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digitsOnly))
            {
                if (digitsOnly.Length == 4 &&
                    int.TryParse(digitsOnly.Substring(0, 2), out var hourFromFour) &&
                    int.TryParse(digitsOnly.Substring(2, 2), out var minuteFromFour) &&
                    hourFromFour >= 0 && hourFromFour < 24 &&
                    minuteFromFour >= 0 && minuteFromFour < 60)
                {
                    normalized = $"{hourFromFour:00}{minuteFromFour:00}";
                    return true;
                }

                if (digitsOnly.Length == 3 &&
                    int.TryParse(digitsOnly.Substring(0, 1), out var hourFromThree) &&
                    int.TryParse(digitsOnly.Substring(1, 2), out var minuteFromThree) &&
                    hourFromThree >= 0 && hourFromThree < 24 &&
                    minuteFromThree >= 0 && minuteFromThree < 60)
                {
                    normalized = $"{hourFromThree:00}{minuteFromThree:00}";
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
            catch
            {
                // Parse hatası
            }

            return false;
        }

        /// <summary>
        /// Pattern'i (örn: "08:00-18:00", "8/16", "0800-1800") "8/16" formatına çevirir
        /// </summary>
        private string ConvertPatternToShiftFormat(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return "Bilinmiyor";
            }

            // Önce normalize et
            string normalized = NormalizeShiftString(pattern);
            
            // "0800-1800" formatını "8/16" formatına çevir
            if (normalized.Contains("-"))
            {
                var parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    // "0800" -> 8, "1800" -> 18
                    if (int.TryParse(parts[0], out int startHour) && int.TryParse(parts[1], out int endHour))
                    {
                        int start = startHour / 100; // 0800 -> 8
                        int end = endHour / 100;     // 1800 -> 18
                        return $"{start}/{end}";
                    }
                }
            }

            // Eğer zaten "8/16" formatındaysa olduğu gibi döndür
            if (System.Text.RegularExpressions.Regex.IsMatch(pattern, @"^\d+/\d+$"))
            {
                return pattern;
            }

            // Geçersiz format
            return "Bilinmiyor";
        }

        /// <summary>
        /// Vardiya tipini normalize eder - sadece 8/16, 16/8 gibi formatları kabul eder (fallback)
        /// </summary>
        private string NormalizeShiftType(string shiftType, TimeSpan checkIn, TimeSpan checkOut)
        {
            if (string.IsNullOrWhiteSpace(shiftType))
            {
                return FormatShiftTypeFromTimes(checkIn, checkOut);
            }

            string normalized = shiftType.Trim();
            
            // Eğer zaten "8/16" formatındaysa (veya varyasyonları: 16/8, 0/8, vb.) olduğu gibi döndür
            if (System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^\d+/\d+$"))
            {
                return normalized;
            }
            
            // Geçersiz format - giriş/çıkış saatlerinden oluştur
            return FormatShiftTypeFromTimes(checkIn, checkOut);
        }
    }
}

