using OpenFilesMonitor.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OpenFilesMonitor.Services
{
    internal static class SettingsService
    {
        private static string AppDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenFilesMonitor");

        private static string ServersFile => Path.Combine(AppDir, "servers.json");

        private class ServerConfigStored
        {
            public string ServerName { get; set; } = "";
            public string Username { get; set; } = "";
            public string PasswordEnc { get; set; } = "";
        }

        public static List<ServerConfig> LoadServersDecrypted()
        {
            Directory.CreateDirectory(AppDir);
            if (!File.Exists(ServersFile)) return new();

            try
            {
                var json = File.ReadAllText(ServersFile);
                var stored = JsonSerializer.Deserialize<List<ServerConfigStored>>(json) ?? new();
                return stored.Select(s => new ServerConfig
                {
                    ServerName = s.ServerName ?? "",
                    Username = s.Username ?? "",
                    Password = Dpapi.Unprotect(s.PasswordEnc ?? "")
                }).Where(x => !string.IsNullOrWhiteSpace(x.ServerName)).ToList();
            }
            catch
            {
                return new();
            }
        }

        public static void Save(IEnumerable<ServerConfig> servers)
        {
            Directory.CreateDirectory(AppDir);
            var stored = servers.Select(s => new ServerConfigStored
            {
                ServerName = s.ServerName ?? "",
                Username = s.Username ?? "",
                PasswordEnc = Dpapi.Protect(s.Password ?? "")
            }).ToList();

            var json = JsonSerializer.Serialize(stored, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ServersFile, json);
        }
    }
}