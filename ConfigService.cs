using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebScraper
{
    /// <summary>
    /// Modüler config servisi - her modül kendi config dosyasını yönetir
    /// </summary>
    public static class ConfigService
    {
        private static readonly string _baseDirectory = AppDomain.CurrentDomain.BaseDirectory;

        /// <summary>
        /// Belirtilen config dosyasını yükler
        /// </summary>
        public static T LoadConfig<T>(string configFileName) where T : class, new()
        {
            try
            {
                string configPath = Path.Combine(_baseDirectory, configFileName);
                Console.WriteLine($"DEBUG: Config yolu: {configPath}");
                Console.WriteLine($"DEBUG: Dosya var mı: {File.Exists(configPath)}");

                if (!File.Exists(configPath))
                {
                    // Config dosyası yoksa varsayılan değerlerle oluştur
                    var defaultConfig = new T();
                    SaveConfig(configFileName, defaultConfig);
                    Console.WriteLine($"DEBUG: Varsayılan config oluşturuldu");
                    return defaultConfig;
                }

                string jsonContent = File.ReadAllText(configPath);
                Console.WriteLine($"DEBUG: JSON içeriği uzunluğu: {jsonContent.Length}");

                var result = JsonConvert.DeserializeObject<T>(jsonContent);
                Console.WriteLine($"DEBUG: Deserialize sonucu: {(result == null ? "NULL" : "OK")}");

                return result ?? new T();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config yükleme hatası ({configFileName}): {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
                return new T();
            }
        }

        /// <summary>
        /// Config dosyasını kaydeder
        /// </summary>
        public static void SaveConfig<T>(string configFileName, T config) where T : class
        {
            try
            {
                string configPath = Path.Combine(_baseDirectory, configFileName);
                string directoryPath = Path.GetDirectoryName(configPath);

                if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                string jsonContent = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config kaydetme hatası ({configFileName}): {ex.Message}");
            }
        }

        /// <summary>
        /// Ana config dosyasından belirli bir bölümü yükler
        /// </summary>
        public static T LoadConfigSection<T>(string configFileName, string sectionName) where T : class, new()
        {
            try
            {
                string configPath = Path.Combine(_baseDirectory, configFileName);

                if (!File.Exists(configPath))
                {
                    return new T();
                }

                string jsonContent = File.ReadAllText(configPath);
                var jsonObject = JObject.Parse(jsonContent);

                if (jsonObject[sectionName] != null)
                {
                    return jsonObject[sectionName]?.ToObject<T>() ?? new T();
                }

                return new T();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config bölümü yükleme hatası ({configFileName}.{sectionName}): {ex.Message}");
                return new T();
            }
        }

        /// <summary>
        /// Ana config dosyasına belirli bir bölümü kaydeder
        /// </summary>
        public static void SaveConfigSection<T>(string configFileName, string sectionName, T sectionData) where T : class
        {
            try
            {
                string configPath = Path.Combine(_baseDirectory, configFileName);

                JObject jsonObject;
                if (File.Exists(configPath))
                {
                    string existingContent = File.ReadAllText(configPath);
                    jsonObject = JObject.Parse(existingContent);
                }
                else
                {
                    jsonObject = new JObject();
                }

                jsonObject[sectionName] = JObject.FromObject(sectionData);
                string jsonContent = jsonObject.ToString(Formatting.Indented);
                File.WriteAllText(configPath, jsonContent);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config bölümü kaydetme hatası ({configFileName}.{sectionName}): {ex.Message}");
            }
        }

        /// <summary>
        /// Config dosyasının var olup olmadığını kontrol eder
        /// </summary>
        public static bool ConfigExists(string configFileName)
        {
            string configPath = Path.Combine(_baseDirectory, configFileName);
            return File.Exists(configPath);
        }

        /// <summary>
        /// Config dosyasını siler
        /// </summary>
        public static void DeleteConfig(string configFileName)
        {
            try
            {
                string configPath = Path.Combine(_baseDirectory, configFileName);
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config silme hatası ({configFileName}): {ex.Message}");
            }
        }

        /// <summary>
        /// Tüm config dosyalarını listeler
        /// </summary>
        public static string[] ListConfigFiles()
        {
            try
            {
                return Directory.GetFiles(_baseDirectory, "*.json");
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
