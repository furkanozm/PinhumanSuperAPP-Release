using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace WebScraper
{
    /// <summary>
    /// Sözleşmeli personel işlemleri için servis sınıfı - SOLID Single Responsibility
    /// </summary>
    public class SozPersonelService : IDisposable
    {
        private IBrowser _browser;
        private IPage _page;
        private bool _isDisposed;

        public SozPersonelService()
        {
            _isDisposed = false;
        }

        /// <summary>
        /// Sözleşmeli personel işlemini başlatır
        /// </summary>
        public async Task StartSozPersonelProcessAsync(SozPersonelSettings config, List<Dictionary<string, string>> excelData, Action<string> logCallback)
        {
            if (excelData == null || excelData.Count == 0)
            {
                logCallback("⚠️ İşlenecek sözleşmeli personel verisi bulunamadı");
                return;
            }

            logCallback($"🚀 Sözleşmeli personel işlemi başlatılıyor - {excelData.Count} kayıt");

            try
            {
                // Playwright başlat
                var playwright = await Playwright.CreateAsync();
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = config.HeadlessMode,
                    SlowMo = 100,
                    Args = new[] { "--start-maximized" }
                });

                _page = await _browser.NewPageAsync();

                // Login işlemleri burada yapılacak
                logCallback("🔐 Sözleşmeli personel sistemine giriş yapılıyor...");

                // İşlem tamamlandı
                logCallback("✅ Sözleşmeli personel işlemi tamamlandı");
            }
            catch (Exception ex)
            {
                logCallback($"❌ Sözleşmeli personel işlemi hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Tarayıcıyı kapatır
        /// </summary>
        public async Task CleanupBrowserAsync()
        {
            try
            {
                if (_page is not null)
                {
                    await _page.CloseAsync();
                    _page = null;
                }

                if (_browser is not null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                }
            }
            catch (Exception ex)
            {
                // Log hatası durumunda sessizce devam et
                Console.WriteLine($"SozPersonel Cleanup hatası: {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                CleanupBrowserAsync().Wait();
                _isDisposed = true;
            }
        }
    }
}
