using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PasswordFileFinderGUI
{
    public partial class RemoteClientForm : Form
    {
        // NOTE: The _mainForm field is kept because it's used successfully 
        // throughout the existing form logic (InitializeClientGrid, UpdateClientList, etc.)
        private Password_finder _mainForm;
        private System.Windows.Forms.Timer _updateTimer;
        private BindingList<RemoteClientInfo> _clientBindingList;

        public RemoteClientForm()
        {
            InitializeComponent();
            this.Text = "Remote Client Manager";

            // Get reference to the main form instance
            _mainForm = Application.OpenForms.OfType<Password_finder>().FirstOrDefault();

            InitializeClientGrid();
            InitializeUpdateTimer();
        }

        private void InitializeClientGrid()
        {
            dgvClients.AutoGenerateColumns = false;
            dgvClients.MultiSelect = false;
            dgvClients.AllowUserToAddRows = false;
            dgvClients.RowHeadersVisible = false;

            // Manual column definitions
            dgvClients.Columns.Add(new DataGridViewCheckBoxColumn { HeaderText = "Select", Name = "ColSelect", DataPropertyName = "IsSelected", Width = 50 });
            dgvClients.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Host Name", Name = "ColHost", DataPropertyName = "HostName", ReadOnly = true, Width = 150 });
            dgvClients.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "IP Address", Name = "ColIP", DataPropertyName = "IpAddress", ReadOnly = true, Width = 150 });
            dgvClients.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Last Check-in", Name = "ColCheckIn", DataPropertyName = "LastCheckIn", ReadOnly = true, Width = 150 });

            _clientBindingList = new BindingList<RemoteClientInfo>();
            dgvClients.DataSource = _clientBindingList;
        }

        private void InitializeUpdateTimer()
        {
            _updateTimer = new System.Windows.Forms.Timer();
            _updateTimer.Interval = 5000; // Update every 5 seconds
            _updateTimer.Tick += UpdateClientList;
            _updateTimer.Start();
        }

        private void UpdateClientList(object sender, EventArgs e)
        {
            if (_mainForm == null) return;

            // Note: This logic assumes Password_finder.CheckedInClients is a static property
            var currentGlobalClients = Password_finder.CheckedInClients.Values.ToList();

            dgvClients.SuspendLayout();

            // Sync current items with global collection
            foreach (var globalClient in currentGlobalClients)
            {
                var existing = _clientBindingList.FirstOrDefault(c => c.ConnectionKey == globalClient.ConnectionKey);

                if (existing == null)
                {
                    // Add new client to the UI list
                    _clientBindingList.Add(globalClient);
                }
                else
                {
                    // Update existing client data (Last Check-in)
                    existing.LastCheckIn = globalClient.LastCheckIn;

                    // Crucially, update the global collection with the UI's selection state
                    // This ensures the main form's list maintains selection across updates.
                    globalClient.IsSelected = existing.IsSelected;
                }
            }

            // Remove any clients that have disappeared (optional, but handles service shutdown)
            var clientsToRemove = _clientBindingList.Where(c => !currentGlobalClients.Any(g => g.ConnectionKey == c.ConnectionKey)).ToList();
            foreach (var client in clientsToRemove)
            {
                _clientBindingList.Remove(client);
            }

            dgvClients.ResumeLayout();
            dgvClients.Invalidate(); // Refresh the grid display
        }

        // --- Event Handlers ---

        private void remoteClientForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            _updateTimer.Stop();
        }

        private void btnStartListener_Click(object sender, EventArgs e)
        {
            if (_mainForm == null)
            {
                MessageBox.Show("Main application form reference is missing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _mainForm.StartRemoteListener();
            btnStartListener.Enabled = false;
            btnStopListnr.Enabled = true;
        }

        private void btnStopListnr_Click(object sender, EventArgs e)
        {
            if (_mainForm == null)
            {
                MessageBox.Show("Main application form reference is missing.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _mainForm.StopRemoteListener();
            btnStopListnr.Enabled = false;
            btnStartListener.Enabled = true;

            // Clear the client list since we're no longer listening
            _clientBindingList.Clear();

            _mainForm.LogRemoteActivity("[LISTENER] Remote listener stopped by user.\r\n");
        }

        private void chkSelectAll_CheckedChanged(object sender, EventArgs e)
        {
            // Update all clients in the binding list based on the main checkbox state
            bool select = chkSelectAll.Checked;
            foreach (var client in _clientBindingList)
            {
                client.IsSelected = select;
            }
            // Update the global collection to reflect the selection change immediately
            foreach (var client in Password_finder.CheckedInClients.Values)
            {
                client.IsSelected = select;
            }

            dgvClients.Invalidate();
        }

        // --- UPDATED SEND SEARCH LOGIC ---

        private async void btnSendSearch_Click(object sender, EventArgs e)
        {
            if (_mainForm == null) return;

            // 1. Get the current search command from the main form's UI
            RemoteSearchCommand command = _mainForm.GetCurrentSearchCommand();
            string commandJson = JsonConvert.SerializeObject(command);

            // 2. Identify selected clients from the Binding List (UI)
            var selectedClients = _clientBindingList.Where(c => c.IsSelected).ToList();

            if (!selectedClients.Any())
            {
                MessageBox.Show("Please select at least one client to send the search command.", "No Clients Selected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnSendSearch.Enabled = false;

            // USE LogRemoteActivity for initial dispatch message
            _mainForm.LogRemoteActivity($"\r\n*** Dispatching search command to {selectedClients.Count} client(s)... ***\r\n");

            // 3. Dispatch the command to each selected client in parallel
            var dispatchTasks = selectedClients.Select(client =>
                SendRemoteCommandAsync(client, commandJson)
            ).ToList();

            await Task.WhenAll(dispatchTasks);

            btnSendSearch.Enabled = true;
            _mainForm.LogRemoteActivity("\r\n*** Remote dispatch complete. Waiting for client results... ***\r\n");
        }

        /// <summary>
        /// Connects to a remote client over SSL, sends the command, and receives the result.
        /// </summary>
        private async Task SendRemoteCommandAsync(RemoteClientInfo client, string commandJson)
        {
            if (_mainForm == null) return;

            TcpClient tcpClient = null;
            CancellationTokenSource cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)); // 5 minutes for large searches

            try
            {
                tcpClient = new TcpClient();

                // Enable keep-alive to prevent connection drops during long searches
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                // Log connection attempt
                _mainForm.LogRemoteActivity($"[CONNECTING] Attempting to connect to {client.HostName} ({client.IpAddress}:55002)...\r\n");

                // 1. Connect to the client using its IP address on the communication port (55002)
                await tcpClient.ConnectAsync(client.IpAddress, 55002);

                _mainForm.LogRemoteActivity($"[TCP OK] TCP connection established to {client.HostName}.\r\n");

                using (var sslStream = new SslStream(
                    tcpClient.GetStream(),
                    false,
                    ValidateServerCertificate,
                    null))
                {
                    try
                    {
                        // CRITICAL FIX: Authenticate as CLIENT (this app) connecting to SERVER (remote service)
                        // The remote service acts as SSL server on port 55002
                        _mainForm.LogRemoteActivity($"[SSL] Starting SSL handshake with {client.HostName}...\r\n");

                        await sslStream.AuthenticateAsClientAsync(client.IpAddress);

                        _mainForm.LogRemoteActivity($"[SSL OK] SSL handshake completed with {client.HostName}.\r\n");
                    }
                    catch (Exception sslEx)
                    {
                        _mainForm.LogRemoteActivity($"[SSL ERROR] SSL handshake failed with {client.HostName}: {sslEx.Message}\r\n");
                        throw;
                    }

                    // 2. Send the Command (e.g., "SEARCH")
                    await NetworkHelper.WriteMessageAsync(sslStream, "SEARCH", cts.Token);

                    // 3. Send the JSON payload
                    await NetworkHelper.WriteMessageAsync(sslStream, commandJson, cts.Token);

                    // USE LogRemoteActivity for confirmation
                    _mainForm.LogRemoteActivity($"[CMD SENT] Search command sent to {client.HostName}. Waiting for results...\r\n");

                    // 4. Wait for results from the client
                    string resultJson = await NetworkHelper.ReadMessageAsync(sslStream, cts.Token);

                    if (!string.IsNullOrEmpty(resultJson))
                    {
                        // 5. Deserialize and call the public DisplayRemoteResults method
                        RemoteSearchResult result = JsonConvert.DeserializeObject<RemoteSearchResult>(resultJson);

                        // Ensure hostname is set (fallback to client info if needed)
                        if (string.IsNullOrEmpty(result.HostName))
                        {
                            result.HostName = client.HostName;
                        }

                        if (string.IsNullOrEmpty(result.IpAddress))
                        {
                            result.IpAddress = client.IpAddress;
                        }

                        // USE DisplayRemoteResults for final results
                        _mainForm.DisplayRemoteResults(result);

                        _mainForm.LogRemoteActivity($"[RESULTS] Received {result.FoundFiles.Count} results from {result.HostName} ({result.IpAddress}) at {result.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC.\r\n");
                    }
                    else
                    {
                        _mainForm.LogRemoteActivity($"[WARNING] Received empty response from {client.HostName}.\r\n");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                _mainForm.LogRemoteActivity($"[TIMEOUT] Failed to get response from {client.HostName} within 30 seconds.\r\n");
            }
            catch (SocketException sockEx)
            {
                _mainForm.LogRemoteActivity($"[NETWORK ERROR] Socket error connecting to {client.HostName}: {sockEx.Message}\r\n");
            }
            catch (Exception ex)
            {
                _mainForm.LogRemoteActivity($"[ERROR] Error communicating with {client.HostName}: {ex.GetType().Name} - {ex.Message}\r\n");
            }
            finally
            {
                tcpClient?.Close();
                cts?.Dispose();
            }
        }

        /// <summary>
        /// Certificate validation callback - accepts all certificates for now (development/testing).
        /// In production, implement proper certificate validation.
        /// </summary>
        private bool ValidateServerCertificate(
            object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            // For self-signed certificates, accept all
            // In production, validate the certificate properly
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                _mainForm?.LogRemoteActivity($"[SSL WARN] Certificate validation warning: {sslPolicyErrors}\r\n");
            }
            return true; // Accept the certificate
        }
    }
}