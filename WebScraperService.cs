using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using System.ComponentModel;

namespace WebScraper
{
    public class WebScraperService
    {
        private readonly WebScraper _scraper;

        public event EventHandler<ProgressChangedEventArgs>? ProgressChanged;
        public event EventHandler<StatusChangedEventArgs>? StatusChanged;
        public event EventHandler<LogMessageEventArgs>? LogMessage;
        public event EventHandler<FoundChangedEventArgs>? FoundChanged;
        public event EventHandler<DownloadedChangedEventArgs>? DownloadedChanged;
        public event EventHandler<TotalAmountChangedEventArgs>? TotalAmountChanged;

        public WebScraperService()
        {
            _scraper = new WebScraper();
        }

        public async Task StartScrapingAsync(AppConfig config, string pageType, int pageSize, CancellationToken cancellationToken)
        {
            try
            {
                OnStatusChanged("Başlatılıyor", "Web scraper başlatılıyor...", StatusType.Processing);
                OnLogMessage("Web scraper başlatılıyor...");

                await _scraper.ScrapeAndDownloadAsync(
                    config.AutoLogin.Username,
                    config.AutoLogin.Password,
                    config.AutoLogin.CompanyCode,
                    config.AutoLogin.TotpSecret,
                    config.Scraping.CssClass,
                    config.Scraping.StatusClass,
                    config,
                    pageType,
                    pageSize,
                    cancellationToken,
                    OnProgressChanged,
                    OnStatusChanged,
                    OnLogMessage,
                    OnFoundChanged,
                    OnDownloadedChanged,
                    OnTotalAmountChanged
                );

                OnStatusChanged("Tamamlandı", "Scraping işlemi başarıyla tamamlandı.", StatusType.Success);
                OnLogMessage("Scraping işlemi tamamlandı.");
            }
            catch (Exception ex)
            {
                // Browser kapatma hatalarını sessizce geç
                if (ex.Message.Contains("Target page, context or browser has been closed") ||
                    ex.Message.Contains("browser has been closed") ||
                    ex.Message.Contains("context has been closed"))
                {
                    OnStatusChanged("Durduruldu", "Scraping işlemi durduruldu.", StatusType.Warning);
                    OnLogMessage("Scraping işlemi durduruldu.");
                    return; // Hata fırlatma, sessizce bitir
                }
                
                OnStatusChanged("Hata", "Scraping işlemi sırasında hata oluştu.", StatusType.Error);
                OnLogMessage("Scraping işlemi sırasında hata oluştu.");
                throw;
            }
        }

        public async Task StartDraftApprovalAsync(AppConfig config, string pageType, int pageSize, CancellationToken cancellationToken)
        {
            try
            {
                OnStatusChanged("Başlatılıyor", "Taslak onaylama işlemi başlatılıyor...", StatusType.Processing);
                OnLogMessage("Taslak onaylama işlemi başlatılıyor...");

                await _scraper.ApproveDraftItemsAsync(
                    config.AutoLogin.Username,
                    config.AutoLogin.Password,
                    config.AutoLogin.CompanyCode,
                    config.AutoLogin.TotpSecret,
                    config,
                    pageType,
                    pageSize,
                    cancellationToken,
                    OnStatusChanged,
                    OnLogMessage,
                    OnProgressChanged,
                    OnFoundChanged,
                    OnDownloadedChanged,
                    OnTotalAmountChanged
                );

                OnStatusChanged("Tamamlandı", "Taslak onaylama işlemi başarıyla tamamlandı.", StatusType.Success);
                OnLogMessage("Taslak onaylama işlemi tamamlandı.");
            }
            catch (Exception ex)
            {
                // Browser kapatma hatalarını sessizce geç
                if (ex.Message.Contains("Target page, context or browser has been closed") ||
                    ex.Message.Contains("browser has been closed") ||
                    ex.Message.Contains("context has been closed"))
                {
                    OnStatusChanged("Durduruldu", "Taslak onaylama işlemi durduruldu.", StatusType.Warning);
                    OnLogMessage("Taslak onaylama işlemi durduruldu.");
                    return; // Hata fırlatma, sessizce bitir
                }
                
                OnStatusChanged("Hata", "Taslak onaylama işlemi sırasında hata oluştu.", StatusType.Error);
                OnLogMessage("Taslak onaylama işlemi sırasında hata oluştu.");
                throw;
            }
        }

