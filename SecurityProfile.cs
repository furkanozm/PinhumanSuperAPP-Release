using System.Collections.Generic;

namespace WebScraper
{
    public class SecurityProfile
    {
        public string PinHash { get; set; } = string.Empty;
        public List<BackupCodeEntry> BackupCodes { get; set; } = new List<BackupCodeEntry>();
        public List<SecurityQuestionEntry> SecurityQuestions { get; set; } = new List<SecurityQuestionEntry>();
    }

    public class BackupCodeEntry
    {
        public string CodeHash { get; set; } = string.Empty;
        public bool IsUsed { get; set; }
    }

    public class SecurityQuestionEntry
    {
        public string Question { get; set; } = string.Empty;
        public string AnswerHash { get; set; } = string.Empty;
    }
}

