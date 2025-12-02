using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace WebScraper
{
    public class PythonInstallerService
    {
        private const string PYTHON_INSTALLER_DIR = "python_installer";
        private const string PYTHON_INSTALLER_PATTERN = "python-*.exe";

        /// <summary>
        /// Python'un sistemde kurulu olup olmadığını kontrol eder
        /// </summary>
        public static bool IsPythonInstalled()
        {
            try
            {
                // python komutunu dene
                var processInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(2000);
                        if (process.ExitCode == 0)
                        {
                            return true;
                        }
                    }
                }

                // py launcher'ı dene (Windows Python Launcher)
                processInfo.FileName = "py";
                processInfo.Arguments = "--version";
                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        process.WaitForExit(2000);
                        if (process.ExitCode == 0)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Python'un kurulu sürümünü döndürür
        /// </summary>
        public static string? GetPythonVersion()
        {
            try
            {
                // python komutunu dene
                var processInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = "--version",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit(2000);
                        
                        if (process.ExitCode == 0)
                        {
                            // Çıktıyı temizle ve sadece sürüm numarasını al
                            var version = !string.IsNullOrEmpty(output) ? output.Trim() : error.Trim();
                            if (!string.IsNullOrEmpty(version))
                            {
                                // "Python 3.12.0" formatından sadece "3.12.0" kısmını al
                                if (version.StartsWith("Python ", StringComparison.OrdinalIgnoreCase))
                                {
                                    version = version.Substring(7).Trim();
                                }
                                return version;
                            }
                        }
                    }
                }

                // py launcher'ı dene (Windows Python Launcher)
                processInfo.FileName = "py";
                processInfo.Arguments = "--version";
                using (var process = Process.Start(processInfo))
                {
                    if (process != null)
                    {
                        var output = process.StandardOutput.ReadToEnd();
                        var error = process.StandardError.ReadToEnd();
                        process.WaitForExit(2000);
                        
                        if (process.ExitCode == 0)
                        {
                            var version = !string.IsNullOrEmpty(output) ? output.Trim() : error.Trim();
                            if (!string.IsNullOrEmpty(version))
                            {
                                if (version.StartsWith("Python ", StringComparison.OrdinalIgnoreCase))
                                {
                                    version = version.Substring(7).Trim();
                                }
                                return version;
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Python installer dosyasını bulur
        /// </summary>
        public static string? FindPythonInstaller()
        {
            try
            {
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var installerDir = Path.Combine(appDirectory, PYTHON_INSTALLER_DIR);

                if (!Directory.Exists(installerDir))
                {
                    return null;
                }

                var files = Directory.GetFiles(installerDir, PYTHON_INSTALLER_PATTERN);
                if (files.Length > 0)
                {
                    // En yeni dosyayı al
                    var latestFile = files[0];
                    var latestTime = File.GetLastWriteTime(latestFile);

                    foreach (var file in files)
                    {
                        var fileTime = File.GetLastWriteTime(file);
                        if (fileTime > latestTime)
                        {
                            latestTime = fileTime;
                            latestFile = file;
                        }
                    }

                    return latestFile;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Python'u sessiz modda kurar
        /// </summary>
        public static async Task<bool> InstallPythonAsync(string installerPath)
        {
            try
            {
                if (!File.Exists(installerPath))
                {
                    return false;
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/quiet InstallAllUsers=1 PrependPath=1 Include_test=0",
                    UseShellExecute = true,
                    Verb = "runas", // Yönetici yetkisi iste
                    CreateNoWindow = false
                };

                var process = Process.Start(processInfo);
                if (process == null)
                {
                    return false;
                }

                // Kurulumun tamamlanmasını bekle (maksimum 5 dakika)
                await Task.Run(() => process.WaitForExit(300000));

                if (process.HasExited && process.ExitCode == 0)
                {
                    // Kurulum sonrası PATH'i yenilemek için kısa bir bekleme
                    await Task.Delay(2000);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Python kurulumu sırasında hata oluştu:\n\n{ex.Message}", 
                    "Kurulum Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Python kurulumunu başlatır (kullanıcıya bilgi vererek)
        /// </summary>
        public static async Task<bool> InstallPythonWithDialogAsync()
        {
            var installerPath = FindPythonInstaller();
            
            if (string.IsNullOrEmpty(installerPath))
            {
                MessageBox.Show(
                    $"Python installer dosyası bulunamadı!\n\n" +
                    $"Lütfen Python installer'ı şu klasöre koyun:\n" +
                    $"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PYTHON_INSTALLER_DIR)}\n\n" +
                    $"Örnek: python-3.12.0-amd64.exe",
                    "Python Installer Bulunamadı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            var result = MessageBox.Show(
                $"Python sisteminizde bulunamadı.\n\n" +
                $"Python'u otomatik olarak kurmak ister misiniz?\n\n" +
                $"Kurulum için yönetici yetkisi gerekecektir.",
                "Python Kurulumu",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return false;
            }

            try
            {
                // Kurulum başlatılıyor mesajı
                var progressWindow = new Window
                {
                    Title = "Python Kurulumu",
                    Width = 400,
                    Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.NoResize
                };

                var stackPanel = new System.Windows.Controls.StackPanel
                {
                    Margin = new Thickness(20),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var label = new System.Windows.Controls.Label
                {
                    Content = "Python kurulumu başlatılıyor...\nLütfen bekleyin.",
                    HorizontalContentAlignment = HorizontalAlignment.Center
                };

                stackPanel.Children.Add(label);
                progressWindow.Content = stackPanel;
                progressWindow.Show();

                var installResult = await InstallPythonAsync(installerPath);

                progressWindow.Close();

                if (installResult)
                {
                    // Kurulum sonrası sürüm bilgisini al
                    var version = GetPythonVersion();
                    var versionText = !string.IsNullOrEmpty(version) 
                        ? $"Kurulu sürüm: Python {version}\n\n" 
                        : "";
                    
                    MessageBox.Show(
                        "Python başarıyla kuruldu! ✅\n\n" +
                        $"{versionText}" +
                        "Uygulamayı yeniden başlatmanız önerilir.",
                        "Kurulum Başarılı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return true;
                }
                else
                {
                    MessageBox.Show(
                        "Python kurulumu tamamlanamadı.\n\n" +
                        "Lütfen manuel olarak kurmayı deneyin.",
                        "Kurulum Hatası",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Python kurulumu sırasında hata oluştu:\n\n{ex.Message}",
                    "Kurulum Hatası",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return false;
            }
        }

        /// <summary>
        /// Uygulama başlangıcında Python kontrolü yapar ve gerekirse kurar
        /// </summary>
        public static async Task CheckAndInstallPythonOnStartupAsync()
        {
            if (IsPythonInstalled())
            {
                return; // Python zaten kurulu
            }

            // Kullanıcıya bilgi ver ve kurulum yap
            await InstallPythonWithDialogAsync();
        }
    }
}

