using System;
using System.ComponentModel;
using System.Linq; // Added for .Where()

namespace WebScraper
{
    public class SmsHistoryItem : INotifyPropertyChanged
    {
        private string _recipientName = string.Empty;
        private string _phoneNumber = string.Empty;
        private string _periodName = string.Empty;
        private DateTime _sentTime;
        private string _status = string.Empty;

        public string RecipientName 
        { 
            get => _recipientName; 
            set 
            { 
                _recipientName = value; 
                OnPropertyChanged(nameof(RecipientName)); 
            } 
        }
        
        public string PhoneNumber 
        { 
            get => _phoneNumber; 
            set 
            { 
                _phoneNumber = value; 
                OnPropertyChanged(nameof(PhoneNumber)); 
                OnPropertyChanged(nameof(FormattedPhone)); 
            } 
        }
        
        public string FormattedPhone 
        { 
            get 
            { 
                if (string.IsNullOrEmpty(_phoneNumber)) return string.Empty;
                
                // Telefon numarasını temizle (sadece rakamları al)
                var cleanPhone = new string(_phoneNumber.Where(char.IsDigit).ToArray());
                
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
                
                return _phoneNumber; // Formatlanamazsa orijinal numarayı döndür
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
        
        public DateTime SentTime 
        { 
            get => _sentTime; 
            set 
            { 
                _sentTime = value; 
                OnPropertyChanged(nameof(SentTime)); 
                OnPropertyChanged(nameof(FormattedSentTime)); 
            } 
        }
        
        public string FormattedSentTime 
        { 
            get 
            { 
                return _sentTime.ToString("dd.MM.yyyy HH:mm:ss"); 
            } 
        }
        
        public string Status 
        { 
            get => _status; 
            set 
            { 
                _status = value; 
                OnPropertyChanged(nameof(Status)); 
            } 
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public override string ToString()
        {
            return $"{RecipientName} - {FormattedPhone} - {PeriodName} - {FormattedSentTime}";
        }
    }
} 