using System.Collections.Generic;

namespace WebScraper
{
    /// <summary>
    /// Personel işlemleri için config sınıfı
    /// </summary>
    public class PersonnelConfig
    {
        public PersonnelSettings Personnel { get; set; } = new PersonnelSettings();
        public SozPersonelSettings SozPersonel { get; set; } = new SozPersonelSettings();
        public BrowserSettings Browser { get; set; } = new BrowserSettings();
        public TemplateSettings Templates { get; set; } = new TemplateSettings();
        public ExcelSettings Excel { get; set; } = new ExcelSettings();
        public ProcessingSettings Processing { get; set; } = new ProcessingSettings();
        public ValidationSettings Validation { get; set; } = new ValidationSettings();
        public NotificationSettings Notifications { get; set; } = new NotificationSettings();
    }

    /// <summary>
    /// Sözleşmeli personel ayarları
    /// </summary>
    public class SozPersonelSettings
    {
        public string BaseUrl { get; set; } = "https://www.pinhuman.net";
        public string SozPersonelUrl { get; set; } = "https://www.pinhuman.net/Employee";
        public string CreateUrl { get; set; } = "https://www.pinhuman.net/Employee/Create";
        public bool LoginEnabled { get; set; } = true;
        public bool AutoNavigate { get; set; } = true;

        // Login bilgileri
        public string FirmaKodu { get; set; } = "";
        public string KullaniciId { get; set; } = "";
        public string Sifre { get; set; } = "";
        public string ToptSecret { get; set; } = "";
        public bool HeadlessMode { get; set; } = false;
    }

    /// <summary>
    /// Personel sayfası ayarları
    /// </summary>
    public class PersonnelSettings
    {
        public string BaseUrl { get; set; } = "https://www.pinhuman.net";
        public string StaffUrl { get; set; } = "https://www.pinhuman.net/AgcStaff";
        public string CreateUrl { get; set; } = "https://www.pinhuman.net/AgcStaff/Create";
        public bool LoginEnabled { get; set; } = true;
        public bool AutoNavigate { get; set; } = true;

        // Login bilgileri
        public string FirmaKodu { get; set; } = "";
        public string KullaniciId { get; set; } = "";
        public string Sifre { get; set; } = "";
        public string TotpSecret { get; set; } = "";
    }

    /// <summary>
    /// Tarayıcı ayarları
    /// </summary>
    public class BrowserSettings
    {
        public bool HeadlessMode { get; set; } = false;
        public string BrowserType { get; set; } = "chromium";
        public int DefaultTimeout { get; set; } = 30000;
        public int NavigationTimeout { get; set; } = 60000;
        public int SlowMo { get; set; } = 100;
    }

    /// <summary>
    /// Şablon ayarları
    /// </summary>
    public class TemplateSettings
    {
        public string TemplatesDirectory { get; set; } = "templates";
        public string DefaultTemplateType { get; set; } = "InternalPersonnel";
        public bool AutoLoadTemplates { get; set; } = true;
        public int MaxTemplates { get; set; } = 50;
    }

    /// <summary>
    /// Excel ayarları
    /// </summary>
    public class ExcelSettings
    {
        public string DefaultExtension { get; set; } = ".xlsx";
        public int MaxRowsPerFile { get; set; } = 10000;
        public bool IncludeHeaders { get; set; } = true;
        public string DateFormat { get; set; } = "dd.MM.yyyy";
        public bool AutoValidateData { get; set; } = true;
    }

    /// <summary>
    /// İşleme ayarları
    /// </summary>
    public class ProcessingSettings
    {
        public int BatchSize { get; set; } = 10;
        public int DelayBetweenRecords { get; set; } = 2000;
        public int MaxRetries { get; set; } = 3;
        public bool ContinueOnError { get; set; } = false;
        public bool SaveProgress { get; set; } = true;
    }

    /// <summary>
    /// Validasyon ayarları
    /// </summary>
    public class ValidationSettings
    {
        public bool TCKNRequired { get; set; } = true;
        public bool EmailFormatCheck { get; set; } = true;
        public bool PhoneFormatCheck { get; set; } = true;
        public bool DateValidation { get; set; } = true;
        public bool RequiredFieldCheck { get; set; } = true;
    }

    /// <summary>
    /// Bildirim ayarları
    /// </summary>
    public class NotificationSettings
    {
        public bool ShowProgressDialog { get; set; } = true;
        public bool ShowCompletionMessage { get; set; } = true;
        public bool LogToFile { get; set; } = false;
        public string LogDirectory { get; set; } = "logs/personnel";
    }
}
