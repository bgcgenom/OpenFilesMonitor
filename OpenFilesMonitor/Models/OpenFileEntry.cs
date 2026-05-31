namespace OpenFilesMonitor.Models
{
    public class OpenFileEntry
    {
        public string Server { get; set; } = "";
        public string User { get; set; } = "";
        public string Client { get; set; } = "";
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string SharePath { get; set; } = "";
        public string ShareName { get; set; } = "";
        public string ShareRelativePath { get; set; } = "";
        public ulong FileId { get; set; }
    }
}