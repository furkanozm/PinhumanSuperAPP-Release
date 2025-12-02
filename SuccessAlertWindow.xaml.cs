using System.Windows;
using System.Windows.Threading;

namespace WebScraper
{
    public partial class SuccessAlertWindow : Window
    {
        private DispatcherTimer _timer;
        private DateTime _startTime;
        private const double TOTAL_DURATION = 2000; // 2 saniye = 2000ms
        private const double TIMER_INTERVAL = 10; // ~100fps için 10ms - daha smooth

        public SuccessAlertWindow(string message)
        {
            InitializeComponent();
            txtMessage.Text = message;
            
            // Pencereyi her zaman en üstte tut
            this.Topmost = true;
            this.ShowInTaskbar = false;
            
            // Pencereyi aktif hale getir ve öne getir
            this.Activate();
            this.Focus();
            
            // Başlangıç zamanını kaydet
            _startTime = DateTime.Now;
            
            // Timer başlat
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(TIMER_INTERVAL);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            // Pencereyi sürekli en üstte tut
            if (!this.Topmost)
            {
                this.Topmost = true;
            }
            
            // Geçen süreyi hesapla
            var elapsedTime = (DateTime.Now - _startTime).TotalMilliseconds;
            var progress = Math.Min((elapsedTime / TOTAL_DURATION) * 100, 100);
            
            // Progress bar'ı güncelle
            progressBar.Value = progress;
            
            // Kalan süreyi hesapla
            var remainingTime = TOTAL_DURATION - elapsedTime;
            var remainingSeconds = (int)Math.Ceiling(remainingTime / 1000.0);
            
            // Geri sayım text'ini güncelle
            if (remainingSeconds > 0)
            {
                txtCountdown.Text = $"{remainingSeconds} saniye sonra kapanacak...";
            }
            else
            {
                txtCountdown.Text = "Kapanıyor...";
            }
            
            // Süre dolduysa kapat
            if (elapsedTime >= TOTAL_DURATION)
            {
                _timer.Stop();
                this.Close();
            }
        }

        private void btnOK_Click(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            this.Close();
        }
    }
} 