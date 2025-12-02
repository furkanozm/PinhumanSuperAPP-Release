using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WebScraper
{
    /// <summary>
    /// Veri şablonlarını JSON dosyasında saklayan basit servis.
    /// İlk aşamada sadece listeleme ve basit ekleme yapıyoruz.
    /// </summary>
    public class DataTemplateService
    {
        private const string DATA_TEMPLATE_FILE = "data-templates.json";
        private const string LEGACY_KEY = "__legacy__";

        public List<DataTemplate> LoadTemplates(string? companyCode)
        {
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return new List<DataTemplate>();
            }

            var store = LoadStore();
            if (store.TryGetValue(companyCode.Trim(), out var templates))
            {
                return CloneTemplates(templates);
            }

            return new List<DataTemplate>();
        }

        public void SaveTemplates(string? companyCode, IEnumerable<DataTemplate> templates)
        {
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return;
            }

            var normalizedCode = companyCode.Trim();
            var store = LoadStore();
            store[normalizedCode] = CloneTemplates(templates ?? Enumerable.Empty<DataTemplate>());
            SaveStore(store);
        }

        public bool HasLegacyTemplates()
        {
            var store = LoadStore();
            return store.TryGetValue(LEGACY_KEY, out var list) && list.Count > 0;
        }

        public int GetLegacyTemplateCount()
        {
            var store = LoadStore();
            return store.TryGetValue(LEGACY_KEY, out var list) ? list.Count : 0;
        }

        public List<DataTemplate> MigrateLegacyTemplates(string? companyCode)
        {
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return new List<DataTemplate>();
            }

            var normalizedCode = companyCode.Trim();
            var store = LoadStore();
            if (!store.TryGetValue(LEGACY_KEY, out var legacy) || legacy.Count == 0)
            {
                return new List<DataTemplate>();
            }

            store.Remove(LEGACY_KEY);
            store[normalizedCode] = CloneTemplates(legacy);
            SaveStore(store);
            return CloneTemplates(legacy);
        }

        public void RenameCompanyTemplates(string? oldCompanyCode, string? newCompanyCode)
        {
            if (string.IsNullOrWhiteSpace(oldCompanyCode) ||
                string.IsNullOrWhiteSpace(newCompanyCode))
            {
                return;
            }

            var oldCode = oldCompanyCode.Trim();
            var newCode = newCompanyCode.Trim();
            if (oldCode.Equals(newCode, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var store = LoadStore();
            if (store.Remove(oldCode, out var templates))
            {
                store[newCode] = templates;
                SaveStore(store);
            }
        }

        public DataTemplate CreateTemplate(string name, string templateType, string sourceHint)
        {
            return new DataTemplate
            {
                Name = name,
                TemplateType = templateType,
                SourceHint = sourceHint
            };
        }

        private Dictionary<string, List<DataTemplate>> LoadStore()
        {
            var store = new Dictionary<string, List<DataTemplate>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(DATA_TEMPLATE_FILE))
            {
                return store;
            }

            try
            {
                var raw = File.ReadAllText(DATA_TEMPLATE_FILE);
                if (string.IsNullOrWhiteSpace(raw))
                {
                    return store;
                }

                raw = raw.Trim();

                if (raw.StartsWith("["))
                {
                    // Eski format: düz liste
                    var legacyList = JsonSerializer.Deserialize<List<DataTemplate>>(raw) ?? new List<DataTemplate>();
                    store[LEGACY_KEY] = NormalizeTemplates(legacyList);
                    return store;
                }

                var deserialized = JsonSerializer.Deserialize<Dictionary<string, List<DataTemplate>>>(raw);
                if (deserialized != null)
                {
                    foreach (var kvp in deserialized)
                    {
                        store[kvp.Key] = NormalizeTemplates(kvp.Value ?? new List<DataTemplate>());
                    }
                }
            }
            catch
            {
                // Dosya bozuksa sessizce yeni store döndür.
            }

            return store;
        }

        private void SaveStore(Dictionary<string, List<DataTemplate>> store)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(store, options);
            File.WriteAllText(DATA_TEMPLATE_FILE, json);
        }

        private List<DataTemplate> NormalizeTemplates(List<DataTemplate> templates)
        {
            var normalized = new List<DataTemplate>();
            if (templates == null)
            {
                return normalized;
            }

            foreach (var template in templates)
            {
                normalized.Add(CloneTemplate(template));
            }

            return normalized;
        }

        private List<DataTemplate> CloneTemplates(IEnumerable<DataTemplate> templates)
        {
            var list = new List<DataTemplate>();
            if (templates == null)
            {
                return list;
            }

            foreach (var template in templates)
            {
                list.Add(CloneTemplate(template));
            }

            return list;
        }

        private DataTemplate CloneTemplate(DataTemplate template)
        {
            if (template == null)
            {
                return new DataTemplate();
            }

            var clone = new DataTemplate
            {
                Id = string.IsNullOrWhiteSpace(template.Id) ? Guid.NewGuid().ToString() : template.Id,
                Name = template.Name ?? string.Empty,
                TemplateType = template.TemplateType ?? "Unknown",
                SourceHint = template.SourceHint ?? string.Empty,
                ExpectedColumns = template.ExpectedColumns != null
                    ? new List<string>(template.ExpectedColumns)
                    : new List<string>(),
                HasHolidaysInTemplate = template.HasHolidaysInTemplate
            };

            if (template.SymbolHourMap != null && template.SymbolHourMap.Count > 0)
            {
                clone.SymbolHourMap = new Dictionary<string, double>(template.SymbolHourMap, StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                clone.SymbolHourMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                if (clone.TemplateType == "Horizontal_DailyHours")
                {
                    clone.SymbolHourMap["x"] = 7.5;
                }
            }

            return clone;
        }

        private List<DataTemplate> GetDefaultTemplates()
        {
            return new List<DataTemplate>
            {
                new DataTemplate
                {
                    Name = "Varsayılan PDKS Log",
                    TemplateType = "PDKS_Log",
                    SourceHint = "Giriş/çıkış saatleri, turnike log kayıtları",
                    ExpectedColumns = new List<string> { "Sicil", "Kart No", "Ad", "Soyad", "Tarih", "Giriş", "Çıkış" }
                },
                new DataTemplate
                {
                    Name = "Yatay Gün-Saat Tablosu (Örnek)",
                    TemplateType = "Horizontal_DailyHours",
                    SourceHint = "Satır başına 1 personel, sütunlarda gün bazında çalışma saati",
                    ExpectedColumns = new List<string> { "Sicil", "Ad Soyad", "TCKN", "1", "2", "3" },
                    SymbolHourMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "x", 7.5 }
                    }
                }
            };
        }
    }
}


