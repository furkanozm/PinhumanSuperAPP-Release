using System;
using System.Windows;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Management;
using OfficeOpenXml;

namespace WebScraper
{
    public class Program
    {
        // EPPlus lisans ayarı - Static constructor ile garanti altına alın
        static Program()
        {
            // EPPlus 8+ için yeni lisans API'si
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
        }

        [STAThread]
        public static void Main()
        {
            try
            {
                // EPPlus lisans ayarı (ücretsiz kullanım için) - Uygulama başlangıcında (çift garanti)
                OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

                // Excel dosyasından config'e veri aktar - Şimdilik devre dışı bırakıldı
                // MainWindow.ImportExcelToConfig(); kaldırıldı

                var app = new Application();

                // Uygulama kapatma event'lerini ayarla
                app.Exit += (sender, e) =>
                {
                    try
                    {
                        // Tüm Chrome/Playwright işlemlerini zorla sonlandır
                        ForceCloseAllBrowsers();

                        // Tüm WebScraper process'lerini kapat
                        ForceCloseAllWebScraperProcesses();

                        // Tüm çalışan işlemleri zorla sonlandır
                        var currentProcess = Process.GetCurrentProcess();

                        // Tüm thread'leri durdur
                        foreach (ProcessThread thread in currentProcess.Threads)
                        {
                            try
                            {
                                if (thread.ThreadState == System.Diagnostics.ThreadState.Running)
                                {
                                    thread.Dispose();
                                }
                            }
                            catch
                            {
                                // Thread dispose hatalarını yoksay
                            }
                        }

                        // Garbage collection'ı zorla
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();

                        // Process'i zorla sonlandır
                        currentProcess.Kill();

                        // Uygulamayı tamamen sonlandır
                        Environment.Exit(0);
                    }
                    catch
                    {
                        // Hata olsa bile uygulamayı kapat
                        Environment.Exit(0);
                    }
                };

                // Unhandled exception handler ekle
                app.DispatcherUnhandledException += (sender, e) =>
                {
                    MessageBox.Show($"İşlenmeyen hata:\n\n{e.Exception.Message}\n\nStack Trace:\n{e.Exception.StackTrace}", 
                        "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };

                var window = new LoginWindow();
                app.Run(window);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Uygulama başlatılırken kritik hata:\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message}", 
                    "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private static void ForceCloseAllBrowsers()
        {
            try
            {
                // Sadece Chromium process'lerini kapat
                var chromiumProcesses = Process.GetProcessesByName("chromium");
                foreach (var process in chromiumProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }

                // Playwright process'lerini kapat
                var playwrightProcesses = Process.GetProcessesByName("playwright");
                foreach (var process in playwrightProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et
            }
        }

        private static string GetCommandLine(int processId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
                using var objects = searcher.Get();
                foreach (ManagementObject obj in objects)
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch
            {
                // Hata durumunda boş string döndür
            }
            return "";
        }

        private static void ForceCloseAllWebScraperProcesses()
        {
            try
            {
                // WebScraper process'lerini kapat
                var webScraperProcesses = Process.GetProcessesByName("WebScraper");
                foreach (var process in webScraperProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }

                // dotnet process'lerini kontrol et (eğer WebScraper çalışıyorsa)
                var dotnetProcesses = Process.GetProcessesByName("dotnet");
                foreach (var process in dotnetProcesses)
                {
                    try
                    {
                        var commandLine = GetCommandLine(process.Id);
                        if (commandLine.Contains("WebScraper") || commandLine.Contains("WebScraper.dll"))
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch { /* Sessizce geç */ }
                }

                // Tüm child process'leri kapat
                var currentProcess = Process.GetCurrentProcess();
                foreach (var process in Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id != currentProcess.Id && 
                            (process.ProcessName.Contains("WebScraper") || 
                             process.ProcessName.Contains("chrome") ||
                             process.ProcessName.Contains("chromium") ||
                             process.ProcessName.Contains("playwright")))
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                    }
                    catch { /* Sessizce geç */ }
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et
            }
        }
    }
} 