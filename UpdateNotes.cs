using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WebScraper
{
    public class UpdateNote
    {
        public string Version { get; set; } = "";
        public DateTime ReleaseDate { get; set; } = DateTime.Now;
        public List<string> NewFeatures { get; set; } = new();
        public List<string> Improvements { get; set; } = new();
        public List<string> BugFixes { get; set; } = new();
        public List<string> Changes { get; set; } = new();
    }

    public class UpdateNotesCollection
    {
        public List<UpdateNote> Updates { get; set; } = new();

        private static readonly string UpdateNotesFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "UPDATE_NOTES.json"
        );

        public static UpdateNotesCollection Load()
        {
            try
            {
                if (File.Exists(UpdateNotesFilePath))
                {
                    var jsonContent = File.ReadAllText(UpdateNotesFilePath);
                    var notes = JsonSerializer.Deserialize<UpdateNotesCollection>(jsonContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    return notes ?? new UpdateNotesCollection();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Güncelleme notları yüklenirken hata: {ex.Message}");
            }

            return new UpdateNotesCollection();
        }

        public static void Save(UpdateNotesCollection notes)
        {
            try
            {
                var jsonContent = JsonSerializer.Serialize(notes, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(UpdateNotesFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Güncelleme notları kaydedilirken hata: {ex.Message}");
            }
        }

        public List<UpdateNote> GetUpdatesSince(string version)
        {
            if (string.IsNullOrEmpty(version))
                return Updates.OrderByDescending(u => u.ReleaseDate).ToList();

            return Updates
                .Where(u => VersionInfo.IsNewer(version, u.Version))
                .OrderByDescending(u => u.ReleaseDate)
                .ToList();
        }

        public UpdateNote? GetLatestUpdate()
        {
            return Updates
                .OrderByDescending(u => u.ReleaseDate)
                .ThenByDescending(u => u.Version)
                .FirstOrDefault();
        }
    }
}

