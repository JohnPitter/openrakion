using System;
using System.IO;
using System.Net.Mail;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;

namespace RakionServer.World
{
    public interface ICharacterDeleteNotifier
    {
        Task<bool> SendAsync(CharacterDeleteOutcome outcome);
    }

    public sealed class CharacterDeletePickupNotifier : ICharacterDeleteNotifier
    {
        private readonly WorldConfig.CharacterDeleteConfig _config;

        public CharacterDeletePickupNotifier(WorldConfig.CharacterDeleteConfig config) => _config = config;

        public async Task<bool> SendAsync(CharacterDeleteOutcome outcome)
        {
            if (!_config.Enabled || string.IsNullOrWhiteSpace(_config.PickupFolder))
            {
                Log.Error("character", "pickup de delete key não configurado para conta {0}", outcome.AccountName);
                return false;
            }

            try
            {
                Directory.CreateDirectory(_config.PickupFolder);
                string bodyTemplate = await LoadBodyTemplateAsync();
                using var message = new MailMessage(
                    new MailAddress(_config.Sender), new MailAddress(outcome.Email))
                {
                    Subject = _config.Subject,
                    Body = string.Format(bodyTemplate, outcome.DeleteKey, outcome.CharacterName),
                    IsBodyHtml = false
                };
                using var client = new SmtpClient
                {
                    DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
                    PickupDirectoryLocation = _config.PickupFolder
                };
                await client.SendMailAsync(message);
                Log.Ok("character", "delete key emitida para conta {0}, char {1}",
                    outcome.AccountName, outcome.CharacterName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("character", "falha ao emitir delete key para conta {0}: {1}",
                    outcome.AccountName, ex.Message);
                return false;
            }
        }

        private async Task<string> LoadBodyTemplateAsync()
        {
            if (string.IsNullOrWhiteSpace(_config.BodyFileName))
                return "Rakion character delete key: {0}";
            string path = _config.BodyFileName;
            if (!Path.IsPathRooted(path))
                path = Path.Combine(_config.BaseDirectory, path);
            if (!File.Exists(path))
                throw new FileNotFoundException("template de delete key não encontrado", path);
            string template = await File.ReadAllTextAsync(path);
            return string.IsNullOrWhiteSpace(template) ? "Rakion character delete key: {0}" : template;
        }
    }
}
