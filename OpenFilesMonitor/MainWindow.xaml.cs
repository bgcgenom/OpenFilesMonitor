using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using OpenFilesMonitor.Services;
using OpenFilesMonitor.Models;

namespace OpenFilesMonitor
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<ServerConfig> _servers = new();
        private readonly ObservableCollection<OpenFileEntry> _allRows = new();
        private readonly List<OpenFileEntry> _lastRows = new();   // backing for live filter
        private readonly DispatcherTimer _timer = new();

        public MainWindow()
        {
            InitializeComponent();

            ServersList.ItemsSource = _servers;
            ResultsGrid.ItemsSource = _allRows;

            foreach (var s in SettingsService.LoadServersDecrypted())
                if (s != null) _servers.Add(s);

            _timer.Tick += async (s, e) => await RefreshAsync();
            ChkAuto.Checked += (s, e) => StartTimer();
            ChkAuto.Unchecked += (s, e) => _timer.Stop();

            // Live filter as user types
            TxtFilter.TextChanged += (s, e) => ApplyFilter();

            this.Closed += (s, e) => SettingsService.Save(_servers);

            Title = $"Open Files Monitor v{GetVersion()}";
            StatusText.Text = $"Ready — v{GetVersion()}";
        }

        private static string GetVersion()
        {
            try
            {
                var exePath =
                    System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName
                    ?? System.AppContext.BaseDirectory;

                var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                return fvi.ProductVersion ?? "1.2.0";
            }
            catch
            {
                return "1.2.0";
            }
        }

        private void StartTimer()
        {
            if (int.TryParse(TxtInterval.Text, out int sec) && sec > 0)
            {
                _timer.Interval = TimeSpan.FromSeconds(sec);
                _timer.Start();
            }
            else
            {
                MessageBox.Show("Please enter a valid auto-refresh interval (seconds).");
                ChkAuto.IsChecked = false;
            }
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

        private async Task RefreshAsync()
        {
            try
            {
                StatusText.Text = "Refreshing...";

                var tasks = _servers.Select(s => Task.Run(() => QueryServer(s))).ToArray();
                var all = await Task.WhenAll(tasks);

                var rows = new List<OpenFileEntry>();
                foreach (var list in all)
                    if (list != null) rows.AddRange(list);

                _lastRows.Clear();
                _lastRows.AddRange(rows);

                ApplyFilter();

                StatusText.Text = $"Last updated {DateTime.Now:T}. Rows: {_allRows.Count} (total: {_lastRows.Count})";
            }
            catch (Exception ex)
            {
                StatusText.Text = "Error while refreshing";
                MessageBox.Show(ex.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyFilter()
        {
            try
            {
                var filter = TxtFilter.Text?.Trim();
                IEnumerable<OpenFileEntry> view = _lastRows;

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    var f = filter.ToLowerInvariant();
                    view = view.Where(r =>
                        (!string.IsNullOrEmpty(r.User) && r.User.ToLowerInvariant().Contains(f)) ||
                        (!string.IsNullOrEmpty(r.Path) && r.Path.ToLowerInvariant().Contains(f)) ||
                        (!string.IsNullOrEmpty(r.Server) && r.Server.ToLowerInvariant().Contains(f)) ||
                        (!string.IsNullOrEmpty(r.Client) && r.Client.ToLowerInvariant().Contains(f)) ||
                        (!string.IsNullOrEmpty(r.Name) && r.Name.ToLowerInvariant().Contains(f)) ||
                        (!string.IsNullOrEmpty(r.SharePath) && r.SharePath.ToLowerInvariant().Contains(f))
                    );
                }

                var sorted = view.OrderBy(r => r.Server).ThenBy(r => r.User).ThenBy(r => r.Path).ToList();

                _allRows.Clear();
                foreach (var r in sorted) _allRows.Add(r);
            }
            catch
            {
                // ignore transient UI issues while typing
            }
        }

        // --------- Querying logic ---------

        private static bool HasCred(ServerConfig cfg) =>
            !string.IsNullOrWhiteSpace(cfg.Username) && !string.IsNullOrEmpty(cfg.Password);

        private static CimSession CreateCimSessionWsman(ServerConfig cfg)
        {
            if (HasCred(cfg))
            {
                var cred = MakeCimCredential(cfg);
                var ws = new WSManSessionOptions();
                ws.AddDestinationCredentials(cred);
                return CimSession.Create(cfg.ServerName, ws);
            }
            return CimSession.Create(cfg.ServerName);
        }

        private static CimSession CreateCimSessionDcom(ServerConfig cfg)
        {
            var dcom = new DComSessionOptions();
            if (HasCred(cfg))
            {
                var cred = MakeCimCredential(cfg);
                dcom.AddDestinationCredentials(cred);
            }
            return CimSession.Create(cfg.ServerName, dcom);
        }

        // Enumerate instances instead of WQL to avoid "Invalid query" from some providers
        private List<OpenFileEntry> QueryServer(ServerConfig cfg)
        {
            try
            {
                static IEnumerable<CimInstance> Enumerate(CimSession s)
                    => s.EnumerateInstances(@"root\Microsoft\Windows\SMB", "MSFT_SmbOpenFile");

                var items = new List<CimInstance>();

                // WSMan first
                try
                {
                    using var wsman = CreateCimSessionWsman(cfg);
                    items.AddRange(Enumerate(wsman));
                }
                catch
                {
                    // try DCOM next
                }

                // DCOM fallback
                if (items.Count == 0)
                {
                    try
                    {
                        using var dcom = CreateCimSessionDcom(cfg);
                        items.AddRange(Enumerate(dcom));
                    }
                    catch (CimException cex)
                    {
                        return new List<OpenFileEntry> {
                            new OpenFileEntry {
                                Server = cfg.ServerName,
                                User   = "ERROR",
                                Path   = $"CIM enumerate failed: {cex.Message}. " +
                                         @"Verify class MSFT_SmbOpenFile exists in root\Microsoft\Windows\SMB and that your account has rights.",
                                FileId = 0,
                                Name   = ""
                            }
                        };
                    }
                    catch (Exception ex)
                    {
                        return new List<OpenFileEntry> {
                            new OpenFileEntry { Server = cfg.ServerName, User = "ERROR", Path = ex.Message, FileId = 0, Name = "" }
                        };
                    }
                }

                if (items.Count > 0)
                {
                    string S(object? v) => v?.ToString() ?? string.Empty;

                    return items.Select(i =>
                    {
                        var path = S(i.CimInstanceProperties["Path"]?.Value);
                        var rel = S(i.CimInstanceProperties["ShareRelativePath"]?.Value);
                        var share = S(i.CimInstanceProperties["ShareName"]?.Value);
                        var sharePathProp = S(i.CimInstanceProperties["SharePath"]?.Value);

                        // Prefer provider's SharePath; otherwise construct it.
                        var sharePath = !string.IsNullOrWhiteSpace(sharePathProp)
                            ? sharePathProp
                            : (!string.IsNullOrWhiteSpace(share) && !string.IsNullOrWhiteSpace(rel)
                                ? $@"\\{cfg.ServerName}\{share}\{rel}"
                                : (!string.IsNullOrWhiteSpace(share)
                                    ? $@"\\{cfg.ServerName}\{share}"
                                    : string.Empty));

                        var name = DeriveName(rel, path);

                        return new OpenFileEntry
                        {
                            Server = cfg.ServerName,
                            User = S(i.CimInstanceProperties["ClientUserName"]?.Value),
                            Client = S(i.CimInstanceProperties["ClientComputerName"]?.Value),
                            Path = path,
                            FileId = ConvertToULong(i.CimInstanceProperties["FileId"]?.Value),
                            Name = name,
                            ShareName = share,
                            ShareRelativePath = rel,
                            SharePath = sharePath
                        };
                    }).ToList();
                }

                // No data returned
                return new List<OpenFileEntry>
                {
                    new OpenFileEntry
                    {
                        Server = cfg.ServerName,
                        User = "(info)",
                        Path = "No open files returned by MSFT_SmbOpenFile.",
                        FileId = 0,
                        Name = ""
                    }
                };
            }
            catch (Exception ex)
            {
                return new List<OpenFileEntry> {
                    new OpenFileEntry { Server = cfg.ServerName, User = "ERROR", Path = ex.Message, FileId = 0, Name = "" }
                };
            }
        }

        private static ulong ConvertToULong(object? v)
        {
            try
            {
                if (v == null) return 0;
                if (v is ulong uu) return uu;
                if (v is long ll) return unchecked((ulong)ll);
                if (ulong.TryParse(v.ToString(), out var p)) return p;
                return 0;
            }
            catch { return 0; }
        }

        private static CimCredential MakeCimCredential(ServerConfig cfg)
        {
            string domain, user;
            var parts = (cfg.Username ?? "").Split('\\', 2);
            if (parts.Length == 2) { domain = parts[0]; user = parts[1]; }
            else { domain = ""; user = cfg.Username ?? ""; }

            return new CimCredential(PasswordAuthenticationMechanism.Default,
                domain, user, ToSecureString(cfg.Password ?? ""));
        }

        private static SecureString ToSecureString(string s)
        {
            var ss = new SecureString();
            if (s != null) foreach (var c in s) ss.AppendChar(c);
            ss.MakeReadOnly();
            return ss;
        }

        // --------- UI actions ---------

        private void BtnAddServer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ServerDialog();
            if (dlg.ShowDialog() == true && dlg.Config is ServerConfig cfg)
            {
                _servers.Add(cfg);
                SettingsService.Save(_servers);
            }
        }

        private void BtnRemoveServer_Click(object sender, RoutedEventArgs e)
        {
            if (ServersList.SelectedItem is ServerConfig cfg)
            {
                _servers.Remove(cfg);
                SettingsService.Save(_servers);
            }
        }

        private void BtnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            TxtFilter.Text = string.Empty; // TextChanged will trigger ApplyFilter
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                        $"OpenFiles_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                var sb = new StringBuilder();
                sb.AppendLine("Server,User,Client,Name,Path,FileId");
                foreach (var r in _allRows)
                {
                    static string esc(string x) => "\""+ (x ?? string.Empty).Replace("\"", "\"\"") + "\"";
                    sb.AppendLine($"{esc(r.Server)},{esc(r.User)},{esc(r.Client)},{esc(r.Name)},{esc(r.Path)},{r.FileId}");
                }
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                StatusText.Text = $"Exported to {path}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Export error");
            }
        }

        private async void BtnCloseSel_Click(object sender, RoutedEventArgs e) => await CloseSelectedAsync(false);
        private async void BtnCloseSelForce_Click(object sender, RoutedEventArgs e) => await CloseSelectedAsync(true);

        private async Task CloseSelectedAsync(bool force)
        {
            try
            {
                var sel = ResultsGrid.SelectedItems.Cast<OpenFileEntry>().ToList();
                if (sel.Count == 0)
                {
                    MessageBox.Show("Select one or more rows to close.", "No selection",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Expand selection: if user picked one handle, close all handles that refer to the same SharePath(s).
                // If SharePath is empty on your servers, this will be skipped and we close only selected rows' FileIds.
                var sharePaths = sel.Select(x => x.SharePath)
                                    .Where(p => !string.IsNullOrWhiteSpace(p))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (sharePaths.Count > 0)
                {
                    sel = _allRows.Where(r => !string.IsNullOrWhiteSpace(r.SharePath) && sharePaths.Contains(r.SharePath))
                                  .ToList();
                }

                var groups = sel.Where(x => x.FileId != 0).GroupBy(x => x.Server, StringComparer.OrdinalIgnoreCase);
                int total = 0, ok = 0, fail = 0;
                string? lastError = null;

                foreach (var g in groups)
                {
                    var server = g.Key;
                    var cfg = _servers.FirstOrDefault(s =>
                        s.ServerName.Equals(server, StringComparison.OrdinalIgnoreCase));

                    if (cfg == null) continue;

                    var ids = g.Select(x => x.FileId).Distinct().ToList();
                    total += ids.Count;

                    var res = await Task.Run(() => CloseFilesViaPowerShellExe(cfg, ids, force));
                    ok += res.ok;
                    fail += res.fail;
                    lastError = res.lastError ?? lastError;
                }

                StatusText.Text = $"Close complete: OK={ok}, Failed={fail} (requested {total}).";

                if (fail > 0 && !string.IsNullOrWhiteSpace(lastError))
                {
                    MessageBox.Show(lastError, "Close failed (last error)",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                await RefreshAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "Close error");
            }
        }

        // Close using external powershell.exe (Windows PowerShell) to call Close-SmbOpenFile.
        // This avoids the PowerShell SDK runspace crash and uses the supported SMB cmdlet.
        private static (int ok, int fail, string? lastError) CloseFilesViaPowerShellExe(ServerConfig cfg, List<ulong> fileIds, bool force)
        {
            int ok = 0, fail = 0;
            string? lastError = null;

            try
            {
                if (!HasCred(cfg))
                    return (0, fileIds.Count, "No credentials stored for this server. Add credentials then try again.");

                var ids = string.Join(",", fileIds);

                string user = cfg.Username ?? "";
                string pass = cfg.Password ?? "";
                string server = cfg.ServerName ?? "";

                // Escape single quotes for PowerShell single-quoted strings
                static string esc(string s) => (s ?? "").Replace("'", "''");

                var script = $@"
$ErrorActionPreference = 'Stop'
$sec  = ConvertTo-SecureString '{esc(pass)}' -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential('{esc(user)}', $sec)

$opt = New-CimSessionOption -Protocol WSMan
$cs  = New-CimSession -ComputerName '{esc(server)}' -Credential $cred -SessionOption $opt -ErrorAction Stop

$ok=0; $fail=0
try {{
  $ids = @({ids})
  foreach($id in $ids) {{
    try {{
      Close-SmbOpenFile -CimSession $cs -FileId $id {(force ? "-Force" : "")} -ErrorAction Stop | Out-Null
      $ok++
    }} catch {{
      $fail++
    }}
  }}
}} finally {{
  if ($cs) {{ Remove-CimSession $cs -ErrorAction SilentlyContinue }}
}}

[pscustomobject]@{{ ok=$ok; fail=$fail }} | ConvertTo-Json -Compress
";

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = System.Diagnostics.Process.Start(psi)!;
                p.StandardInput.Write(script);
                p.StandardInput.Close();

                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();

                p.WaitForExit();

                if (p.ExitCode != 0)
                {
                    lastError = string.IsNullOrWhiteSpace(stderr) ? $"powershell.exe exited {p.ExitCode}" : stderr.Trim();
                    return (0, fileIds.Count, lastError);
                }

                // Parse {"ok":N,"fail":M}
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(stdout);
                    ok = doc.RootElement.GetProperty("ok").GetInt32();
                    fail = doc.RootElement.GetProperty("fail").GetInt32();
                    return (ok, fail, null);
                }
                catch
                {
                    return (0, fileIds.Count, "Close ran but output could not be parsed. Output: " + stdout);
                }
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                return (0, fileIds.Count, lastError);
            }
        }

        private static string DeriveName(string rel, string path)
        {
            static string LastSegment(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return "";
                s = s.TrimEnd('\\', '/');
                int i = Math.Max(s.LastIndexOf('\\'), s.LastIndexOf('/'));
                return i >= 0 ? s[(i + 1)..] : s;
            }

            static bool LooksLikeFile(string s)
            {
                if (string.IsNullOrWhiteSpace(s)) return false;
                if (s.EndsWith(".", StringComparison.Ordinal)) return false;
                return s.Contains('.');
            }

            var fromPath = LastSegment(path ?? "");
            var fromRel = LastSegment(rel ?? "");

            if (LooksLikeFile(fromPath)) return fromPath;
            if (LooksLikeFile(fromRel)) return fromRel;

            return !string.IsNullOrEmpty(fromRel) ? fromRel : fromPath;
        }
    }
}