        public async Task StartPaymentOrderCreationAsync(AppConfig config, CancellationToken cancellationToken)
        {
            try
            {
                OnStatusChanged("Başlatılıyor", "Ödeme emri oluşturma işlemi başlatılıyor...", StatusType.Processing);
                OnLogMessage("Ödeme emri oluşturma işlemi başlatılıyor...");

                await _scraper.CreatePaymentOrdersAsync(
                    "https://pinhuman.net",
                    config.AutoLogin.Username,
                    config.AutoLogin.Password,
                    config.AutoLogin.CompanyCode,
                    config.AutoLogin.TotpSecret,
                    config,
                    cancellationToken,
                    OnStatusChanged,
                    OnLogMessage
                );

                OnStatusChanged("Tamamlandı", "Ödeme emri oluşturma işlemi başarıyla tamamlandı.", StatusType.Success);
                OnLogMessage("Ödeme emri oluşturma işlemi tamamlandı.");
            }
            catch (Exception ex)
            {
                // Browser kapatma hatalarını sessizce geç
                if (ex.Message.Contains("Target page, context or browser has been closed") ||
                    ex.Message.Contains("browser has been closed") ||
                    ex.Message.Contains("context has been closed"))
                {
                    OnStatusChanged("Durduruldu", "Ödeme emri oluşturma işlemi durduruldu.", StatusType.Warning);
                    OnLogMessage("Ödeme emri oluşturma işlemi durduruldu.");
                    return; // Hata fırlatma, sessizce bitir
                }
                
                OnStatusChanged("Hata", "Ödeme emri oluşturma işlemi sırasında hata oluştu.", StatusType.Error);
                OnLogMessage("Ödeme emri oluşturma işlemi sırasında hata oluştu.");
                throw;
            }
        }

        public void ExportReport(string filePath)
        {
            try
            {
                _scraper.CreateExcelFile(filePath);
                OnLogMessage($"Rapor oluşturuldu: {filePath}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Rapor oluşturma hatası: {ex.Message}");
                throw;
            }
        }

        public void ForceStopBrowser()
        {
            try
            {
                _scraper.ForceCloseBrowser();
                OnLogMessage("Chrome tarayıcısı zorla kapatıldı.");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Browser kapatılırken hata: {ex.Message}");
            }
        }

        private void OnProgressChanged(int progress, int total)
        {
            ProgressChanged?.Invoke(this, new ProgressChangedEventArgs(progress, total));
        }

        private void OnStatusChanged(string status, string detail, StatusType statusType)
        {
            StatusChanged?.Invoke(this, new StatusChangedEventArgs
            {
                Status = status,
                Detail = detail,
                StatusType = statusType
            });
        }

        private void OnLogMessage(string message)
        {
            LogMessage?.Invoke(this, new LogMessageEventArgs { Message = message });
        }

        private void OnFoundChanged(int foundCount)
        {
            FoundChanged?.Invoke(this, new FoundChangedEventArgs { FoundCount = foundCount });
        }

        private void OnDownloadedChanged(int downloadedCount)
        {
            DownloadedChanged?.Invoke(this, new DownloadedChangedEventArgs { DownloadedCount = downloadedCount });
        }

        private void OnTotalAmountChanged(decimal totalAmount)
        {
            TotalAmountChanged?.Invoke(this, new TotalAmountChangedEventArgs { TotalAmount = totalAmount });
        }
    }
} 