namespace OpenFilesMonitor.Models
{
    public class ServerConfig
    {
        public string ServerName { get; set; } = "";
        public string Username { get; set; } = "";
        public string Password { get; set; } = ""; // stored encrypted on disk via SettingsService
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Username) ? ServerName : $"{ServerName} ({Username})";
        }
    }
}