using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Newtonsoft.Json;
using static PasswordFileFinderGUI.SearchConstants;

namespace PasswordFileFinderGUI
{
    public partial class Password_finder : Form

    {
        private StringBuilder searchLog = new StringBuilder();

        // Global token to manage local search cancellation
        private CancellationTokenSource _searchCts = new CancellationTokenSource();

        // Global token to manage network listener cancellation
        private CancellationTokenSource _listenerCts;
        private TcpListener _listener;
        private X509Certificate2 _serverCertificate;
        private const int SERVER_PORT = 55001;

        // --- 1. CERTIFICATE PATH (Updated for subdirectory access) ---
        private const string CERT_PATH = "certificate\\server.pfx";

        // --- 2. CERTIFICATE PASSWORD (Updated to your custom password) ---
        private const string CERT_PASS = "12dSecretp@swdd";

        // Global collection of checked-in clients
        public static ConcurrentDictionary<string, RemoteClientInfo> CheckedInClients
            = new ConcurrentDictionary<string, RemoteClientInfo>();

        public Password_finder()
        {
            InitializeComponent();

            txtUserExtensions.PlaceholderText = ".key, .db, .zip";
            // Uses SearchConstants
            txtDirectories.PlaceholderText = $"Leave blank to search: {string.Join(", ", DefaultSearchDirectories)}";
            txtContentKeywords.PlaceholderText = "e.g., secret_key, azure_cert, sftp";

            // Uses SearchConstants
            chkDefaultExtensions.Text = $"Include Default Password Extensions ({string.Join(", ", DefaultPasswordExtensions.Take(4))}...)";
            chkDefaultExtensions.Checked = true;
        }

        // --- PUBLIC METHODS FOR REMOTE CLIENT FORM ACCESS ---

        /// <summary>
        /// Packages the current search criteria from the main form's UI into a command object.
        /// </summary>
        public RemoteSearchCommand GetCurrentSearchCommand()
        {   

            // --- 1. Get Extensions ---
            List<string> searchExtensions = new List<string>();
            if (chkDefaultExtensions.Checked)
            {
                searchExtensions.AddRange(SearchConstants.DefaultPasswordExtensions);
            }
            List<string> userExtensions = ParseInput(txtUserExtensions.Text);
            searchExtensions.AddRange(userExtensions.Where(ext => !searchExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)));

            // --- 2. Get Keywords ---
            List<string> finalContentKeywords = new List<string>();
            finalContentKeywords.AddRange(SearchConstants.BaseContentKeywords);
            List<string> userKeywords = ParseInput(txtContentKeywords.Text);
            finalContentKeywords.AddRange(userKeywords.Where(k => !finalContentKeywords.Contains(k, StringComparer.OrdinalIgnoreCase)));

            // --- 3. Get Directories ---
            List<string> searchDirectories = ParseInput(txtDirectories.Text, true);

            return new RemoteSearchCommand
            {
                SearchDirectories = searchDirectories.Any() ? searchDirectories : SearchConstants.DefaultSearchDirectories,
                SearchExtensions = searchExtensions.Select(ext => ext.Trim()).ToList(),
                ContentKeywords = finalContentKeywords
            };
        }

        /// <summary>
        /// Displays remote search results safely on the main thread (used by RemoteClientForm).
        /// </summary>
        public void DisplayRemoteResults(RemoteSearchResult result)
        {
            // Ensure thread safety for UI update
            Invoke((MethodInvoker)delegate
            {
                // This method safely accesses the private txtResults control
                txtResults.AppendText($"\r\n=== REMOTE RESULTS from {result.HostName} ({result.FoundFiles.Count} hits) ===\r\n");

                if (result.AccessDeniedEncountered)
                {
                    txtResults.AppendText($"[WARNING] Access denied errors encountered on {result.HostName}.\r\n");
                }

                if (result.FoundFiles.Any())
                {
                    foreach (var fileEntry in result.FoundFiles)
                    {
                        txtResults.AppendText($"[MATCH: {fileEntry.KeywordMatch}] {fileEntry.FilePath}\r\n");
                    }
                }
                else
                {
                    txtResults.AppendText("No sensitive files found.\r\n");
                }
                txtResults.SelectionStart = txtResults.Text.Length;
                txtResults.ScrollToCaret();
            });
        }

