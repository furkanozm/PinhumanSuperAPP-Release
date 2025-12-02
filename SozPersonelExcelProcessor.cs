using System;
using System.Collections.Generic;
using System.Linq;
using OfficeOpenXml;

namespace WebScraper
{
    /// <summary>
    /// Sözleşmeli personel Excel işlemleri için sınıf - SOLID Single Responsibility
    /// </summary>
    public class SozPersonelExcelProcessor
    {
        /// <summary>
        /// Excel dosyasından sözleşmeli personel verilerini okur
        /// </summary>
        public List<Dictionary<string, string>> LoadSozPersonelDataFromExcel(string excelFilePath)
        {
            var result = new List<Dictionary<string, string>>();

            using (var package = new ExcelPackage(new System.IO.FileInfo(excelFilePath)))
            {
                var worksheet = package.Workbook.Worksheets[0];
                if (worksheet == null) return result;

                // İlk satır başlık
                var headers = new List<string>();
                for (int col = 1; col <= worksheet.Dimension.Columns; col++)
                {
                    var headerValue = worksheet.Cells[1, col].Text?.Trim();
                    if (!string.IsNullOrEmpty(headerValue))
                    {
                        headers.Add(headerValue);
                    }
                }

                // Veri satırları
                for (int row = 2; row <= worksheet.Dimension.Rows; row++)
                {
                    var record = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                    for (int col = 1; col <= headers.Count; col++)
                    {
                        var value = worksheet.Cells[row, col].Text?.Trim() ?? "";
                        if (col <= headers.Count)
                        {
                            record[headers[col - 1]] = value;
                        }
                    }

                    if (record.Count > 0)
                    {
                        result.Add(record);
                    }
                }
            }

            return result;
        }
    }
}
