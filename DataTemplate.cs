using System;
using System.Collections.Generic;

namespace WebScraper
{
    /// <summary>
    /// PDKS / ERP tarafında gelen farklı Excel yapılarının
    /// tek bir iç modele dönüştürülmesi için kullanılan veri şablonu tanımı.
    /// İlk sürüm sade tutuldu; ihtiyaç oldukça alanlar genişletilebilir.
    /// </summary>
    public class DataTemplate
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Şablonun yüksek seviye tipi (örn. PDKS log, yatay gün-saat, ERP ek tablo vs.)
        /// </summary>
        public string TemplateType { get; set; } = "Unknown";

        /// <summary>
        /// Kullanıcıya ipucu olarak gösterilecek kısa bilgi
        /// Örn. "Giriş/çıkış logu", "Yatay gün-saat tablosu"
        /// </summary>
        public string SourceHint { get; set; } = string.Empty;

        /// <summary>
        /// Dosyada beklenen sütun adları veya desenleri.
        /// Örn. [\"Sicil\", \"Ad Soyad\", \"TCKN\"] gibi.
        /// </summary>
        public List<string> ExpectedColumns { get; set; } = new();

        /// <summary>
        /// Yatay puantaj şablonlarında sembolik değerlerin saat karşılıkları.
        /// Örn. "x" => 7.5 gibi. Küçük/büyük harfe duyarsız kullanılır.
        /// </summary>
        public Dictionary<string, double> SymbolHourMap { get; set; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Yatay şablonlarda tatillerin zaten şablonda işaretli olup olmadığı.
        /// True ise, tatiller şablonda geliyor ve tatil hesaplaması yapılmaz.
        /// False ise, sistem tatilleri hesaplar.
        /// </summary>
        public bool HasHolidaysInTemplate { get; set; } = false;

        /// <summary>
        /// Basit görünüm için TemplateType'ı kullanıcı dostu yazıya çevirir.
        /// </summary>
        public string TemplateTypeDisplay
        {
            get
            {
                return TemplateType switch
                {
                    "PDKS_Log" => "PDKS Log",
                    "Horizontal_DailyHours" => "Yatay Gün-Saat",
                    _ => TemplateType
                };
            }
        }
    }
}


