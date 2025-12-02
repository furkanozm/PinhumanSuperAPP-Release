using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WebScraper
{
    public class SecurityProfileService
    {
        private const string PROFILE_FILE = "security-profile.json";

        public bool ProfileExists()
        {
            return File.Exists(PROFILE_FILE);
        }

        public SecurityProfile? LoadProfile()
        {
            if (!ProfileExists())
            {
                return null;
            }

            try
            {
                var json = File.ReadAllText(PROFILE_FILE);
                var profile = JsonSerializer.Deserialize<SecurityProfile>(json);
                if (profile == null)
                {
                    return null;
                }

                profile.BackupCodes ??= new List<BackupCodeEntry>();
                profile.SecurityQuestions ??= new List<SecurityQuestionEntry>();
                return profile;
            }
            catch
            {
                return null;
            }
        }

        public void SaveProfile(SecurityProfile profile)
        {
            profile ??= new SecurityProfile();
            profile.BackupCodes ??= new List<BackupCodeEntry>();
            profile.SecurityQuestions ??= new List<SecurityQuestionEntry>();

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(profile, options);
            File.WriteAllText(PROFILE_FILE, json);
        }

        public static string Hash(string input)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input ?? string.Empty));
            return Convert.ToHexString(bytes);
        }

        public bool VerifyPin(SecurityProfile profile, string pin)
        {
            if (profile == null || string.IsNullOrEmpty(pin))
            {
                return false;
            }

            return string.Equals(profile.PinHash, Hash(pin), StringComparison.OrdinalIgnoreCase);
        }

        public (bool success, BackupCodeEntry? entry) TryConsumeBackupCode(SecurityProfile profile, string code)
        {
            if (profile?.BackupCodes == null || string.IsNullOrWhiteSpace(code))
            {
                return (false, null);
            }

            var hash = Hash(code);
            var entry = profile.BackupCodes.FirstOrDefault(b =>
                !b.IsUsed && string.Equals(b.CodeHash, hash, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
            {
                return (false, null);
            }

            entry.IsUsed = true;
            SaveProfile(profile);
            return (true, entry);
        }

        public bool VerifySecurityQuestions(SecurityProfile profile, string answer1, string answer2)
        {
            if (profile?.SecurityQuestions == null || profile.SecurityQuestions.Count < 2)
            {
                return false;
            }

            var normalizedAnswers = new[]
            {
                Hash(answer1?.Trim().ToLowerInvariant() ?? string.Empty),
                Hash(answer2?.Trim().ToLowerInvariant() ?? string.Empty)
            };

            return string.Equals(profile.SecurityQuestions[0].AnswerHash, normalizedAnswers[0], StringComparison.OrdinalIgnoreCase)
                   && string.Equals(profile.SecurityQuestions[1].AnswerHash, normalizedAnswers[1], StringComparison.OrdinalIgnoreCase);
        }

        public static string GeneratePin()
        {
            var rng = RandomNumberGenerator.Create();
            var bytes = new byte[4];
            rng.GetBytes(bytes);
            var value = BitConverter.ToUInt32(bytes, 0) % 1_000_000;
            return value.ToString("D6");
        }

        public static List<string> GenerateBackupCodes(int count = 6)
        {
            var codes = new List<string>();
            var rng = RandomNumberGenerator.Create();
            while (codes.Count < count)
            {
                var bytes = new byte[4];
                rng.GetBytes(bytes);
                var value = BitConverter.ToUInt32(bytes, 0) % 1_000_000;
                codes.Add(value.ToString("D6"));
            }
            return codes;
        }
    }
}