        /// <summary>
        /// Logs simple activity messages safely to the results textbox (used by RemoteClientForm).
        /// </summary>
        public void LogRemoteActivity(string message)
        {
            Invoke((MethodInvoker)delegate
            {
                txtResults.AppendText(message);
                txtResults.SelectionStart = txtResults.Text.Length;
                txtResults.ScrollToCaret();
            });
        }

        // *** NETWORK LISTENER MANAGEMENT METHODS ***
        public void StartRemoteListener()
        {
            if (_listenerCts != null && !_listenerCts.IsCancellationRequested) return;

            try
            {
                // Load certificate using the updated path and password
                _serverCertificate = new X509Certificate2(CERT_PATH, CERT_PASS);

                _listenerCts = new CancellationTokenSource();
                Task.Run(() => ListenerLoop(_listenerCts.Token));

                Invoke((MethodInvoker)delegate { txtResults.AppendText($"[INFO] Remote Listener started on port {SERVER_PORT}.\r\n"); });
            }
            catch (Exception ex)
            {
                // Logs the specific error if file or password is wrong
                Invoke((MethodInvoker)delegate { txtResults.AppendText($"[CRITICAL] Listener failed to start: {ex.Message}.\r\n"); });
            }
        }

        public void StopRemoteListener()
        {
            _listenerCts?.Cancel();
            _listener?.Stop();
        }

