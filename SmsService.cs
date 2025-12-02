
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;
using System.Linq;
using OtpNet;

namespace WebScraper
{
    public class SmsService
    {
        public event EventHandler<LogMessageEventArgs>? LogMessage;
        public event EventHandler<string>? StatusChanged;

        private IBrowser? _browser;
        private IBrowserContext? _context;
        private IPage? _page;
        private string _baseUrl = "https://www.pinhuman.net"; // Varsayılan URL

        public SmsService()
        {
        }

        public SmsService(string baseUrl)
        {
            _baseUrl = baseUrl;
        }



        public async Task<List<PeriodInfo>> LoadPeriodsFromWebAsync()
        {
            try
            {
                OnStatusChanged("Tarayıcı başlatılıyor...");
                OnLogMessage("Dönemler yükleniyor...");

                await InitializeBrowserAsync();
                await LoginToSystemAsync();

                OnStatusChanged("Dönem sayfasına gidiliyor...");
                await NavigateToReceiptPeriodAsync();

                var periods = await ExtractPeriodsFromPageAsync();
                
                OnStatusChanged("Dönemler yüklendi");
                OnLogMessage($"{periods.Count} dönem bulundu.");
                
                return periods;
            }
            catch (Exception ex)
            {
                OnStatusChanged("Dönem yükleme hatası");
                OnLogMessage($"Dönemler yüklenirken hata: {ex.Message}");
                throw;
            }
            // Tarayıcıyı kapatma - kullanıcı seçim yapana kadar açık kalacak
        }

