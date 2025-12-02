using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace WebScraper
{
    /// <summary>
    /// Sözleşmeli personel için ViewModel - SOLID Single Responsibility, MVVM pattern
    /// </summary>
    public class SozPersonelViewModel : INotifyPropertyChanged, IDisposable
    {
        private SozPersonelService _service;
        private SozPersonelExcelProcessor _excelProcessor;
        private SozPersonelSettings _settings;
        private List<Dictionary<string, string>> _excelData;
        private bool _isProcessingEnabled;

        public SozPersonelViewModel()
        {
            _service = new SozPersonelService();
            _excelProcessor = new SozPersonelExcelProcessor();
            _settings = new SozPersonelSettings();
            _excelData = new List<Dictionary<string, string>>();
            _isProcessingEnabled = false;
        }

        public SozPersonelSettings Settings
        {
            get => _settings;
            set
            {
                _settings = value;
                OnPropertyChanged();
            }
        }

        public List<Dictionary<string, string>> ExcelData
        {
            get => _excelData;
            set
            {
                _excelData = value;
                OnPropertyChanged();
                IsProcessingEnabled = _excelData != null && _excelData.Count > 0;
            }
        }

        public bool IsProcessingEnabled
        {
            get => _isProcessingEnabled;
            set
            {
                _isProcessingEnabled = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Excel dosyasından veri yükler
        /// </summary>
        public void LoadExcelData(string filePath, Action<string> logCallback)
        {
            try
            {
                logCallback($"📂 Sözleşmeli personel Excel dosyası yükleniyor: {System.IO.Path.GetFileName(filePath)}");

                ExcelData = _excelProcessor.LoadSozPersonelDataFromExcel(filePath);

                logCallback($"✅ {ExcelData.Count} adet sözleşmeli personel verisi yüklendi");
                logCallback("🚀 Sözleşmeli personel işlemi başlatılabilir");
            }
            catch (Exception ex)
            {
                logCallback($"❌ Excel okuma hatası: {ex.Message}");
                IsProcessingEnabled = false;
            }
        }

        /// <summary>
        /// Sözleşmeli personel işlemini başlatır
        /// </summary>
        public async void StartProcess(Action<string> logCallback)
        {
            if (ExcelData == null || ExcelData.Count == 0)
            {
                logCallback("⚠️ İşlenecek sözleşmeli personel verisi bulunamadı");
                return;
            }

            await _service.StartSozPersonelProcessAsync(Settings, ExcelData, logCallback);
        }

        /// <summary>
        /// Kaynakları temizler
        /// </summary>
        public async void Cleanup()
        {
            await _service.CleanupBrowserAsync();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            _service?.Dispose();
            _excelProcessor = null;
        }
    }
}
