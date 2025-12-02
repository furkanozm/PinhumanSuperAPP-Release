using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace WebScraper
{
    /// <summary>
    /// Excel dosyasının yapısına bakarak uygun veri şablonunu tahmin eden basit servis.
    /// İlk sürümde sadece sütun başlıkları ve sütun sayısı üzerinden kaba bir skor hesaplar.
    /// </summary>
    public class DataTemplateDetectionService
    {
        public DataTemplate? DetectTemplate(string filePath, IEnumerable<DataTemplate> templates, out string reason)
        {
            reason = string.Empty;

            if (!File.Exists(filePath))
            {
                reason = "Dosya bulunamadı.";
                return null;
            }

            var templateList = templates?.ToList() ?? new List<DataTemplate>();
            if (templateList.Count == 0)
            {
                reason = "Tanımlı veri şablonu yok.";
                return null;
            }

            try
            {
                // EPPlus 8+ için kişisel/kurumsal olmayan kullanım lisans bilgisini set et
                OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

                using var package = new ExcelPackage(new FileInfo(filePath));
                var sheet = package.Workbook.Worksheets.FirstOrDefault();
                if (sheet == null)
                {
                    reason = "Çalışma sayfası bulunamadı.";
                    return null;
                }

                int lastRow = sheet.Dimension.End.Row;
                int lastCol = sheet.Dimension.End.Column;
                
                // Yatay puantaj şablonu kontrolü: Gün başlıklarını (1-31) ara
                bool isHorizontalDailyHours = false;
                int headerRow = 1;
                
                // İlk 20 satırı tara (yatay puantajlarda gün başlıkları genelde üstte)
                int maxScanRow = Math.Min(lastRow, 20);
                for (int row = 1; row <= maxScanRow && !isHorizontalDailyHours; row++)
                {
                    var candidateDayCols = new List<(int Day, int Col)>();
                    int? previousDay = null;
                    
                    for (int col = 1; col <= lastCol; col++)
                    {
                        var text = sheet.Cells[row, col].Text?.Trim();
                        if (int.TryParse(text, out int day) && day is >= 1 and <= 31)
                        {
                            // Ardışık gün kontrolü
                            if (previousDay.HasValue && day == previousDay.Value + 1)
                            {
                                candidateDayCols.Add((day, col));
                                previousDay = day;
                            }
                            else if (!previousDay.HasValue || day == 1)
                            {
                                // Yeni seri başlıyor
                                candidateDayCols.Clear();
                                candidateDayCols.Add((day, col));
                                previousDay = day;
                            }
                            else
                            {
                                // Ardışıklık bozuldu, yeni seri başlat
                                if (candidateDayCols.Count >= 3)
                                {
                                    // Yeterli ardışık gün bulundu
                                    isHorizontalDailyHours = true;
                                    headerRow = row;
                                    break;
                                }
                                candidateDayCols.Clear();
                                candidateDayCols.Add((day, col));
                                previousDay = day;
                            }
                        }
                        else
                        {
                            // Sayı değil, ardışıklığı sıfırla
                            if (candidateDayCols.Count >= 3)
                            {
                                // Satır içinde yeterli ardışık gün bulundu
                                isHorizontalDailyHours = true;
                                headerRow = row;
                                break;
                            }
                            previousDay = null;
                        }
                    }
                    
                    // Satır sonunda da kontrol et
                    if (!isHorizontalDailyHours && candidateDayCols.Count >= 3)
                    {
                        isHorizontalDailyHours = true;
                        headerRow = row;
                    }
                }
                
                // Başlıkları oku
                var headers = new List<string>();
                for (int col = 1; col <= lastCol; col++)
                {
                    var value = sheet.Cells[headerRow, col].Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        headers.Add(value);
                    }
                }

                if (headers.Count == 0)
                {
                    reason = "Başlık satırı boş.";
                    return null;
                }

                // Her şablon için basit skor hesabı
                DataTemplate? bestTemplate = null;
                double bestScore = 0;

                foreach (var template in templateList)
                {
                    // Yatay puantaj şablonu için özel kontrol
                    if (template.TemplateType == "Horizontal_DailyHours")
                    {
                        if (isHorizontalDailyHours)
                        {
                            // Yatay puantaj tespit edildi - bu şablonu kullan
                            // ExpectedColumns varsa kontrol et, yoksa direkt eşleştir
                            if (template.ExpectedColumns == null || template.ExpectedColumns.Count == 0)
                            {
                                bestTemplate = template;
                                bestScore = 1.0;
                                reason = "Yatay puantaj şablonu tespit edildi (gün başlıkları bulundu).";
                                return bestTemplate;
                            }
                            
                            // ExpectedColumns varsa basit bir kontrol yap
                            int matchCount = 0;
                            foreach (var expected in template.ExpectedColumns)
                            {
                                // Hem başlık satırında hem de üst satırlarda ara
                                bool found = false;
                                
                                // Başlık satırında ara
                                if (headers.Any(h => h.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0))
                                {
                                    found = true;
                                }
                                // Üst satırlarda da ara (max 5 satır yukarı)
                                if (!found && headerRow > 1)
                                {
                                    for (int searchRow = Math.Max(1, headerRow - 5); searchRow < headerRow; searchRow++)
                                    {
                                        for (int col = 1; col <= Math.Min(lastCol, 10); col++)
                                        {
                                            var text = sheet.Cells[searchRow, col].Text?.Trim();
                                            if (!string.IsNullOrWhiteSpace(text) && 
                                                text.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                found = true;
                                                break;
                                            }
                                        }
                                        if (found) break;
                                    }
                                }
                                
                                if (found)
                                    matchCount++;
                            }
                            
                            double score = template.ExpectedColumns.Count > 0 
                                ? (double)matchCount / template.ExpectedColumns.Count 
                                : 0.8; // ExpectedColumns yoksa veya boşsa yine de eşleştir
                            
                            // Yatay puantaj tespit edildiyse bonus ver
                            score += 0.3;
                            
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestTemplate = template;
                            }
                        }
                        continue; // Yatay şablon için burada devam et
                    }
                    
                    // Normal şablon kontrolü
                    if (template.ExpectedColumns == null || template.ExpectedColumns.Count == 0)
                    {
                        continue;
                    }

                    int matchCountNormal = 0;
                    foreach (var expected in template.ExpectedColumns)
                    {
                        if (headers.Any(h => h.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            matchCountNormal++;
                        }
                    }

                    double scoreNormal = (double)matchCountNormal / template.ExpectedColumns.Count;

                    if (scoreNormal > bestScore)
                    {
                        bestScore = scoreNormal;
                        bestTemplate = template;
                    }
                }

                if (bestTemplate == null || bestScore < 0.3)
                {
                    if (isHorizontalDailyHours)
                    {
                        reason = "Yatay puantaj şablonu tespit edildi ancak eşleşen veri şablonu bulunamadı. Lütfen 'Horizontal_DailyHours' tipinde bir veri şablonu tanımlayın.";
                    }
                    else
                    {
                        reason = "Hiçbir şablon yeterince eşleşmedi.";
                    }
                    return null;
                }

                reason = $"En yüksek skor: {bestScore:0.##} ({bestTemplate.Name})";
                return bestTemplate;
            }
            catch (Exception ex)
            {
                reason = $"Algılama hatası: {ex.Message}";
                return null;
            }
        }
    }
}