        /// <summary>
        /// Belirli bir dönem için SMS alıcılarını getirir (SMS göndermeden) - Sadece belirli dönem için
        /// </summary>
        public async Task<List<SmsRecipientInfo>> GetSmsRecipientsForPeriodAsync(PeriodInfo period)
        {
            try
            {
                OnStatusChanged($"{period.Name} dönemi için SMS alıcıları getiriliyor...");
                OnLogMessage($"{period.Name} dönemi için SMS alıcıları alınıyor...");

                // Eğer tarayıcı açık değilse, yeni bir tarayıcı başlat
                if (_browser == null || _page == null)
                {
                    await InitializeBrowserAsync();
                    await LoginToSystemAsync();
                    await NavigateToReceiptPeriodAsync();
                }

                // Sadece belirli dönem için SMS alıcılarını getir
                var recipients = await GetSmsRecipientsForSpecificPeriodAsync(period);
                
                OnStatusChanged($"{period.Name} dönemi için {recipients.Count} alıcı bulundu");
                OnLogMessage($"{period.Name} dönemi için {recipients.Count} SMS alıcısı bulundu.");
                
                return recipients;
            }
            catch (Exception ex)
            {
                OnStatusChanged($"{period.Name} dönemi alıcı getirme hatası");
                OnLogMessage($"{period.Name} dönemi için SMS alıcıları alınırken hata: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Belirli bir dönem için SMS alıcılarını getirir - Sadece o dönem için sekme açar
        /// </summary>
        private async Task<List<SmsRecipientInfo>> GetSmsRecipientsForSpecificPeriodAsync(PeriodInfo period)
        {
            try
            {
                OnLogMessage($"{period.Name} dönemi için SMS alıcıları alınıyor...");
                
                // Sadece belirli dönem için direkt URL ile alıcıları al
                var recipients = await GetSmsRecipientsViaDirectUrlAsync(period.Id);
                
                // Dönem adını alıcılara ekle
                foreach (var recipient in recipients)
                {
                    recipient.PeriodName = period.Name;
                }
                
                OnLogMessage($"{period.Name} dönemi için {recipients.Count} alıcı bulundu.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"{period.Name} dönemi için SMS alıcıları alınırken hata: {ex.Message}");
                return new List<SmsRecipientInfo>();
            }
        }

        /// <summary>
        /// Mevcut sayfa için SMS alıcılarını getirir - TÜM dönemler için ayrı sekmeler açıp kapatır
        /// </summary>
        private async Task<List<SmsRecipientInfo>> GetSmsRecipientsForCurrentPageAsync()
        {
            try
            {
                OnLogMessage("Mevcut sayfadan SMS alıcıları alınıyor...");
                
                var recipients = new List<SmsRecipientInfo>();
                
                // Sayfanın yüklenmesini bekle
                await _page!.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await _page.WaitForTimeoutAsync(300);
                
                // Dropdown menüdeki SMS gönderim linklerini bul
                var smsLinks = await _page.QuerySelectorAllAsync("a[href*='/AgcServiceRecieptPeriod/SendSmsToEngineers/']");
                OnLogMessage($"{smsLinks.Count} SMS gönderim linki bulundu.");
                
                if (!smsLinks.Any())
                {
                    OnLogMessage("Hiç SMS linki bulunamadı.");
                    return recipients;
                }
                
                // TÜM SMS linklerini işle (sadece ilk değil)
                var processedCount = 0;
                foreach (var smsLink in smsLinks)
                {
                    try
                    {
                        processedCount++;
                        OnLogMessage($"SMS link {processedCount}/{smsLinks.Count} işleniyor...");
                        
                        // Link'in href'ini al
                        var href = await smsLink.GetAttributeAsync("href");
                        if (string.IsNullOrEmpty(href))
                        {
                            OnLogMessage($"SMS link {processedCount} için href bulunamadı.");
                            continue;
                        }
                        
                        var periodId = ExtractPeriodIdFromHref(href);
                        if (string.IsNullOrEmpty(periodId))
                        {
                            OnLogMessage($"SMS link {processedCount} için dönem ID'si çıkarılamadı.");
                            continue;
                        }
                        
                        OnLogMessage($"Dönem {periodId} için SMS alıcıları alınıyor... (Link {processedCount}/{smsLinks.Count})");
                        
                        // Aynı context'te yeni sekme aç ve HTML'den alıcıları çıkar
                        var modalRecipients = await GetSmsRecipientsViaDirectUrlAsync(periodId);
                        
                        if (modalRecipients.Any())
                        {
                            recipients.AddRange(modalRecipients);
                            OnLogMessage($"Dönem {periodId} için {modalRecipients.Count} alıcı bulundu. (Toplam: {recipients.Count})");
                        }
                        else
                        {
                            OnLogMessage($"Dönem {periodId} için alıcı bulunamadı.");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"SMS link {processedCount} işlenirken hata: {ex.Message}");
                        continue; // Hata olsa bile diğer linkleri işlemeye devam et
                    }
                }
                
                OnLogMessage($"Tüm SMS linkleri işlendi. Toplam {recipients.Count} SMS alıcısı bulundu.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"SMS alıcıları alınırken hata: {ex.Message}");
                return new List<SmsRecipientInfo>();
            }
        }

        /// <summary>
        /// Sıralı olarak dönemlerden SMS alıcılarını alır (tekrarlı veri olmaz)
        /// </summary>
        public async Task<List<SmsRecipientInfo>> GetSmsRecipientsSequentialAsync(List<string> periodIds)
        {
            try
            {
                OnLogMessage($"Sıralı olarak {periodIds.Count} dönemden SMS alıcıları alınıyor...");
                
                var allRecipients = new List<SmsRecipientInfo>();
                var processedCount = 0;
                
                foreach (var periodId in periodIds)
                {
                    processedCount++;
                    OnLogMessage($"Dönem {processedCount}/{periodIds.Count} işleniyor...");
                    
                    try
                    {
                        var recipients = await GetSmsRecipientsViaDirectUrlAsync(periodId);
                        allRecipients.AddRange(recipients);
                        
                        OnLogMessage($"Dönem {processedCount} tamamlandı: {recipients.Count} alıcı eklendi.");
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"Dönem {processedCount} işlenirken hata: {ex.Message}");
                        continue; // Hata olsa bile diğer dönemlere devam et
                    }
                }
                
                OnLogMessage($"Sıralı işlem tamamlandı. Toplam {allRecipients.Count} alıcı bulundu.");
                return allRecipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Sıralı SMS alıcı alma hatası: {ex.Message}");
                return new List<SmsRecipientInfo>();
            }
        }

        /// <summary>
        /// Direkt URL'ye aynı context'te yeni sekmede gidip HTML'den SMS alıcılarını alır (çok daha hızlı)
        /// </summary>
        private async Task<List<SmsRecipientInfo>> GetSmsRecipientsViaDirectUrlAsync(string periodId)
        {
            IPage? newPage = null;
            try
            {
                OnLogMessage($"Aynı oturumda yeni sekmede SMS alıcıları alınıyor... (Dönem: {periodId})");
                
                // Aynı context'te yeni sekme oluştur (session'ı paylaşır, login gerekmez)
                // Playwright'ın yeni versiyonunda doğru yöntem
                newPage = await _context!.NewPageAsync();
                
                // Direkt SMS URL'sine git (yeni sekmede, aynı session) - sabit URL kullan
                var smsUrl = $"https://www.pinhuman.net/AgcServiceRecieptPeriod/SendSmsToEngineers/{periodId}";
                await newPage.GotoAsync(smsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                
                // SMS sayfası açıldığında sekme aktif hale getir
                try
                {
                    await newPage.BringToFrontAsync();
                    OnLogMessage("SMS sekmesi aktif hale getirildi.");
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Sekme aktif hale getirme hatası: {ex.Message}");
                }
                
                // Sayfanın yüklenmesini bekle
                await newPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await newPage.WaitForTimeoutAsync(500);
                
                // SMS alıcıları sayfası açıldı - kullanıcı seçim yapacak
                OnLogMessage($"✅ SMS alıcıları sayfası açıldı (Dönem ID: {periodId})");
                OnLogMessage("📋 Lütfen alıcıları seçin ve 'Gönder' butonuna tıklayın.");
                
                // HTML'den alıcıları çıkar (yeni sekmeden)
                var recipients = await ExtractRecipientsFromHtmlAsync(newPage, periodId);
                
                OnLogMessage($"Aynı oturumda yeni sekmeden {recipients.Count} alıcı çıkarıldı.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Aynı oturum yeni sekme ile alıcı alma hatası: {ex.Message}");
                return new List<SmsRecipientInfo>();
            }
            finally
            {
                // SMS alıcıları alındıktan sonra sekmeyi kapat
                if (newPage != null)
                {
                    try
                    {
                        await newPage.CloseAsync();
                        OnLogMessage("✅ SMS alıcıları sekmesi kapatıldı.");
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"Sekme kapatma hatası: {ex.Message}");
                    }
                }
            }
        }

                /// <summary>
        /// HTML'den SMS alıcılarını çıkarır
        /// </summary>
        private async Task<List<SmsRecipientInfo>> ExtractRecipientsFromHtmlAsync(IPage? page = null, string periodName = "")
        {
            try
            {
                var targetPage = page ?? _page!;
                var recipients = new List<SmsRecipientInfo>();
                
                // Farklı HTML yapılarını dene
                var tableRows = await targetPage.QuerySelectorAllAsync("table tbody tr, .table tbody tr, tbody tr");
                OnLogMessage($"HTML'de {tableRows.Count} satır bulundu.");
                
                if (!tableRows.Any())
                {
                    OnLogMessage("HTML'de hiç satır bulunamadı, alternatif yapılar deneniyor...");
                    
                    // Alternatif: Tüm tabloları bul
                    var allTables = await targetPage.QuerySelectorAllAsync("table, .table");
                    OnLogMessage($"Sayfada {allTables.Count} tablo bulundu.");
                    
                    foreach (var table in allTables)
                    {
                        var rows = await table.QuerySelectorAllAsync("tr");
                        OnLogMessage($"Tablo'da {rows.Count} satır bulundu.");
                        
                        if (rows.Count > 1) // Header + data rows
                        {
                            tableRows = rows.Skip(1).ToArray(); // Header'ı atla
                            OnLogMessage($"Alternatif tablo kullanılıyor: {tableRows.Count()} satır");
                            break;
                        }
                    }
                }
                
                if (!tableRows.Any())
                {
                    OnLogMessage("Hiçbir tablo satırı bulunamadı.");
                    return recipients;
                }
                
                foreach (var row in tableRows)
                {
                    try
                    {
                        // Satırdaki hücreleri al
                        var cells = await row.QuerySelectorAllAsync("td");
                        OnLogMessage($"HTML satırda {cells.Count} hücre bulundu.");
                        
                        if (cells.Count < 2) continue; // En az 2 hücre olmalı: adı, telefon
                        
                        // Farklı sütun yapılarını dene
                        string name = "";
                        string phone = "";
                        string id = "";
                        
                        // Checkbox'tan ID'yi al
                        var checkbox = await row.QuerySelectorAsync("input[name='SelectedIds'], input[type='checkbox']");
                        if (checkbox != null)
                        {
                            id = await checkbox.GetAttributeAsync("value") ?? "";
                        }
                        
                        // İsim ve telefon için farklı sütun kombinasyonlarını dene
                        if (cells.Count >= 3)
                        {
                            // Standart format: checkbox, isim, telefon
                            name = await cells[1].InnerTextAsync();
                            phone = await cells[2].InnerTextAsync();
                        }
                        else if (cells.Count >= 2)
                        {
                            // Basit format: isim, telefon
                            name = await cells[0].InnerTextAsync();
                            phone = await cells[1].InnerTextAsync();
                        }
                        
                        // Telefon numarasını temizle
                        phone = new string(phone.Where(char.IsDigit).ToArray());
                        
                        // Geçerli veri kontrolü
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(phone) && 
                            phone.Length >= 10)
                        {
                            recipients.Add(new SmsRecipientInfo
                            {
                                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                                Name = name.Trim(),
                                Phone = phone.Trim(),
                                PeriodName = periodName,
                                IsSelected = true
                            });
                            
                            OnLogMessage($"✅ HTML Alıcı bulundu: {name.Trim()} - {phone.Trim()} (Dönem: {periodName})");
                        }
                        else
                        {
                            OnLogMessage($"❌ HTML Geçersiz veri: İsim='{name}', Telefon='{phone}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"HTML satır işlenirken hata: {ex.Message}");
                        continue;
                    }
                }
                
                OnLogMessage($"HTML'den {recipients.Count} alıcı çıkarıldı.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"HTML'den alıcı çıkarma hatası: {ex.Message}");
                return new List<SmsRecipientInfo>();
            }
        }

        /// <summary>
        /// Direkt linke tıklayarak SMS alıcılarını alır (daha güvenilir)
        /// </summary>
        private async Task<List<SmsRecipientInfo>> GetSmsRecipientsViaDirectClickAsync(IElementHandle smsLink, string periodId)
        {
            try
            {
                OnLogMessage($"SMS linkine direkt tıklanıyor... (Dönem: {periodId})");
                
                // Link'in görünür ve tıklanabilir olduğundan emin ol
                await smsLink.WaitForElementStateAsync(ElementState.Visible);
                await smsLink.WaitForElementStateAsync(ElementState.Enabled);
                
                // Sayfanın stabil olmasını bekle
                await _page!.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await _page.WaitForTimeoutAsync(500);
                
                // Önce dropdown-toggle-split butonunu bul ve aç
                OnLogMessage("Dropdown-toggle-split butonu aranıyor...");
                
                // SMS linkinin parent'larında dropdown-toggle-split butonunu ara
                var dropdownToggle = await _page.EvaluateAsync<IElementHandle>(@"
                    (function() {
                        function findDropdownToggle(element) {
                            let current = element;
                            while (current && current.parentElement) {
                                // Özellikle dropdown-toggle-split butonunu ara
                                let toggle = current.querySelector('.dropdown-toggle-split[data-toggle=""dropdown""], .btn-outline-secondary.dropdown-toggle-split');
                                if (toggle) return toggle;
                                
                                // Genel dropdown toggle butonlarını da ara
                                toggle = current.querySelector('.dropdown-toggle[data-toggle=""dropdown""], button[data-toggle=""dropdown""]');
                                if (toggle) return toggle;
                                
                                // Bir üst parent'a git
                                current = current.parentElement;
                            }
                            return null;
                        }
                        return findDropdownToggle(arguments[0]);
                    })();
                ", smsLink);
                
                // Eğer bulunamazsa, sayfada genel arama yap
                if (dropdownToggle == null)
                {
                    OnLogMessage("Parent'ta dropdown toggle bulunamadı, sayfada genel arama yapılıyor...");
                    dropdownToggle = await _page.QuerySelectorAsync("button.dropdown-toggle-split[data-toggle='dropdown'], .btn-outline-secondary.dropdown-toggle-split");
                }
                
                // JavaScript ile daha agresif arama
                if (dropdownToggle == null)
                {
                    OnLogMessage("Genel aramada da bulunamadı, JavaScript ile agresif arama yapılıyor...");
                    dropdownToggle = await _page.EvaluateAsync<IElementHandle>(@"
                        (function() {
                            // Önce dropdown-toggle-split butonlarını ara
                            let toggles = document.querySelectorAll('.dropdown-toggle-split[data-toggle=""dropdown""], .btn-outline-secondary.dropdown-toggle-split');
                            if (toggles.length > 0) {
                                return toggles[0];
                            }
                            
                            // Sonra genel dropdown toggle butonlarını ara
                            toggles = document.querySelectorAll('button[data-toggle=""dropdown""], .dropdown-toggle');
                            if (toggles.length > 0) {
                                return toggles[0];
                            }
                            return null;
                        })();
                    ");
                }
                
                if (dropdownToggle != null)
                {
                    OnLogMessage("Dropdown toggle butonu bulundu, açılıyor...");
                    
                    // Dropdown menüyü aç
                    await dropdownToggle.ClickAsync(new ElementHandleClickOptions
                    {
                        Delay = 200,
                        Force = false,
                        NoWaitAfter = false
                    });
                    
                    await _page.WaitForTimeoutAsync(500);
                    
                    // Dropdown'ın açılıp açılmadığını kontrol et
                    var dropdownMenu = await _page.QuerySelectorAsync(".dropdown-menu.show, .dropdown-menu[style*='display: block']");
                    if (dropdownMenu == null)
                    {
                        OnLogMessage("Dropdown açılmadı, JavaScript ile tıklama deneniyor...");
                        await _page.EvaluateAsync(@"
                            (function() {
                                let toggle = arguments[0];
                                if (toggle) {
                                    toggle.click();
                                    // Bootstrap dropdown'ı manuel olarak aç
                                    toggle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                                    toggle.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
                                }
                            })();
                        ", dropdownToggle);
                        
                        await _page.WaitForTimeoutAsync(500);
                    }
                    
                    // Şimdi SMS linkine tıkla
                    OnLogMessage("Dropdown açıldı, SMS linkine tıklanıyor...");
                    
                    // SMS linkini dropdown menüsü içinde bul
                    var smsLinkInDropdown = await _page.QuerySelectorAsync("a[href*='/AgcServiceRecieptPeriod/SendSmsToEngineers/']");
                    if (smsLinkInDropdown != null)
                    {
                        await smsLinkInDropdown.ClickAsync(new ElementHandleClickOptions
                        {
                            Delay = 200,
                            Force = false,
                            NoWaitAfter = false
                        });
                }
                else
                {
                        // Orijinal linke tıkla
                        await smsLink.ClickAsync(new ElementHandleClickOptions
                        {
                            Delay = 200,
                            Force = false,
                            NoWaitAfter = false
                        });
                    }
                }
                else
                {
                    OnLogMessage("Dropdown toggle bulunamadı, orijinal linke tıklanıyor...");
                    await smsLink.ClickAsync(new ElementHandleClickOptions
                    {
                        Delay = 200,
                        Force = false,
                        NoWaitAfter = false
                    });
                }
                
                // Modal'ın açılıp açılmadığını kontrol et
                var modal = await _page.QuerySelectorAsync(".modal.show, .modal[style*='display: block'], .modal-dialog");
                if (modal == null)
                {
                    OnLogMessage("Modal açılmadı, alternatif yöntem deneniyor...");
                    
                    // Alternatif: JavaScript ile tıkla
                    await _page.EvaluateAsync(@"
                        (function() {
                            let link = arguments[0];
                            if (link) {
                                link.click();
                            }
                        })();
                    ", smsLink);
                    
                    await _page.WaitForTimeoutAsync(1000);
                    
                    modal = await _page.QuerySelectorAsync(".modal.show, .modal[style*='display: block'], .modal-dialog");
                    if (modal == null)
                    {
                        OnLogMessage("Modal hala açılmadı, işlem iptal ediliyor.");
                        return new List<SmsRecipientInfo>();
                    }
                }
                
                OnLogMessage("Modal başarıyla açıldı, alıcılar alınıyor...");
                
                // Modal içindeki alıcıları al
                var recipients = await ExtractRecipientsFromModalAsync();
                
                // Modal'ı kapat
                await CloseModalAsync();
                
                OnLogMessage($"Modal'dan {recipients.Count} alıcı çıkarıldı.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Direkt tıklama ile alıcı alma hatası: {ex.Message}");
                
                // Hata durumunda modal'ı kapatmaya çalış
                try
                {
                    await CloseModalAsync();
                }
                catch
                {
                    // Modal kapatma hatası olursa görmezden gel
                }
                
                return new List<SmsRecipientInfo>();
            }
        }

        /// <summary>
        /// Dropdown menüyü açıp SMS linkine tıklayarak alıcıları alır (yeni sekmede)
        /// </summary>
        private async Task<List<SmsRecipientInfo>> GetSmsRecipientsViaDropdownAsync(IElementHandle smsLink, string periodId)
        {
            // Timeout için CancellationToken oluştur
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30)); // 30 saniye timeout
            
            try
            {
                OnLogMessage($"Dropdown menü açılıyor ve SMS linkine tıklanıyor... (Dönem: {periodId})");
                
                // Önce dropdown toggle butonunu bul (daha güvenli yöntem)
                IElementHandle? dropdownToggle = null;
                
                try
                {
                    // Yöntem 1: SMS linkinin parent'larında dropdown toggle'ı ara
                    dropdownToggle = await smsLink.EvaluateAsync<IElementHandle>(@"
                        (function() {
                            function findDropdownToggle(element) {
                                // Element'in kendisinden başlayarak yukarı doğru git
                                let current = element;
                                while (current && current.parentElement) {
                                    // Dropdown toggle butonunu ara (verilen yapıya uygun)
                                    let toggle = current.querySelector('.dropdown-toggle-split, .dropdown-toggle, .btn-outline-secondary[data-toggle=""dropdown""], button[data-toggle=""dropdown""]');
                                    if (toggle) return toggle;
                                    
                                    // Bir üst parent'a git
                                    current = current.parentElement;
                                }
                                return null;
                            }
                            return findDropdownToggle(arguments[0]);
                        })();
                    ");
                    
                    // Yöntem 2: Eğer bulunamazsa, sayfada genel arama yap
                    if (dropdownToggle == null)
                    {
                        OnLogMessage("Parent'ta dropdown toggle bulunamadı, sayfada genel arama yapılıyor...");
                        dropdownToggle = await _page!.QuerySelectorAsync("button.dropdown-toggle-split[data-toggle='dropdown'], button.btn-outline-secondary[data-toggle='dropdown']");
                    }
                    
                    // Yöntem 3: JavaScript ile daha agresif arama
                    if (dropdownToggle == null)
                    {
                        OnLogMessage("Genel aramada da bulunamadı, JavaScript ile agresif arama yapılıyor...");
                        dropdownToggle = await _page!.EvaluateAsync<IElementHandle>(@"
                            (function() {
                                // Tüm dropdown toggle butonlarını bul
                                let toggles = document.querySelectorAll('button[data-toggle=""dropdown""], .dropdown-toggle-split, .dropdown-toggle');
                                if (toggles.length > 0) {
                                    // İlk bulunan toggle'ı döndür
                                    return toggles[0];
                                }
                                return null;
                            })();
                        ");
                    }
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Dropdown toggle bulma hatası: {ex.Message}");
                }
                
                if (dropdownToggle != null)
                {
                    // Dropdown menüyü aç (daha güvenilir yöntem)
                    OnLogMessage("Dropdown toggle butonuna tıklanıyor...");
                    
                    try
                    {
                        // Önce butonun çevresinde renk değişikliği yap (görsel feedback)
                        await _page!.EvaluateAsync(@"
                            (function() {
                                let button = arguments[0];
                                if (button) {
                                    // Orijinal stilleri kaydet
                                    button._originalBorder = button.style.border;
                                    button._originalBoxShadow = button.style.boxShadow;
                                    
                                    // Görsel feedback için renk değişikliği
                                    button.style.border = '3px solid #FF6B35';
                                    button.style.boxShadow = '0 0 10px #FF6B35';
                                    button.style.transition = 'all 0.3s ease';
                                    
                                    // 1 saniye bekle
                                    setTimeout(() => {
                                        // Orijinal stilleri geri yükle
                                        button.style.border = button._originalBorder;
                                        button.style.boxShadow = button._originalBoxShadow;
                                    }, 1000);
                                }
                            })();
                        ", dropdownToggle);
                        
                        // Yöntem 1: Normal tıklama
                        await dropdownToggle.ClickAsync(new ElementHandleClickOptions
                        {
                            Delay = 100,
                            Force = false,
                            NoWaitAfter = false,
                            Position = new Position { X = 5, Y = 5 }
                        });
                        
                        await _page!.WaitForTimeoutAsync(500);
                        cts.Token.ThrowIfCancellationRequested();
                        
                        // Yöntem 2: Eğer dropdown açılmadıysa, JavaScript ile tıkla
                        var dropdownMenu = await _page.QuerySelectorAsync(".dropdown-menu.show, .dropdown-menu[style*='display: block']");
                        if (dropdownMenu == null)
                        {
                            OnLogMessage("Dropdown açılmadı, JavaScript ile tıklama deneniyor...");
                            await _page.EvaluateAsync(@"
                                (function() {
                                    let toggle = arguments[0];
                                    if (toggle) {
                                        // Bootstrap dropdown'ı manuel olarak aç
                                        toggle.click();
                                        // Alternatif olarak mousedown event'i tetikle
                                        toggle.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
                                        toggle.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
                                    }
                                })();
                            ", dropdownToggle);
                            
                            await _page.WaitForTimeoutAsync(500);
                            cts.Token.ThrowIfCancellationRequested();
                        }
                        
                        // Yöntem 3: Enter tuşu ile açma
                        dropdownMenu = await _page.QuerySelectorAsync(".dropdown-menu.show, .dropdown-menu[style*='display: block']");
                        if (dropdownMenu == null)
                        {
                            OnLogMessage("JavaScript ile de açılmadı, Enter tuşu deneniyor...");
                            await dropdownToggle.FocusAsync();
                            await _page.Keyboard.PressAsync("Enter");
                            await _page.WaitForTimeoutAsync(500);
                            cts.Token.ThrowIfCancellationRequested();
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"Dropdown toggle tıklama hatası: {ex.Message}");
                    }
                    
                    // SMS linkine tıkla (daha güvenilir)
                    OnLogMessage("SMS linkine tıklanıyor...");
                    
                    try
                    {
                        // Dropdown menüsünün açık olduğundan emin ol
                        var dropdownMenu = await _page.QuerySelectorAsync(".dropdown-menu.show, .dropdown-menu[style*='display: block']");
                        if (dropdownMenu != null)
                        {
                            OnLogMessage("Dropdown menüsü açık, SMS linkine tıklanıyor...");
                            
                            // SMS linkini dropdown menüsü içinde bul
                            var smsLinkInDropdown = await dropdownMenu.QuerySelectorAsync("a[href*='/AgcServiceRecieptPeriod/SendSmsToEngineers/']");
                            if (smsLinkInDropdown != null)
                            {
                                // SMS linkine görsel feedback ekle
                                await _page!.EvaluateAsync(@"
                                    (function() {
                                        let link = arguments[0];
                                        if (link) {
                                            // Orijinal stilleri kaydet
                                            link._originalBackground = link.style.background;
                                            link._originalColor = link.style.color;
                                            link._originalBoxShadow = link.style.boxShadow;
                                            
                                            // Görsel feedback için renk değişikliği
                                            link.style.background = '#4CAF50';
                                            link.style.color = 'white';
                                            link.style.boxShadow = '0 0 10px #4CAF50';
                                            link.style.transition = 'all 0.3s ease';
                                            
                                            // 1 saniye bekle
                                            setTimeout(() => {
                                                // Orijinal stilleri geri yükle
                                                link.style.background = link._originalBackground;
                                                link.style.color = link._originalColor;
                                                link.style.boxShadow = link._originalBoxShadow;
                                            }, 1000);
                                        }
                                    })();
                                ", smsLinkInDropdown);
                                
                                // 1 saniye bekle (görsel feedback için)
                                await _page.WaitForTimeoutAsync(1000);
                                cts.Token.ThrowIfCancellationRequested();
                                
                                await smsLinkInDropdown.ClickAsync(new ElementHandleClickOptions
                                {
                                    Delay = 100,
                                    Force = false,
                                    NoWaitAfter = false
                                });
                            }
                            else
                            {
                                // Orijinal linke görsel feedback ekle
                                await _page!.EvaluateAsync(@"
                                    (function() {
                                        let link = arguments[0];
                                        if (link) {
                                            // Orijinal stilleri kaydet
                                            link._originalBackground = link.style.background;
                                            link._originalColor = link.style.color;
                                            link._originalBoxShadow = link.style.boxShadow;
                                            
                                            // Görsel feedback için renk değişikliği
                                            link.style.background = '#4CAF50';
                                            link.style.color = 'white';
                                            link.style.boxShadow = '0 0 10px #4CAF50';
                                            link.style.transition = 'all 0.3s ease';
                                            
                                            // 1 saniye bekle
                                            setTimeout(() => {
                                                // Orijinal stilleri geri yükle
                                                link.style.background = link._originalBackground;
                                                link.style.color = link._originalColor;
                                                link.style.boxShadow = link._originalBoxShadow;
                                            }, 1000);
                                        }
                                    })();
                                ", smsLink);
                                
                                // 1 saniye bekle (görsel feedback için)
                                await _page.WaitForTimeoutAsync(1000);
                                cts.Token.ThrowIfCancellationRequested();
                                
                                await smsLink.ClickAsync(new ElementHandleClickOptions
                                {
                                    Delay = 100,
                                    Force = false,
                                    NoWaitAfter = false
                                });
                            }
                        }
                        else
                        {
                            OnLogMessage("Dropdown menüsü açık değil, orijinal linke tıklanıyor...");
                            
                            // Orijinal linke görsel feedback ekle
                            await _page!.EvaluateAsync(@"
                                (function() {
                                    let link = arguments[0];
                                    if (link) {
                                        // Orijinal stilleri kaydet
                                        link._originalBackground = link.style.background;
                                        link._originalColor = link.style.color;
                                        link._originalBoxShadow = link.style.boxShadow;
                                        
                                        // Görsel feedback için renk değişikliği
                                        link.style.background = '#4CAF50';
                                        link.style.color = 'white';
                                        link.style.boxShadow = '0 0 10px #4CAF50';
                                        link.style.transition = 'all 0.3s ease';
                                        
                                        // 1 saniye bekle
                                        setTimeout(() => {
                                            // Orijinal stilleri geri yükle
                                            link.style.background = link._originalBackground;
                                            link.style.color = link._originalColor;
                                            link.style.boxShadow = link._originalBoxShadow;
                                        }, 1000);
                                    }
                                })();
                            ", smsLink);
                            
                            // 1 saniye bekle (görsel feedback için)
                            await _page.WaitForTimeoutAsync(1000);
                            cts.Token.ThrowIfCancellationRequested();
                            
                            await smsLink.ClickAsync(new ElementHandleClickOptions
                            {
                                Delay = 100,
                                Force = false,
                                NoWaitAfter = false
                            });
                        }
                        
                        await _page.WaitForTimeoutAsync(1000); // Daha uzun bekleme
                        cts.Token.ThrowIfCancellationRequested();
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"SMS link tıklama hatası: {ex.Message}");
                    }
                    
                    // Modal içindeki alıcıları hızlıca al
                    var recipients = await ExtractRecipientsFromModalAsync();
                    
                    // Modal'ı kapat
                    await CloseModalAsync();
                    
                    OnLogMessage($"Hızlı simülasyon ile {recipients.Count} alıcı alındı.");
                    return recipients;
                }
                else
                {
                    // Dropdown toggle bulunamadı, direkt linke tıkla (hızlı)
                    OnLogMessage("Dropdown toggle bulunamadı, direkt linke hızlıca tıklanıyor...");
                    await smsLink.ClickAsync(new ElementHandleClickOptions
                    {
                        Delay = 50,
                        Force = false,
                        NoWaitAfter = false,
                        Position = new Position { X = 10, Y = 10 }
                    });
                    
                    await _page!.WaitForTimeoutAsync(500);
                    cts.Token.ThrowIfCancellationRequested();
                    
                    var recipients = await ExtractRecipientsFromModalAsync();
                    
                    // Modal'ı kapat
                    await CloseModalAsync();
                    
                    OnLogMessage($"Direkt hızlı simülasyon ile {recipients.Count} alıcı alındı.");
                    return recipients;
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Dropdown ile alıcı alma hatası: {ex.Message}");
                
                // Hata durumunda modal'ı kapatmaya çalış
                try
                {
                    await CloseModalAsync();
                }
                catch
                {
                    // Modal kapatma hatası olursa görmezden gel
                }
                
                return new List<SmsRecipientInfo>();
            }
            finally
            {
                // Timeout kontrolü - cts zaten using ile dispose edilecek
            }
        }

        /// <summary>
        /// Modal açıp hızlıca SMS alıcılarını alır
        /// </summary>
        private async Task<List<SmsRecipientInfo>> GetSmsRecipientsViaModalAsync(IElementHandle eyeButton, string periodId)
        {
            try
            {
                OnLogMessage($"Modal açılıyor ve hızlıca alıcılar alınıyor... (Dönem: {periodId})");
                
                // Modal'ı aç (hızlı)
                await eyeButton.ClickAsync();
                await _page!.WaitForLoadStateAsync(LoadState.DOMContentLoaded); // Daha hızlı
                await _page.WaitForTimeoutAsync(500); // Daha kısa bekleme
                
                // Modal içindeki alıcıları hızlıca al
                var recipients = await ExtractRecipientsFromModalAsync();
                
                // Modal'ı kapat (geri git - hızlı)
                await _page.GoBackAsync();
                await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded); // Daha hızlı
                await _page.WaitForTimeoutAsync(300); // Daha kısa bekleme
                
                OnLogMessage($"Modal ile {recipients.Count} alıcı alındı.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Modal ile alıcı alma hatası: {ex.Message}");
                
                // Hata durumunda geri gitmeye çalış (hızlı)
                try
                {
                    await _page!.GoBackAsync();
                    await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                }
                catch
                {
                    // Geri gitme hatası olursa görmezden gel
                }
                
                return new List<SmsRecipientInfo>();
            }
        }

        /// <summary>
        /// Modal'ı güvenli bir şekilde kapatır
        /// </summary>
        private async Task CloseModalAsync()
        {
            try
            {
                OnLogMessage("Modal kapatılıyor...");
                
                // Yöntem 1: ESC tuşu ile kapat
                await _page!.Keyboard.PressAsync("Escape");
                await _page.WaitForTimeoutAsync(500);
                
                // Yöntem 2: Modal overlay'ine tıkla
                var modalOverlay = await _page.QuerySelectorAsync(".modal-backdrop, .modal-overlay, .modal");
                if (modalOverlay != null)
                {
                    await modalOverlay.ClickAsync();
                    await _page.WaitForTimeoutAsync(500);
                }
                
                // Yöntem 3: Close butonunu bul ve tıkla
                var closeButton = await _page.QuerySelectorAsync(".modal .close, .modal .btn-close, .modal [data-dismiss='modal']");
                if (closeButton != null)
                {
                    await closeButton.ClickAsync();
                    await _page.WaitForTimeoutAsync(500);
                }
                
                // Yöntem 4: JavaScript ile modal'ı kapat
                await _page.EvaluateAsync(@"
                    (function() {
                        // Tüm modal'ları kapat
                        let modals = document.querySelectorAll('.modal');
                        modals.forEach(modal => {
                            if (modal.classList.contains('show')) {
                                modal.classList.remove('show');
                                modal.style.display = 'none';
                            }
                        });
                        
                        // Modal backdrop'ları kaldır
                        let backdrops = document.querySelectorAll('.modal-backdrop');
                        backdrops.forEach(backdrop => {
                            backdrop.remove();
                        });
                        
                        // Body'den modal-open class'ını kaldır
                        document.body.classList.remove('modal-open');
                        document.body.style.overflow = '';
                        document.body.style.paddingRight = '';
                    })();
                ");
                
                await _page.WaitForTimeoutAsync(500);
                
                OnLogMessage("Modal güvenli bir şekilde kapatıldı.");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Modal kapatılırken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Modal'dan SMS alıcılarını çıkarır
        /// </summary>
        private async Task<List<SmsRecipientInfo>> ExtractRecipientsFromModalAsync()
        {
            try
            {
                var recipients = new List<SmsRecipientInfo>();
                
                // Modal'ın açılmasını bekle
                await _page!.WaitForTimeoutAsync(1000);
                
                // Farklı HTML yapılarını dene
                var tableRows = await _page.QuerySelectorAllAsync(".modal-content table tbody tr, .modal table tbody tr, table tbody tr, .table tbody tr, tbody tr");
                
                OnLogMessage($"Modal'da {tableRows.Count} satır bulundu.");
                
                if (!tableRows.Any())
                {
                    OnLogMessage("Modal'da hiç satır bulunamadı, alternatif yapılar deneniyor...");
                    
                    // Alternatif: Tüm tabloları bul
                    var allTables = await _page.QuerySelectorAllAsync("table, .table");
                    OnLogMessage($"Sayfada {allTables.Count} tablo bulundu.");
                    
                    foreach (var table in allTables)
                    {
                        var rows = await table.QuerySelectorAllAsync("tr");
                        OnLogMessage($"Tablo'da {rows.Count} satır bulundu.");
                        
                        if (rows.Count > 1) // Header + data rows
                        {
                            tableRows = rows.Skip(1).ToArray(); // Header'ı atla
                            OnLogMessage($"Alternatif tablo kullanılıyor: {tableRows.Count()} satır");
                            break;
                        }
                    }
                }
                
                if (!tableRows.Any())
                {
                    OnLogMessage("Hiçbir tablo satırı bulunamadı.");
                    return recipients;
                }
                
                foreach (var row in tableRows)
                {
                    try
                    {
                        // Satırdaki hücreleri al
                        var cells = await row.QuerySelectorAllAsync("td");
                        OnLogMessage($"Satırda {cells.Count} hücre bulundu.");
                        
                        if (cells.Count < 2) continue; // En az 2 hücre olmalı: adı, telefon
                        
                        // Farklı sütun yapılarını dene
                        string name = "";
                        string phone = "";
                        string id = "";
                        
                        // Checkbox'tan ID'yi al
                        var checkbox = await row.QuerySelectorAsync("input[name='SelectedIds'], input[type='checkbox']");
                        if (checkbox != null)
                        {
                            id = await checkbox.GetAttributeAsync("value") ?? "";
                        }
                        
                        // İsim ve telefon için farklı sütun kombinasyonlarını dene
                        if (cells.Count >= 3)
                        {
                            // Standart format: checkbox, isim, telefon
                            name = await cells[1].InnerTextAsync();
                            phone = await cells[2].InnerTextAsync();
                        }
                        else if (cells.Count >= 2)
                        {
                            // Basit format: isim, telefon
                            name = await cells[0].InnerTextAsync();
                            phone = await cells[1].InnerTextAsync();
                        }
                        
                        // Telefon numarasını temizle
                        phone = new string(phone.Where(char.IsDigit).ToArray());
                        
                        // Geçerli veri kontrolü
                        if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(phone) && 
                            phone.Length >= 10)
                        {
                            recipients.Add(new SmsRecipientInfo
                            {
                                Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                                Name = name.Trim(),
                                Phone = phone.Trim(),
                                IsSelected = true
                            });
                            
                            OnLogMessage($"✅ Alıcı bulundu: {name.Trim()} - {phone.Trim()}");
                        }
                        else
                        {
                            OnLogMessage($"❌ Geçersiz veri: İsim='{name}', Telefon='{phone}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"Modal satır işlenirken hata: {ex.Message}");
                        continue;
                    }
                }
                
                OnLogMessage($"Modal'dan {recipients.Count} alıcı çıkarıldı.");
                return recipients;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Modal'dan alıcı çıkarma hatası: {ex.Message}");
                return new List<SmsRecipientInfo>();
            }
        }

        public async Task SendSmsForPeriodAsync(PeriodInfo period, List<SmsRecipientInfo> selectedRecipients, CancellationToken cancellationToken)
        {
            OnLogMessage($"🚀 SMS gönderimi başlatılıyor - Dönem: {period.Name}, Alıcı Sayısı: {selectedRecipients.Count}");
            await SendSmsForPeriodInternalAsync(period, selectedRecipients, cancellationToken, false);
        }

        public async Task SendSmsForPaymentOrderCreatorAsync(PeriodInfo period, List<SmsRecipientInfo> selectedRecipients, CancellationToken cancellationToken)
        {
            // Ödeme emri oluşturan için normal SMS gönderim metodunu kullan
            await SendSmsForPeriodAsync(period, selectedRecipients, cancellationToken);
        }

        private async Task SendSmsForPeriodInternalAsync(PeriodInfo period, List<SmsRecipientInfo> selectedRecipients, CancellationToken cancellationToken, bool isPaymentOrderCreator)
        {
            try
            {
                var operationType = isPaymentOrderCreator ? "Ödeme Emri Oluşturan SMS" : "Normal SMS";
                OnStatusChanged($"{operationType} gönderimi başlatılıyor - {period.Name}...");
                OnLogMessage($"{period.Name} dönemi için {operationType} gönderimi başlatılıyor...");

                // Mevcut Chrome context'ini kullan (yeni pencere açma)
                if (_context == null)
                {
                    OnLogMessage("Chrome context'i bulunamadı, mevcut pencereye bağlanılıyor...");
                    var playwright = await Playwright.CreateAsync();
                    _browser = await playwright.Chromium.ConnectOverCDPAsync("http://localhost:9222");
                    _context = _browser.Contexts.FirstOrDefault() ?? await _browser.NewContextAsync();
                }

                // Direkt dönem ID'si ile SMS URL'sine git
                OnLogMessage($"Direkt SMS URL'sine gidiliyor (Dönem ID: {period.Id})...");
                
                // Açık olan pencerede yeni sekme aç ve direkt SMS URL'sine git - sabit URL kullan
                var smsPage = await _context.NewPageAsync();
                var smsUrl = $"https://www.pinhuman.net/AgcServiceRecieptPeriod/SendSmsToEngineers/{period.Id}";
                
                await smsPage.GotoAsync(smsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                await smsPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
                await smsPage.WaitForTimeoutAsync(2000);
                
                // SMS alıcıları sayfası açıldı - otomatik seçim ve gönderim yapılacak
                OnLogMessage($"✅ SMS alıcıları sayfası yeni sekmede açıldı (Dönem ID: {period.Id})");
                
                // Önce sayfadaki alıcıları al
                var pageRecipients = await ExtractRecipientsFromHtmlAsync(smsPage, period.Name);
                OnLogMessage($"Sayfada {pageRecipients.Count} alıcı bulundu.");
                
                // Seçili alıcıları filtrele
                var recipientsToSend = selectedRecipients.Where(r => r.IsSelected).ToList();
                OnLogMessage($"Gönderilecek alıcı sayısı: {recipientsToSend.Count}");
                
                if (!recipientsToSend.Any())
                {
                    OnLogMessage("Gönderilecek alıcı bulunamadı, tüm alıcılar seçiliyor...");
                    recipientsToSend = pageRecipients;
                }
                
                // Tüm alıcı checkbox'larını işaretle (YALNIZCA GÖRÜNÜR OLANLAR)
                try
                {
                    // Sağ paneldeki alıcı listesi konteynırını bul ve sadece o kapsamda ara
                    var recipientsContainer = await smsPage.QuerySelectorAsync("div:has-text('SMS Alıcıları')");
                    var allCheckboxes = recipientsContainer != null
                        ? await recipientsContainer.QuerySelectorAllAsync("input[type='checkbox'][name='SelectedIds']")
                        : await smsPage.QuerySelectorAllAsync("input[type='checkbox'][name='SelectedIds']");

                    var visibleCheckboxes = new List<IElementHandle>();
                    foreach (var cb in allCheckboxes)
                    {
                        try
                        {
                            var isActuallyVisible = await cb.EvaluateAsync<bool>(@"(el) => {
                                const isHidden = (node) => {
                                    if (!node) return false;
                                    const cs = window.getComputedStyle(node);
                                    if (cs.display === 'none' || cs.visibility === 'hidden' || parseFloat(cs.opacity) === 0) return true;
                                    return isHidden(node.parentElement);
                                };
                                if (isHidden(el)) return false;
                                const rect = el.getBoundingClientRect();
                                if (rect.width <= 0 || rect.height <= 0) return false;
                                // En yakın kaydırılabilir konteynır (overflow auto/scroll)
                                const getScrollParent = (node) => {
                                    while (node && node !== document.body) {
                                        const cs = window.getComputedStyle(node);
                                        const overflowY = cs.overflowY;
                                        if (overflowY === 'auto' || overflowY === 'scroll') return node;
                                        node = node.parentElement;
                                    }
                                    return document.scrollingElement || document.documentElement;
                                };
                                const sp = getScrollParent(el);
                                const vp = sp.getBoundingClientRect();
                                const intersects = !(rect.bottom < vp.top || rect.top > vp.bottom || rect.right < vp.left || rect.left > vp.right);
                                return intersects;
                            }");
                            var isDisabled = await cb.IsDisabledAsync();
                            if (isActuallyVisible && !isDisabled) {
                                visibleCheckboxes.Add(cb);
                            }
                        }
                        catch { /* element detached, ignore */ }
                    }

                    // Filtre sonrası sayfa üstündeki sayacı oku: "Seçili: X / Toplam: Y"
                    int filteredTotal = -1;
                    try
                    {
                        var counterElement = await smsPage.QuerySelectorAsync("text=/Seçili:\\s*\\d+\\s*\\/\\s*Toplam:\\s*(\\d+)/");
                        if (counterElement != null)
                        {
                            var counterText = await counterElement.InnerTextAsync();
                            // Rakamları ayıkla
                            var match = System.Text.RegularExpressions.Regex.Match(counterText, @"Seçili:\s*\d+\s*/\s*Toplam:\s*(\d+)");
                            if (match.Success)
                            {
                                filteredTotal = int.Parse(match.Groups[1].Value);
                                OnLogMessage($"Filtrelenmiş toplam okundu: {filteredTotal}");
                            }
                        }
                    }
                    catch { /* ignore */ }

                    var targetsNeeded = (filteredTotal >= 0) ? filteredTotal : visibleCheckboxes.Count;
                    var checkedCount = 0;
                    OnLogMessage($"Seçilecek hedef sayı: {targetsNeeded}");

                    // Döngü ve scroll olmadan, sadece şu an görünür ve hedef kadarını işaretle
                    foreach (var checkbox in visibleCheckboxes.Take(targetsNeeded))
                    {
                        try
                        {
                            await checkbox.CheckAsync();
                            checkedCount++;
                            if (checkedCount % 10 == 0)
                            {
                                OnLogMessage($"✅ {checkedCount}/{targetsNeeded} alıcı işaretlendi...");
                            }
                        }
                        catch (Exception ex)
                        {
                            OnLogMessage($"Checkbox işaretleme hatası: {ex.Message}");
                        }
                    }

                    OnLogMessage($"✅ {checkedCount} görünür alıcı checkbox'ı işaretlendi.");
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Checkbox işaretleme hatası: {ex.Message}");
                }
                
                // Kısa bir bekleme
                await smsPage.WaitForTimeoutAsync(1000);
                
                // "Gönder" butonuna tıkla
                try
                {
                    var sendButton = await smsPage.QuerySelectorAsync("button.btn.btn-info.float-right, button[type='submit'], input[type='submit']");
                    if (sendButton != null)
                    {
                        OnLogMessage("'Gönder' butonu bulundu, tıklanıyor...");
                        await sendButton.ClickAsync();
                        OnLogMessage("✅ 'Gönder' butonuna tıklandı.");
                        
                        // SMS gönderimi onayını bekle
                        await smsPage.WaitForTimeoutAsync(3000);
                        
                        // Başarı mesajını kontrol et
                        var successMessage = await smsPage.QuerySelectorAsync(".alert-success, .success-message, .message-success");
                        if (successMessage != null)
                        {
                            var messageText = await successMessage.InnerTextAsync();
                            OnLogMessage($"✅ SMS gönderimi başarılı: {messageText}");
                        }
                        else
                        {
                            OnLogMessage($"✅ SMS gönderimi tamamlandı (Dönem ID: {period.Id})");
                        }
                    }
                    else
                    {
                        OnLogMessage("❌ 'Gönder' butonu bulunamadı, alternatif butonlar aranıyor...");
                        
                        // Alternatif butonları dene
                        var alternativeButtons = await smsPage.QuerySelectorAllAsync("button, input[type='submit']");
                        foreach (var button in alternativeButtons)
                        {
                            var buttonText = await button.InnerTextAsync();
                            if (buttonText.Contains("Gönder") || buttonText.Contains("Send") || buttonText.Contains("Submit"))
                            {
                                OnLogMessage($"Alternatif buton bulundu: {buttonText}");
                                await button.ClickAsync();
                                OnLogMessage("✅ Alternatif 'Gönder' butonuna tıklandı.");
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    OnLogMessage($"Gönder butonu tıklama hatası: {ex.Message}");
                }
                
                // Kısa bir bekleme sonra sekmeyi kapat
                await smsPage.WaitForTimeoutAsync(2000);
                await smsPage.CloseAsync();
                OnLogMessage("✅ SMS sekmesi kapatıldı.");

                OnStatusChanged($"{period.Name} dönemi tamamlandı");
                OnLogMessage($"{period.Name} dönemi için SMS gönderimi tamamlandı.");
            }
            catch (OperationCanceledException)
            {
                OnStatusChanged($"{period.Name} dönemi iptal edildi");
                OnLogMessage($"{period.Name} dönemi için SMS gönderimi iptal edildi.");
                throw;
            }
            catch (Exception ex)
            {
                OnStatusChanged($"{period.Name} dönemi hatası");
                OnLogMessage($"{period.Name} dönemi için SMS gönderimi sırasında hata: {ex.Message}");
                throw;
            }
        }

        private async Task InitializeBrowserAsync()
        {
            try
            {

                OnLogMessage("Mevcut Chrome penceresine bağlanılıyor...");
                
                var playwright = await Playwright.CreateAsync();
                
                // Mevcut Chrome penceresine bağlan (CDP üzerinden)
                _browser = await playwright.Chromium.ConnectOverCDPAsync("http://localhost:9222");
                
                // Mevcut context'i al veya yeni context oluştur
                var contexts = _browser.Contexts;
                if (contexts.Any())
                {
                    _context = contexts.First();
                    OnLogMessage("Mevcut Chrome context'ine bağlanıldı.");
                }
                else
                {
                    _context = await _browser.NewContextAsync();
                    OnLogMessage("Yeni Chrome context'i oluşturuldu.");
                }
                
                // Mevcut sayfayı al veya yeni sayfa oluştur
                var pages = _context.Pages;
                if (pages.Any())
                {
                    _page = pages.First();
                    OnLogMessage("Mevcut Chrome sayfasına bağlanıldı.");
                }
                else
                {
                    _page = await _context.NewPageAsync();
                    OnLogMessage("Yeni Chrome sayfası oluşturuldu.");
                }
                
                OnLogMessage("Chrome penceresine başarıyla bağlanıldı.");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Chrome'a bağlanırken hata: {ex.Message}");
                OnLogMessage("Yeni Chrome penceresi başlatılıyor...");
                
                // Config'den headless mod ayarını al
                var config = ConfigManager.LoadConfig();
                var isHeadless = config.Sms.HeadlessMode;
                
                OnLogMessage($"Gizli mod ayarı: {(isHeadless ? "Açık" : "Kapalı")}");
                
                // CDP bağlantısı başarısız olursa yeni pencere başlat
                var playwright = await Playwright.CreateAsync();
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = isHeadless,
                    Args = new[] { 
                        "--disable-blink-features=AutomationControlled", 
                        "--disable-web-security", 
                        "--remote-debugging-port=9222",
                        "--disable-extensions",
                        "--disable-plugins",
                        "--disable-images",
                        "--disable-javascript",
                        "--disable-background-timer-throttling",
                        "--disable-backgrounding-occluded-windows",
                        "--disable-renderer-backgrounding"
                    }
                });

                _context = await _browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = true,
                    BypassCSP = true
                });
                _page = await _context.NewPageAsync();
                await _page.SetViewportSizeAsync(1024, 768);
                
                OnLogMessage($"Yeni Chrome penceresi başarıyla başlatıldı. (Gizli mod: {(isHeadless ? "Açık" : "Kapalı")})");
            }
        }

        private async Task LoginToSystemAsync()
        {
            try
            {
                OnLogMessage("Sisteme giriş yapılıyor...");
                
                // Load config for login credentials
                var config = ConfigManager.LoadConfig();
                
                await _page!.GotoAsync("https://pinhuman.net");
                await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded); // Hızlandırıldı

                // Login formunu doldur
                await FillLoginFormAsync(config);
                
                // Login butonuna tıkla
                await ClickLoginButtonAsync();
                
                // 2FA kontrolü ve TOTP kodu üretimi
                await Handle2FAWithTOTPAsync(config);
                
                // Login başarısını kontrol et
                await CheckLoginSuccessAsync();
                
                // Login sonrası 0.5 saniye bekle (hızlandırıldı)
                await _page.WaitForTimeoutAsync(500);
                
                OnLogMessage("Sisteme başarıyla giriş yapıldı.");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Login sırasında hata: {ex.Message}");
                throw;
            }
        }

        private async Task FillLoginFormAsync(AppConfig config)
        {
            OnLogMessage("Login formu dolduruluyor...");
            
            // Kullanıcı adı alanı
            var usernameField = await _page!.QuerySelectorAsync("#UserName");
            if (usernameField != null)
            {
                await usernameField.FillAsync(config.AutoLogin.Username);
                OnLogMessage("Kullanıcı adı girildi.");
            }
            else
            {
                OnLogMessage("Kullanıcı adı alanı bulunamadı!");
            }
            
            // Firma kodu alanı
            var companyCodeField = await _page!.QuerySelectorAsync("#CompanyCode");
            if (companyCodeField != null)
            {
                await companyCodeField.FillAsync(config.AutoLogin.CompanyCode);
                OnLogMessage("Firma kodu girildi.");
            }
            else
            {
                OnLogMessage("Firma kodu alanı bulunamadı!");
            }
            
            // Şifre alanı
            var passwordField = await _page!.QuerySelectorAsync("#Password");
            if (passwordField != null)
            {
                await passwordField.FillAsync(config.AutoLogin.Password);
                OnLogMessage("Şifre girildi.");
            }
            else
            {
                OnLogMessage("Şifre alanı bulunamadı!");
            }
        }

        private async Task ClickLoginButtonAsync()
        {
            // GİRİŞ butonunu bul
            var loginButton = await _page!.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block");
            
            if (loginButton != null)
            {
                // Butona tıklamadan önce biraz bekle
                await _page.WaitForTimeoutAsync(2000);
                
                // Önce butonun görünür olduğundan emin ol
                await loginButton.WaitForElementStateAsync(ElementState.Visible);
                
                // JavaScript ile tıkla
                await _page.EvaluateAsync(@"
                    const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block');
                    if (button) {
                        button.click();
                    }
                ");
                
                // Form submit'i bekle
                await _page.WaitForTimeoutAsync(2000);
                OnLogMessage("Login butonuna tıklandı.");
            }
            else
            {
                OnLogMessage("Login butonu bulunamadı! Manuel olarak giriş yapın...");
            }
        }

        private async Task Handle2FAWithTOTPAsync(AppConfig config)
        {
            try
            {
                var twoFactorField = await _page!.WaitForSelectorAsync("#Code, input[name='code'], input[name='2fa'], input[name='otp'], input[placeholder*='code'], input[placeholder*='2fa'], input[placeholder*='OTP'], input[placeholder*='doğrulama'], input[placeholder*='verification']", new PageWaitForSelectorOptions { Timeout = 3000 });
                
                if (twoFactorField != null)
                {
                    string twoFactorCode;
                    
                    if (!string.IsNullOrEmpty(config.AutoLogin.TotpSecret))
                    {
                        // TOTP kodu üret
                        twoFactorCode = GenerateTOTPCode(config.AutoLogin.TotpSecret);
                        OnLogMessage("TOTP kodu üretildi.");
                    }
                    else
                    {
                        // Manuel kod girişi
                        OnLogMessage("2FA kodu manuel olarak girilmeli.");
                        return;
                    }
                    
                    if (!string.IsNullOrEmpty(twoFactorCode))
                    {
                        // Kodu temizle ve gir
                        await twoFactorField.FillAsync("");
                        await twoFactorField.FillAsync(twoFactorCode);
                        OnLogMessage("2FA kodu girildi.");
                        
                        // Biraz bekle
                        await _page.WaitForTimeoutAsync(500);
                        
                        // 2FA submit butonunu bul ve tıkla
                        var submitButton = await _page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block, button[type='submit'], input[type='submit']");
                        if (submitButton != null)
                        {
                            // JavaScript ile tıkla
                            await _page.EvaluateAsync(@"
                                const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block, button[type=""submit""]');
                                if (button) {
                                    button.click();
                                }
                            ");
                            
                            // Submit sonrası bekle
                            await _page.WaitForTimeoutAsync(1000);
                            OnLogMessage("2FA submit butonuna tıklandı.");
                        }
                        else
                        {
                            OnLogMessage("2FA submit butonu bulunamadı. Manuel olarak doğrulayın...");
                        }
                    }
                }
                else
                {
                    OnLogMessage("2FA alanı bulunamadı, 2FA gerekmiyor olabilir.");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"2FA işlemi sırasında hata: {ex.Message}");
            }
        }

        private async Task CheckLoginSuccessAsync()
        {
            try
            {
                // Login başarısını kontrol et - dashboard veya ana sayfa elementlerini ara
                var successIndicator = await _page!.QuerySelectorAsync(".dashboard, .main-content, .user-info, .logout, [href*='logout']");
                
                if (successIndicator != null)
                {
                    OnLogMessage("Login başarılı - dashboard bulundu.");
                }
                else
                {
                    // URL'yi kontrol et
                    var currentUrl = _page.Url;
                    if (!currentUrl.Contains("login") && !currentUrl.Contains("Login"))
                    {
                        OnLogMessage("Login başarılı - URL login sayfasında değil.");
                    }
                    else
                    {
                        OnLogMessage("Login durumu belirsiz, manuel kontrol gerekebilir.");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Login kontrolü sırasında hata: {ex.Message}");
            }
        }

        private string GenerateTOTPCode(string secret)
        {
            try
            {
                // TOTP kodu üretimi için Otp.NET kullan
                var totp = new OtpNet.Totp(Base32Encoding.ToBytes(secret));
                return totp.ComputeTotp();
            }
            catch (Exception ex)
            {
                OnLogMessage($"TOTP kodu üretilirken hata: {ex.Message}");
                return "000000"; // Fallback
            }
        }

        private async Task NavigateToReceiptPeriodAsync()
        {
            try
            {
                OnLogMessage("AgcServiceRecieptPeriod sayfasına gidiliyor...");
                
                // Ödeme emri URL'sine gidiş yöntemiyle aynı şekilde sabit URL kullan
                var targetUrl = "https://www.pinhuman.net/AgcServiceRecieptPeriod";
                await _page!.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded }); // Hızlandırıldı
                
                // Sayfanın DOM yüklenmesini bekle
                await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded); // Hızlandırıldı
                
                // Ek bekleme süresi - sayfa içeriğinin tam yüklenmesi için
                await _page.WaitForTimeoutAsync(1500); // Hızlandırıldı
                
                // Sayfa içeriğinin yüklendiğinden emin ol
                await _page.WaitForSelectorAsync("body", new PageWaitForSelectorOptions { Timeout = 10000 });
                
                OnLogMessage("AgcServiceRecieptPeriod sayfasına başarıyla gidildi.");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Sayfa geçişi sırasında hata: {ex.Message}");
                throw;
            }
        }

        private async Task<List<PeriodInfo>> ExtractPeriodsFromPageAsync()
        {
            try
            {
                OnLogMessage("Sayfadan dönemler çıkarılıyor...");
                
                // Sayfa içeriğinin tam yüklenmesi için ek bekleme
                await _page!.WaitForTimeoutAsync(1500); // Hızlandırıldı
                
                // Global tekrar kontrolü için HashSet'ler
                var seenPeriodIds = new HashSet<string>();
                var seenPeriodNames = new HashSet<string>();
                var seenNormalizedNames = new HashSet<string>();
                
                // Sayfa 120'yi göster
                OnLogMessage("Sayfa 120 gösteriliyor...");
                
                // ItemPerPage dropdown'ını bul ve 120 seç
                var itemPerPageSelect = await _page.QuerySelectorAsync("select[name='ItemPerPage_'], select#ItemPerPage_");
                if (itemPerPageSelect != null)
                {
                    await itemPerPageSelect.SelectOptionAsync("120");
                    await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded); // Hızlandırıldı
                    await _page.WaitForTimeoutAsync(1000); // Sayfa yeniden yüklenmesi için hızlandırıldı
                    OnLogMessage("Sayfa 120 olarak ayarlandı.");
                    
                    // Sayfanın tam yüklenmesi için ek bekleme
                    await _page.WaitForSelectorAsync("table tbody tr");
                    await _page.WaitForTimeoutAsync(500); // Ek güvenlik için hızlandırıldı
                }
                else
                {
                    OnLogMessage("ItemPerPage dropdown'ı bulunamadı.");
                }
                
                // Tablo satırlarını bul - onay bekleyen fişleri olan satırlar
                var tableRows = await _page.QuerySelectorAllAsync("table tbody tr");
                OnLogMessage($"Toplam {tableRows.Count} satır bulundu.");
                
                // Eğer 5'ten az satır bulunduysa, sayfa tam yüklenmemiş olabilir
                if (tableRows.Count < 10)
                {
                    OnLogMessage("⚠️ Az satır bulundu, sayfa yeniden yükleniyor...");
                    await _page.WaitForTimeoutAsync(2000); // Hızlandırıldı
                    tableRows = await _page.QuerySelectorAllAsync("table tbody tr");
                    OnLogMessage($"Yeniden yükleme sonrası toplam {tableRows.Count} satır bulundu.");
                }
                
                var periods = new List<PeriodInfo>();
                var filteredCount = 0;
                var totalChecked = 0;
                
                foreach (var row in tableRows)
                {
                    try
                    {
                        totalChecked++;
                        
                        // Satırdaki hücreleri al
                        var cells = await row.QuerySelectorAllAsync("td");
                        if (cells.Count < 2) 
                        {
                            OnLogMessage($"Satır {totalChecked}: Yetersiz hücre sayısı ({cells.Count})");
                            continue;
                        }
                        
                        // Dönem bilgisi (genellikle ilk sütun)
                        var periodCell = await cells[0].InnerTextAsync();
                        if (string.IsNullOrEmpty(periodCell)) 
                        {
                            OnLogMessage($"Satır {totalChecked}: Boş dönem hücresi");
                            continue;
                        }
                        
                        // OnLogMessage($"Satır {totalChecked}: Dönem = '{periodCell.Trim()}'");
                        
                        // Onay durumu hücresi (ikinci sütun veya sonraki sütunlar)
                        string approvalStatusText = "";
                        int preApprovalCount = 0;
                        int approvalCount = 0;
                        
                        // Onay durumu hücresini bul
                        for (int i = 1; i < cells.Count; i++)
                        {
                            var cellText = await cells[i].InnerTextAsync();
                            if (cellText.Contains("önonay") || cellText.Contains("onay"))
                            {
                                approvalStatusText = cellText;
                                // OnLogMessage($"Satır {totalChecked}: Onay durumu = '{cellText}'");
                                break;
                            }
                        }
                        
                        if (!string.IsNullOrEmpty(approvalStatusText))
                        {
                            // Farklı formatları oku: "3 önonay 0 onay bekliyor", "1 önonay 0 onay", "0 onay 2 önonay" vb.
                            var preApprovalMatch = System.Text.RegularExpressions.Regex.Match(approvalStatusText, @"(\d+)\s*önonay");
                            if (preApprovalMatch.Success)
                            {
                                int.TryParse(preApprovalMatch.Groups[1].Value, out preApprovalCount);
                            }
                            
                            var approvalMatch = System.Text.RegularExpressions.Regex.Match(approvalStatusText, @"(\d+)\s*onay\s*bekliyor");
                            if (approvalMatch.Success)
                            {
                                int.TryParse(approvalMatch.Groups[1].Value, out approvalCount);
                            }
                            
                            // Eğer "onay bekliyor" bulunamazsa, sadece "onay" ara
                            if (approvalCount == 0)
                            {
                                var simpleApprovalMatch = System.Text.RegularExpressions.Regex.Match(approvalStatusText, @"(\d+)\s*onay(?!\s*bekliyor)");
                                if (simpleApprovalMatch.Success)
                                {
                                    int.TryParse(simpleApprovalMatch.Groups[1].Value, out approvalCount);
                                }
                            }
                            
                            // OnLogMessage($"Satır {totalChecked}: Çıkarılan sayılar - Ön Onay: {preApprovalCount}, Onay Bekleyen: {approvalCount}");
                        }
                        else
                        {
                            // OnLogMessage($"Satır {totalChecked}: Onay durumu bulunamadı");
                        }
                        
                        // Sadece ön onay veya onay bekleyen sayısı 0'dan büyük olanları al
                        if (preApprovalCount > 0 || approvalCount > 0)
                        {
                            filteredCount++;
                            
                            // Dönem ismini temizle
                            var cleanPeriodName = periodCell.Trim();
                            
                            // Eğer dönem ismi sadece sayı ise, daha detaylı bilgi ara
                            if (int.TryParse(cleanPeriodName, out int periodNumber))
                            {
                                // Dönem numarasından gerçek ismi bulmaya çalış
                                // Önce satırdaki diğer hücrelerde dönem bilgisi var mı bak
                                for (int i = 0; i < cells.Count; i++)
                                {
                                    var cellText = await cells[i].InnerTextAsync();
                                    if (!string.IsNullOrEmpty(cellText) && 
                                        (cellText.Contains("2025") || cellText.Contains("2024") || 
                                         cellText.Contains("Ocak") || cellText.Contains("Şubat") || 
                                         cellText.Contains("Mart") || cellText.Contains("Nisan") ||
                                         cellText.Contains("Mayıs") || cellText.Contains("Haziran") ||
                                         cellText.Contains("Temmuz") || cellText.Contains("Ağustos") ||
                                         cellText.Contains("Eylül") || cellText.Contains("Ekim") ||
                                         cellText.Contains("Kasım") || cellText.Contains("Aralık")))
                                    {
                                        cleanPeriodName = cellText.Trim();
                                        break;
                                    }
                                }
                                
                                // Eğer hala sayı ise, varsayılan format kullan
                                if (int.TryParse(cleanPeriodName, out _))
                                {
                                    cleanPeriodName = $"Dönem {periodNumber}";
                                }
                            }
                            
                            // Dönem ID'sini HTML'den çıkar - GUID formatında olmalı
                            string periodId = "";
                            try
                            {
                                // Önce SMS gönderim linklerini bul (GUID içeren)
                                var smsLinks = await row.QuerySelectorAllAsync("a[href*='/SendSmsToEngineers/']");
                                foreach (var smsLink in smsLinks)
                                {
                                    var href = await smsLink.GetAttributeAsync("href");
                                    if (!string.IsNullOrEmpty(href))
                                    {
                                        // URL'den GUID'i çıkar: /AgcServiceRecieptPeriod/SendSmsToEngineers/cdb9edfb-1e88-4c73-aa59-13ee2be6e45d
                                        var match = System.Text.RegularExpressions.Regex.Match(href, @"/SendSmsToEngineers/([a-f0-9\-]+)");
                                        if (match.Success)
                                        {
                                            periodId = match.Groups[1].Value;
                                            OnLogMessage($"✅ Dönem ID bulundu: {periodId}");
                                            break;
                                        }
                                    }
                                }
                                
                                // Eğer SMS linklerinden bulunamazsa, diğer linklerden dene
                                if (string.IsNullOrEmpty(periodId))
                                {
                                    var periodLinks = await row.QuerySelectorAllAsync("a[href*='period']");
                                    foreach (var link in periodLinks)
                                    {
                                        var href = await link.GetAttributeAsync("href");
                                        if (!string.IsNullOrEmpty(href))
                                        {
                                            // URL'den period parametresini çıkar
                                            var match = System.Text.RegularExpressions.Regex.Match(href, @"[?&]period=([^&]+)");
                                            if (match.Success)
                                            {
                                                periodId = match.Groups[1].Value;
                                                OnLogMessage($"✅ Dönem ID bulundu (period param): {periodId}");
                                                break;
                                            }
                                        }
                                    }
                                }
                                
                                // Eğer hala bulunamazsa, data attribute'lardan dene
                                if (string.IsNullOrEmpty(periodId))
                                {
                                    var dataElements = await row.QuerySelectorAllAsync("[data-period-id], [data-id], [data-period]");
                                    foreach (var element in dataElements)
                                    {
                                        periodId = await element.GetAttributeAsync("data-period-id") ?? 
                                                  await element.GetAttributeAsync("data-id") ?? 
                                                  await element.GetAttributeAsync("data-period") ?? "";
                                        if (!string.IsNullOrEmpty(periodId)) 
                                        {
                                            OnLogMessage($"✅ Dönem ID bulundu (data attr): {periodId}");
                                            break;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                OnLogMessage($"Dönem ID çıkarılırken hata: {ex.Message}");
                            }
                            
                            // Eğer dönem ID bulunamazsa, hata logla
                            if (string.IsNullOrEmpty(periodId))
                            {
                                OnLogMessage($"❌ Dönem ID bulunamadı: {cleanPeriodName}");
                                continue; // Bu dönemi atla
                            }
                            
                            // Global tekrar kontrolü - ID
                            if (seenPeriodIds.Contains(periodId))
                            {
                                OnLogMessage($"⚠️ Global: Aynı ID'ye sahip dönem zaten var, atlanıyor: {cleanPeriodName} (ID: {periodId})");
                                continue; // Bu dönemi atla
                            }
                            
                            // Global tekrar kontrolü - İsim
                            if (seenPeriodNames.Contains(cleanPeriodName))
                            {
                                OnLogMessage($"⚠️ Global: Aynı isme sahip dönem zaten var, atlanıyor: {cleanPeriodName}");
                                continue; // Bu dönemi atla
                            }
                            
                            // Global tekrar kontrolü - Normalize edilmiş isim
                            var normalizedName = cleanPeriodName.Replace("-", "").Replace(" ", "").ToLower();
                            if (seenNormalizedNames.Contains(normalizedName))
                            {
                                OnLogMessage($"⚠️ Global: Benzer isme sahip dönem zaten var, atlanıyor: {cleanPeriodName}");
                                continue; // Bu dönemi atla
                            }
                            
                            // Yerel tekrar kontrolü - ID
                            var existingPeriod = periods.FirstOrDefault(p => p.Id == periodId);
                            if (existingPeriod != null)
                            {
                                OnLogMessage($"⚠️ Yerel: Aynı ID'ye sahip dönem zaten var, atlanıyor: {cleanPeriodName} (ID: {periodId})");
                                continue; // Bu dönemi atla
                            }
                            
                            // Yerel tekrar kontrolü - İsim
                            var existingPeriodByName = periods.FirstOrDefault(p => p.Name == cleanPeriodName);
                            if (existingPeriodByName != null)
                            {
                                OnLogMessage($"⚠️ Yerel: Aynı isme sahip dönem zaten var, atlanıyor: {cleanPeriodName}");
                                continue; // Bu dönemi atla
                            }
                            
                            // Yerel tekrar kontrolü - Benzer isim
                            var similarPeriod = periods.FirstOrDefault(p => 
                                p.Name.Replace("-", "").Replace(" ", "").ToLower() == normalizedName);
                            if (similarPeriod != null)
                            {
                                OnLogMessage($"⚠️ Yerel: Benzer isme sahip dönem zaten var, atlanıyor: {cleanPeriodName} (Benzer: {similarPeriod.Name})");
                                continue; // Bu dönemi atla
                            }
                            
                            // Dönem adında çizgi yoksa ve aynı tarih aralığı varsa atla
                            if (!cleanPeriodName.Contains("-"))
                            {
                                // Çizgisiz dönem adından tarih bilgisini çıkar
                                var dateMatch = System.Text.RegularExpressions.Regex.Match(cleanPeriodName, @"(\d{1,2})\s*[-–]\s*(\d{1,2})\s+(\w+)\s+(\d{4})");
                                if (dateMatch.Success)
                                {
                                    var startDate = dateMatch.Groups[1].Value;
                                    var endDate = dateMatch.Groups[2].Value;
                                    var month = dateMatch.Groups[3].Value;
                                    var year = dateMatch.Groups[4].Value;
                                    
                                    // Aynı tarih aralığına sahip dönem var mı kontrol et
                                    var sameDatePeriod = periods.FirstOrDefault(p => 
                                        p.Name.Contains($"{startDate}-{endDate}") && 
                                        p.Name.Contains(month) && 
                                        p.Name.Contains(year));
                                    
                                    if (sameDatePeriod != null)
                                    {
                                        OnLogMessage($"⚠️ Aynı tarih aralığına sahip dönem zaten var, atlanıyor: {cleanPeriodName} (Mevcut: {sameDatePeriod.Name})");
                                        continue; // Bu dönemi atla
                                    }
                                }
                            }
                            
                            // Parantez içindeki şirket adını kontrol et
                            var companyMatch = System.Text.RegularExpressions.Regex.Match(cleanPeriodName, @"\(([^)]+)\)");
                            if (companyMatch.Success)
                            {
                                var companyName = companyMatch.Groups[1].Value.Trim();
                                
                                // Aynı şirket adına sahip dönem var mı kontrol et
                                var sameCompanyPeriod = periods.FirstOrDefault(p => 
                                    p.Name.Contains($"({companyName})"));
                                
                                if (sameCompanyPeriod != null)
                                {
                                    // Tarih aralığını da kontrol et
                                    var currentDateMatch = System.Text.RegularExpressions.Regex.Match(cleanPeriodName, @"(\d{1,2})\s*[-–]\s*(\d{1,2})\s+(\w+)\s+(\d{4})");
                                    var existingDateMatch = System.Text.RegularExpressions.Regex.Match(sameCompanyPeriod.Name, @"(\d{1,2})\s*[-–]\s*(\d{1,2})\s+(\w+)\s+(\d{4})");
                                    
                                    if (currentDateMatch.Success && existingDateMatch.Success)
                                    {
                                        var currentDateRange = $"{currentDateMatch.Groups[1].Value}-{currentDateMatch.Groups[2].Value} {currentDateMatch.Groups[3].Value} {currentDateMatch.Groups[4].Value}";
                                        var existingDateRange = $"{existingDateMatch.Groups[1].Value}-{existingDateMatch.Groups[2].Value} {existingDateMatch.Groups[3].Value} {existingDateMatch.Groups[4].Value}";
                                        
                                        if (currentDateRange == existingDateRange)
                                        {
                                            OnLogMessage($"⚠️ Aynı şirket ve tarih aralığına sahip dönem zaten var, atlanıyor: {cleanPeriodName} (Mevcut: {sameCompanyPeriod.Name})");
                                            continue; // Bu dönemi atla
                                        }
                                    }
                                }
                            }
                            
                            periods.Add(new PeriodInfo
                            {
                                Id = periodId,
                                Name = cleanPeriodName,
                                Description = $"Ön Onay: {preApprovalCount}, Onay Bekleyen: {approvalCount}",
                                ApprovalCount = approvalCount + preApprovalCount // Toplam onay sayısı
                            });
                            
                            // Global HashSet'lere ekle
                            seenPeriodIds.Add(periodId);
                            seenPeriodNames.Add(cleanPeriodName);
                            seenNormalizedNames.Add(normalizedName);
                            
                            OnLogMessage($"✅ Dönem {filteredCount} eklendi: {cleanPeriodName} - Ön Onay: {preApprovalCount}, Onay Bekleyen: {approvalCount}");
                        }
                        else
                        {
                            OnLogMessage($"❌ Satır {totalChecked} filtrelendi: Ön Onay: {preApprovalCount}, Onay Bekleyen: {approvalCount} (ikisi de 0)");
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"Satır {totalChecked} işlenirken hata: {ex.Message}");
                        continue;
                    }
                }
                
                OnLogMessage($"{periods.Count} dönem bulundu (sadece onay bekleyenler). {totalChecked} satırdan {filteredCount} tanesi filtrelendi.");
                return periods;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Dönem çıkarma hatası: {ex.Message}");
                throw;
            }
        }

        private async Task NavigateToSpecificPeriodAsync(PeriodInfo period)
        {
            try
            {
                OnLogMessage($"{period.Name} dönemine gidiliyor...");
                
                // Dönem seçim dropdown'ını bul ve seç
                var periodSelect = await _page!.QuerySelectorAsync("select[name*='period'], select[id*='period'], .period-select");
                if (periodSelect != null)
                {
                    await periodSelect.SelectOptionAsync(period.Id);
                    await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    OnLogMessage($"{period.Name} dönemi seçildi.");
                }
                else
                {
                    OnLogMessage("Dönem seçim dropdown'ı bulunamadı, mevcut sayfa kullanılıyor.");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Dönem seçimi hatası: {ex.Message}");
                throw;
            }
        }

        private async Task<int> GetTotalPagesAsync()
        {
            try
            {
                // Get total pages from pagination
                // This would need to be implemented based on actual page structure
                await Task.Delay(200);
                return 120; // Simulate 120 pages as mentioned in requirements
            }
            catch (Exception ex)
            {
                OnLogMessage($"Sayfa sayısı alınırken hata: {ex.Message}");
                return 1; // Fallback to 1 page
            }
        }

        private async Task NavigateToPageAsync(int pageNumber)
        {
            try
            {
                if (pageNumber > 1)
                {
                    // Navigate to specific page
                    // This would need actual implementation
                    await Task.Delay(150);
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Sayfa {pageNumber}'e giderken hata: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessPageForSmsAsync(List<SmsRecipientInfo> selectedRecipients, CancellationToken cancellationToken)
        {
            try
            {
                // Find approval status column header
                var approvalStatusHeader = await _page!.QuerySelectorAsync("div.th-inner:has-text('Fiş Onay Durumu')");
                if (approvalStatusHeader == null)
                {
                    OnLogMessage("Fiş Onay Durumu sütunu bulunamadı.");
                    return;
                }

                // Find rows with approval count > 1
                var rows = await _page.QuerySelectorAllAsync("tr");
                
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Check if this row has pending approvals (count > 1)
                    var approvalCountCell = await row.QuerySelectorAsync("td"); // This would need proper selector
                    if (approvalCountCell != null)
                    {
                        var approvalText = await approvalCountCell.InnerTextAsync();
                        if (int.TryParse(approvalText.Trim(), out int approvalCount) && approvalCount >= 1)
                        {
                            OnLogMessage($"Onay bekleyen fiş bulundu (Onay sayısı: {approvalCount})");
                            
                            // Find the SMS link in dropdown menu to get the period ID (verilen yapıya uygun)
                            var smsLink = await row.QuerySelectorAsync(".dropdown-item[href*='/AgcServiceRecieptPeriod/SendSmsToEngineers/'], a[href*='/AgcServiceRecieptPeriod/SendSmsToEngineers/']");
                            if (smsLink != null)
                            {
                                try
                                {
                                    // Extract period ID from href (güvenli şekilde)
                                    var href = await smsLink.GetAttributeAsync("href");
                                    if (!string.IsNullOrEmpty(href))
                                    {
                                        var periodId = ExtractPeriodIdFromHref(href);
                                        
                                        if (!string.IsNullOrEmpty(periodId))
                                        {
                                            // Direkt dönem ID'si ile SMS URL'sine git
                                            OnLogMessage($"Direkt SMS URL'sine gidiliyor (Dönem ID: {periodId})...");
                                            
                                            // Açık olan Chrome penceresinde yeni sekme aç - sabit URL kullan
                                            var smsPage = await _context!.NewPageAsync();
                                            var smsUrl = $"https://www.pinhuman.net/AgcServiceRecieptPeriod/SendSmsToEngineers/{periodId}";
                                            
                                            await smsPage.GotoAsync(smsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                                            await smsPage.WaitForLoadStateAsync(LoadState.NetworkIdle);
                                            await smsPage.WaitForTimeoutAsync(2000);
                                            
                                            // SMS alıcıları sayfası açıldı - kullanıcı seçim yapacak
                                            OnLogMessage($"✅ SMS alıcıları sayfası yeni sekmede açıldı (Dönem ID: {periodId})");
                                            OnLogMessage("📋 Lütfen alıcıları seçin ve 'Gönder' butonuna tıklayın.");
                                            
                                            // Yeni sekmeyi aktif hale getir
                                            await smsPage.BringToFrontAsync();
                                            
                                            // Kısa bir bekleme sonra sekmeyi kapat
                                            await smsPage.WaitForTimeoutAsync(3000);
                                            await smsPage.CloseAsync();
                                            OnLogMessage("✅ SMS sekmesi kapatıldı.");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    OnLogMessage($"SMS link işlenirken hata: {ex.Message}");
                                    continue;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Sayfa işlenirken hata: {ex.Message}");
                throw;
            }
        }

        private async Task ProcessSmsModalAsync(List<SmsRecipientInfo> selectedRecipients)
        {
            try
            {
                OnLogMessage("SMS modal penceresi işleniyor...");
                
                // Find form with SMS sending functionality
                var form = await _page!.QuerySelectorAsync("form[action*='/AgcServiceRecieptPeriod/SendSmsToEngineers/']");
                if (form == null)
                {
                    OnLogMessage("SMS gönderim formu bulunamadı.");
                    return;
                }

                // Önce tüm checkbox'ları temizle
                var selectAllCheckbox = await form.QuerySelectorAsync("input[type='checkbox'][id='selectAllPop']");
                if (selectAllCheckbox != null)
                {
                    await selectAllCheckbox.UncheckAsync();
                    OnLogMessage("Tümünü Seç checkbox'ı temizlendi.");
                }
                
                // Select checkboxes based on selected recipients
                var checkboxes = await form.QuerySelectorAllAsync("input[type='checkbox'][name='SelectedIds']");
                var selectedCount = 0;

                foreach (var checkbox in checkboxes)
                {
                    try
                    {
                        // Get person info from the row
                        var row = await checkbox.EvaluateAsync<IElementHandle>("el => el.closest('tr')");
                        if (row != null)
                        {
                            var cells = await row.QuerySelectorAllAsync("td");
                            if (cells.Count >= 3)
                            {
                                var nameCell = cells[1]; // Name column (2. sütun)
                                var phoneCell = cells[2]; // Phone column (3. sütun)
                                
                                var name = await nameCell.InnerTextAsync();
                                var phone = await phoneCell.InnerTextAsync();
                                
                                // Check if this person is in the selected recipients list
                                var isSelected = selectedRecipients.Any(r => 
                                    r.Name.Trim().Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                                    r.Phone.Trim().Equals(phone.Trim(), StringComparison.OrdinalIgnoreCase));
                                
                                if (isSelected)
                                {
                                    await checkbox.CheckAsync();
                                    selectedCount++;
                                    OnLogMessage($"Seçildi: {name.Trim()} - {phone.Trim()}");
                                }
                                else
                                {
                                    await checkbox.UncheckAsync();
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"Checkbox işlenirken hata: {ex.Message}");
                        continue;
                    }
                }

                if (selectedCount > 0)
                {
                    // Click send button
                    var sendButton = await form.QuerySelectorAsync("button:has-text('Gönder'), button[type='submit']");
                    if (sendButton != null)
                    {
                        OnLogMessage($"{selectedCount} kişi için SMS gönderiliyor...");
                        await sendButton.ClickAsync();
                        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        
                        OnLogMessage($"SMS başarıyla gönderildi ({selectedCount} kişi).");
                    }
                    else
                    {
                        OnLogMessage("SMS gönder butonu bulunamadı.");
                    }
                }
                else
                {
                    OnLogMessage("Bu sayfada SMS gönderilecek kişi bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"SMS modal işlenirken hata: {ex.Message}");
                throw;
            }
        }



        /// <summary>
        /// Href'ten period ID'yi çıkarır - GUID formatında
        /// </summary>
        private string ExtractPeriodIdFromHref(string? href)
        {
            try
            {
                if (string.IsNullOrEmpty(href))
                    return string.Empty;
                
                // Önce SMS gönderim URL'sinden GUID'i çıkar: /AgcServiceRecieptPeriod/SendSmsToEngineers/cdb9edfb-1e88-4c73-aa59-13ee2be6e45d
                var smsPattern = @"/SendSmsToEngineers/([a-f0-9\-]+)";
                var smsMatch = System.Text.RegularExpressions.Regex.Match(href, smsPattern);
                
                if (smsMatch.Success)
                {
                    var guid = smsMatch.Groups[1].Value;
                    OnLogMessage($"✅ SMS URL'den GUID çıkarıldı: {guid}");
                    return guid;
                }
                
                // Eğer SMS URL'den bulunamazsa, period parametresinden dene
                var periodPattern = @"[?&]period=([^&]+)";
                var periodMatch = System.Text.RegularExpressions.Regex.Match(href, periodPattern);
                
                if (periodMatch.Success)
                {
                    var periodId = periodMatch.Groups[1].Value;
                    OnLogMessage($"✅ Period parametresinden ID çıkarıldı: {periodId}");
                    return periodId;
                }
                
                OnLogMessage($"❌ Href'den dönem ID çıkarılamadı: {href}");
                return string.Empty;
            }
            catch (Exception ex)
            {
                OnLogMessage($"Period ID çıkarma hatası: {ex.Message}");
                return string.Empty;
            }
        }



        private async Task CloseBrowserAsync()
        {
            try
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                    _page = null;
                    OnLogMessage("Tarayıcı kapatıldı.");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Tarayıcı kapatılırken hata: {ex.Message}");
                // Hata olsa bile referansları temizle
                _browser = null;
                _page = null;
            }
        }

        private void OnLogMessage(string message)
        {
            LogMessage?.Invoke(this, new LogMessageEventArgs { Message = message });
        }

        private void OnStatusChanged(string status)
        {
            StatusChanged?.Invoke(this, status);
        }

        public async Task StopAsync()
        {
            try
            {
                if (_browser != null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                    _page = null;
                    OnLogMessage("Tarayıcı tamamen kapatıldı.");
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Tarayıcı durdurulurken hata: {ex.Message}");
                // Hata olsa bile tarayıcı referanslarını temizle
                _browser = null;
                _page = null;
            }
        }

        public void ForceStopBrowser()
        {
            try
            {
                _browser?.CloseAsync();
                OnLogMessage("Tarayıcı zorla kapatıldı.");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Tarayıcı zorla kapatılırken hata: {ex.Message}");
            }
        }
    }

    public class PeriodInfo
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = false;
        public int ApprovalCount { get; set; } = 0;
    }

    public class SmsRecipientInfo : System.ComponentModel.INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _phone = string.Empty;
        private string _periodName = string.Empty;
        private bool _isSelected = true;
        private bool _isDuplicate = false;

        public string Id 
        { 
            get => _id; 
            set 
            { 
                _id = value; 
                OnPropertyChanged(nameof(Id)); 
            } 
        }
        
        public string Name 
        { 
            get => _name; 
            set 
            { 
                _name = value; 
                OnPropertyChanged(nameof(Name)); 
            } 
        }
        
        public string Phone 
        { 
            get => _phone; 
            set 
            { 
                _phone = value; 
                OnPropertyChanged(nameof(Phone)); 
                OnPropertyChanged(nameof(FormattedPhone)); 
            } 
        }
        
        public string FormattedPhone 
        { 
            get 
            { 
                if (string.IsNullOrEmpty(_phone)) return string.Empty;
                
                // Telefon numarasını temizle (sadece rakamları al)
                var cleanPhone = new string(_phone.Where(char.IsDigit).ToArray());
                
                if (cleanPhone.Length == 11 && cleanPhone.StartsWith("0"))
                {
                    // 0533 123 45 67 formatı için
                    var areaCode = cleanPhone.Substring(0, 4);
                    var firstPart = cleanPhone.Substring(4, 3);
                    var secondPart = cleanPhone.Substring(7, 2);
                    var thirdPart = cleanPhone.Substring(9, 2);
                    return $"({areaCode}) {firstPart} {secondPart} {thirdPart}";
                }
                else if (cleanPhone.Length == 10)
                {
                    // 533 123 45 67 formatı için
                    var areaCode = cleanPhone.Substring(0, 3);
                    var firstPart = cleanPhone.Substring(3, 3);
                    var secondPart = cleanPhone.Substring(6, 2);
                    var thirdPart = cleanPhone.Substring(8, 2);
                    return $"(0{areaCode}) {firstPart} {secondPart} {thirdPart}";
                }
                
                return _phone; // Formatlanamazsa orijinal numarayı döndür
            } 
        }
        
        public string PeriodName 
        { 
            get => _periodName; 
            set 
            { 
                _periodName = value; 
                OnPropertyChanged(nameof(PeriodName)); 
            } 
        }
        
        public bool IsSelected 
        { 
            get => _isSelected; 
            set 
            { 
                _isSelected = value; 
                OnPropertyChanged(nameof(IsSelected)); 
            } 
        }
        
        public bool IsDuplicate 
        { 
            get => _isDuplicate; 
            set 
            { 
                _isDuplicate = value; 
                OnPropertyChanged(nameof(IsDuplicate)); 
            } 
        }
        
        public SmsRecipientInfo()
        {
            _isSelected = false; // Varsayılan olarak seçili değil
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"{Name} - {Phone}";
        }
    }
} 