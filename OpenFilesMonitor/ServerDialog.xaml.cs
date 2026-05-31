using OpenFilesMonitor.Models;
using System.Windows;

namespace OpenFilesMonitor
{
    public partial class ServerDialog : Window
    {
        public ServerConfig? Config { get; private set; }

        public ServerDialog()
        {
            InitializeComponent();
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            var server = TxtServer.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(server))
            {
                MessageBox.Show("Server name is required.");
                return;
            }

            Config = new ServerConfig
            {
                ServerName = server,
                Username = TxtUser.Text?.Trim() ?? "",
                Password = TxtPass.Password ?? ""
            };

            DialogResult = true;
        }
    }
}