        private async Task ListenerLoop(CancellationToken token)
        {
            _listener = new TcpListener(IPAddress.Any, SERVER_PORT);
            try
            {
                _listener.Start();

                while (!token.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(token);
                    _ = Task.Run(() => HandleClientConnection(client, token));
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when listener is stopped
            }
            finally
            {
                _listener?.Stop();
                Invoke((MethodInvoker)delegate { txtResults.AppendText($"[INFO] Remote Listener stopped.\r\n"); });
            }
        }

        private async Task HandleClientConnection(TcpClient client, CancellationToken token)
        {
            using (client)
            using (var sslStream = new SslStream(client.GetStream(), false))
            {
                try
                {
                    await sslStream.AuthenticateAsServerAsync(_serverCertificate);

                    // 1. Read the BeaconData
                    string jsonIn = await NetworkHelper.ReadMessageAsync(sslStream, token);
                    if (string.IsNullOrEmpty(jsonIn) || token.IsCancellationRequested) return;

                    BeaconData beaconData = JsonConvert.DeserializeObject<BeaconData>(jsonIn);

                    // 2. Register/Update Client
                    RemoteClientInfo newClientInfo = new RemoteClientInfo
                    {
                        HostName = beaconData.HostName,
                        IpAddress = beaconData.IpAddress,
                        LastCheckIn = DateTime.Now,
                        IsSelected = false
                    };

                    CheckedInClients.AddOrUpdate(
                        newClientInfo.ConnectionKey, newClientInfo,
                        (key, existingVal) => { existingVal.LastCheckIn = DateTime.Now; return existingVal; }
                    );

                    // 3. Send ACK
                    await NetworkHelper.WriteMessageAsync(sslStream, "ACK", token);

                    Invoke((MethodInvoker)delegate { txtResults.AppendText($"[BEACON] Client checked in: {newClientInfo.ConnectionKey}.\r\n"); });
                }
                catch (Exception ex)
                {
                    Invoke((MethodInvoker)delegate { txtResults.AppendText($"[NET ERR] {ex.Message}.\r\n"); });
                }
            }
        }

        // --- Local Search Methods (Unchanged core logic) ---
        private async void btnSearch_Click(object sender, EventArgs e)
        {
            _searchCts = new CancellationTokenSource();

            searchLog.Clear();
            txtResults.Text = "Starting search...\r\n";
            btnSearch.Enabled = false;
            btnStopSearch.Enabled = true;

            // ... (Search setup logic remains the same) ...
            List<string> searchExtensions = new List<string>();

            if (chkDefaultExtensions.Checked)
            {
                searchExtensions.AddRange(DefaultPasswordExtensions);
                txtResults.AppendText("Default file extensions included.\r\n");
            }

            List<string> userExtensions = ParseInput(txtUserExtensions.Text);
            searchExtensions.AddRange(userExtensions.Where(ext => !searchExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)));

            if (!searchExtensions.Any())
            {
                MessageBox.Show("Please include the default list or enter additional file extensions.", "Missing Input", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtResults.Text = "Search failed: No extensions defined.";
                btnSearch.Enabled = true;
                btnStopSearch.Enabled = false;
                return;
            }

            List<string> searchPatterns = searchExtensions.Select(ext => $"*{ext.Trim()}").ToList();
            txtResults.AppendText($"Searching for {searchPatterns.Count} unique file types.\r\n");

            List<string> searchDirectories = ParseInput(txtDirectories.Text, true);

            if (!searchDirectories.Any())
            {
                searchDirectories.AddRange(DefaultSearchDirectories);
                txtResults.AppendText($"No directories entered; using defaults: {string.Join(", ", DefaultSearchDirectories)}\r\n");
            }

            List<string> finalContentKeywords = new List<string>();
            finalContentKeywords.AddRange(BaseContentKeywords);
            List<string> userKeywords = ParseInput(txtContentKeywords.Text);
            finalContentKeywords.AddRange(userKeywords.Where(k => !finalContentKeywords.Contains(k, StringComparer.OrdinalIgnoreCase)));

            if (!finalContentKeywords.Any())
            {
                txtResults.AppendText("Error: No content keywords defined for internal file search.\r\n");
                btnSearch.Enabled = true;
                btnStopSearch.Enabled = false;
                return;
            }

            // --- 4. Execute Search ASYNCHRONOUSLY ---
            List<(string FilePath, string KeywordMatch)> foundFiles = new List<(string, string)>();
            bool accessDeniedEncountered = false;
            bool searchAborted = false;

            txtResults.AppendText($"\r\nSearching file contents for {finalContentKeywords.Count} combined keywords...\r\n");

            try
            {
                await Task.Run(() =>
                {
                    foreach (string directory in searchDirectories)
                    {
                        if (_searchCts.Token.IsCancellationRequested) return;

                        if (!Directory.Exists(directory))
                        {
                            searchLog.AppendLine($"[SKIPPED] Directory not found: {directory}");
                            continue;
                        }

                        Invoke((MethodInvoker)delegate { txtResults.AppendText($"\r\nSearching in: {directory}...\r\n"); });

                        foreach (string pattern in searchPatterns)
                        {
                            if (_searchCts.Token.IsCancellationRequested) return;

                            try
                            {
                                var safeFiles = SafelyEnumerateFiles(directory, pattern, _searchCts.Token);

                                foreach (string filePath in safeFiles.FoundFiles)
                                {
                                    if (_searchCts.Token.IsCancellationRequested) return;

                                    string matchingKeyword = CheckFileContent(filePath, finalContentKeywords);
                                    if (matchingKeyword != null)
                                    {
                                        foundFiles.Add((filePath, matchingKeyword));
                                    }
                                }

                                if (safeFiles.AccessDeniedOccurred)
                                {
                                    accessDeniedEncountered = true;
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                searchLog.AppendLine($"[ERROR] An unexpected error occurred: {ex.Message}");
                            }
                        }
                    }
                }, _searchCts.Token);
            }
            catch (OperationCanceledException)
            {
                searchAborted = true;
            }
            finally
            {
                btnSearch.Enabled = true;
                btnStopSearch.Enabled = false;
            }

            // --- 5. Display Results based on Abort State ---
            if (searchAborted)
            {
                txtResults.Text = "USER ABORTED SEARCH: No results will be displayed.";
                return;
            }

            searchLog.ToString();
            txtResults.AppendText(searchLog.ToString());

            if (foundFiles.Any())
            {
                txtResults.AppendText($"\r\n--- SEARCH COMPLETE: FOUND FILES WITH KEYWORDS ({foundFiles.Distinct().Count()} Total) ---\r\n");
                foreach (var fileEntry in foundFiles.Distinct())
                {
                    txtResults.AppendText($"[MATCH: {fileEntry.KeywordMatch}] {fileEntry.FilePath}\r\n");
                }
            }
            else
            {
                txtResults.AppendText("\r\n--- SEARCH COMPLETE: NOTHING FOUND. ---\r\n");
            }

            // --- 6. Prompt for Administrator Access ---
            if (accessDeniedEncountered)
            {
                MessageBox.Show(
                    "The search encountered restricted directories (Access Denied).",
                    "Permissions Issue",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }
        }

        private void btnStopSearch_Click(object sender, EventArgs e)
        {
            _searchCts.Cancel();
        }

        // *** Menu handlers ***
        private void remoteChecksToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (RemoteClientForm remoteForm = new RemoteClientForm())
            {
                remoteForm.ShowDialog();
            }
        }

        private void btnSaveResults_Click_Click(object sender, EventArgs e)
        {
            // ... (Save results logic remains the same) ...
            if (string.IsNullOrWhiteSpace(txtResults.Text))
            {
                MessageBox.Show("The results box is empty. Run a search first.", "Cannot Save Empty File", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (SaveFileDialog saveFileDialog = new SaveFileDialog())
            {
                saveFileDialog.Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*";
                saveFileDialog.FileName = "PasswordFinder_Results.txt";
                saveFileDialog.Title = "Save Search Results";

                if (saveFileDialog.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        File.WriteAllText(saveFileDialog.FileName, txtResults.Text);
                        MessageBox.Show($"Results successfully saved to:\n{saveFileDialog.FileName}", "Save Successful", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"An error occurred while saving the file: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        // --- Helper Function 1: Safely Enumerate Files ---
        private (List<string> FoundFiles, bool AccessDeniedOccurred) SafelyEnumerateFiles(string rootPath, string searchPattern, CancellationToken token)
        {
            // ... (Logic remains the same) ...
            List<string> files = new List<string>();
            bool accessDenied = false;

            try
            {
                if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();

                files.AddRange(Directory.GetFiles(rootPath, searchPattern, SearchOption.TopDirectoryOnly));

                foreach (string dir in Directory.GetDirectories(rootPath))
                {
                    if (token.IsCancellationRequested) token.ThrowIfCancellationRequested();

                    try
                    {
                        var result = SafelyEnumerateFiles(dir, searchPattern, token);
                        files.AddRange(result.FoundFiles);
                        if (result.AccessDeniedOccurred)
                        {
                            accessDenied = true;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        searchLog.AppendLine($"    [Skipping] Access denied to: {dir}");
                        accessDenied = true;
                    }
                    catch (PathTooLongException)
                    {
                        searchLog.AppendLine($"    [Skipping] Path too long: {dir}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                accessDenied = true;
            }
            catch (DirectoryNotFoundException)
            {
            }

            return (files, accessDenied);
        }

        // --- Helper Function 2: Check File Content ---
        private string CheckFileContent(string filePath, List<string> keywords)
        {
            // ... (Logic remains the same) ...
            const long MaxFileSize = 10 * 1024 * 1024; // 10 MB limit

            try
            {
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > MaxFileSize)
                {
                    searchLog.AppendLine($"    [Skipping Content Check] File too large: {filePath}");
                    return null;
                }

                string content = File.ReadAllText(filePath);
                string lowerContent = content.ToLowerInvariant();

                foreach (string keyword in keywords)
                {
                    if (lowerContent.Contains(keyword.ToLowerInvariant()))
                    {
                        return keyword;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // --- Helper Function 3: Parse Input ---
        private List<string> ParseInput(string input, bool preserveCase = false)
        {
            // ... (Logic remains the same) ...
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            return input.Split(',')
                .Select(s => preserveCase ? s.Trim() : s.Trim().ToLowerInvariant())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList();
        }
    }
}