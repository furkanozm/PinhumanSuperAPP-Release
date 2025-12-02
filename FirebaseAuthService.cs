using System;
using System.Threading.Tasks;
using System.Text.Json;
using Firebase.Auth;
using System.IO;

namespace WebScraper
{
    public class FirebaseAuthService
    {
        private string? _currentUserEmail;
        private bool _isLoggedIn = false;
        private FirebaseAuthProvider _authProvider;
        private FirebaseAuthLink? _authLink;

        public FirebaseAuthService()
        {
            // Firebase konfigürasyonunu yükle
            var config = LoadFirebaseConfig();
            if (!string.IsNullOrEmpty(config.ApiKey))
            {
                WriteToLog($"Firebase config yüklendi. API Key: {config.ApiKey.Substring(0, Math.Min(10, config.ApiKey.Length))}...");
            }
            else
            {
                WriteToLog("Firebase config yüklendi. API Key boş!");
            }
            _authProvider = new FirebaseAuthProvider(new Firebase.Auth.FirebaseConfig(config.ApiKey));
        }

        public async Task<bool> LoginAsync(string email, string password)
        {
            try
            {
                var logMessage = $"Login denemesi: {email}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    logMessage = "Email veya şifre boş";
                    System.Diagnostics.Debug.WriteLine(logMessage);
                    WriteToLog(logMessage);
                    return false;
                }

                if (!IsValidEmail(email))
                {
                    logMessage = "Geçersiz email formatı";
                    System.Diagnostics.Debug.WriteLine(logMessage);
                    WriteToLog(logMessage);
                    return false;
                }

                logMessage = "Firebase authentication başlatılıyor...";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                // Firebase ile giriş yap
                _authLink = await _authProvider.SignInWithEmailAndPasswordAsync(email, password);
                
                logMessage = $"Firebase response: {_authLink?.User?.Email}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                if (_authLink?.User != null)
                {
                    _currentUserEmail = email;
                    _isLoggedIn = true;
                    logMessage = "Giriş başarılı";
                    System.Diagnostics.Debug.WriteLine(logMessage);
                    WriteToLog(logMessage);
                    return true;
                }

                logMessage = "Firebase user null döndü";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                return false;
            }
            catch (FirebaseAuthException ex)
            {
                var logMessage = $"Firebase login hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                logMessage = $"Firebase error code: {ex.Reason}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                logMessage = $"Firebase response data: {ex.ResponseData}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                logMessage = $"Firebase error type: {ex.GetType().Name}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                return false;
            }
            catch (Exception ex)
            {
                var logMessage = $"Login hatası: {ex.Message}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                
                logMessage = $"Exception type: {ex.GetType().Name}";
                System.Diagnostics.Debug.WriteLine(logMessage);
                WriteToLog(logMessage);
                return false;
            }
        }

        private void WriteToLog(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firebase_debug.log");
                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
                File.AppendAllText(logPath, logEntry + Environment.NewLine);
            }
            catch
            {
                // Log yazma hatası durumunda sessizce devam et
            }
        }

        public async Task<bool> RegisterAsync(string email, string password)
        {
            try
            {
                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(password))
                {
                    return false;
                }

                if (!IsValidEmail(email))
                {
                    return false;
                }

                if (password.Length < 6)
                {
                    return false;
                }

                // Firebase ile kayıt ol
                _authLink = await _authProvider.CreateUserWithEmailAndPasswordAsync(email, password);
                
                if (_authLink?.User != null)
                {
                    _currentUserEmail = email;
                    _isLoggedIn = true;
                    return true;
                }

                return false;
            }
            catch (FirebaseAuthException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Firebase kayıt hatası: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Kayıt hatası: {ex.Message}");
                return false;
            }
        }

        public void Logout()
        {
            _currentUserEmail = null;
            _isLoggedIn = false;
            _authLink = null;
        }

        public bool IsLoggedIn => _isLoggedIn;
        public string CurrentUserEmail => _currentUserEmail ?? "";

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private FirebaseConfig LoadFirebaseConfig()
        {
            try
            {
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "firebase-config.json");
                WriteToLog($"Firebase config dosya yolu: {configPath}");
                
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    WriteToLog($"Firebase config dosyası okundu. İçerik: {json}");
                    var config = JsonSerializer.Deserialize<FirebaseConfig>(json) ?? new FirebaseConfig();
                    WriteToLog($"Firebase config deserialize edildi. API Key: {config.ApiKey}");
                    return config;
                }
                else
                {
                    WriteToLog("Firebase config dosyası bulunamadı!");
                }
            }
            catch (Exception ex)
            {
                WriteToLog($"Firebase config yükleme hatası: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Firebase config yükleme hatası: {ex.Message}");
            }
            
            WriteToLog("Varsayılan Firebase config döndürülüyor");
            return new FirebaseConfig();
        }
    }

    public class FirebaseConfig
    {
        [System.Text.Json.Serialization.JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonPropertyName("authDomain")]
        public string AuthDomain { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonPropertyName("projectId")]
        public string ProjectId { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonPropertyName("storageBucket")]
        public string StorageBucket { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonPropertyName("messagingSenderId")]
        public string MessagingSenderId { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonPropertyName("appId")]
        public string AppId { get; set; } = "";
        
        [System.Text.Json.Serialization.JsonPropertyName("measurementId")]
        public string MeasurementId { get; set; } = "";

        public FirebaseConfig() { }

        public FirebaseConfig(string apiKey)
        {
            ApiKey = apiKey;
        }
    }
} 