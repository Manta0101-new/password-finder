using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RemoteSearchClient
{
    // Alias for Program's inner classes for cleaner code
    using RemoteSearchCommand = Program.RemoteSearchCommand;
    using RemoteSearchResult = Program.RemoteSearchResult;

    public class RemoteWorker : BackgroundService
    {
        private readonly Program.Configuration _config;
        private static readonly object _logLock = new object();
        private static string _logFilePath;

        public RemoteWorker(Program.Configuration config)
        {
            _config = config;

            // Initialize log file in working directory
            string workingDir = AppDomain.CurrentDomain.BaseDirectory;
            _logFilePath = System.IO.Path.Combine(workingDir, $"RemoteSearchService_{DateTime.Now:yyyyMMdd}.log");

            LogMessage($"=== Service Starting at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
            LogMessage($"Log file: {_logFilePath}");
        }

        private static void LogMessage(string message)
        {
            string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";

            // Write to console
            Console.WriteLine(logEntry);

            // Write to file (thread-safe)
            lock (_logLock)
            {
                try
                {
                    System.IO.File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[LOG ERR] Failed to write to log file: {ex.Message}");
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            LogMessage($"[INFO] Service starting. Beacon target: {_config.ServerIp}:{_config.Port}");
            LogMessage($"[INFO] Command listener will start on port {Program.COMMAND_PORT}");

            // Task 1: Permanent Listener for incoming search commands (Server connects to Client on 55002)
            var commandListenerTask = StartCommandListenerAsync(stoppingToken);

            // Task 2: Initial and recurring beaconing (Client connects to Server on 55001)
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ConnectAndBeaconAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogMessage($"[BEACON ERR] {ex.Message}. Retrying in 1 minute...");
                    await Task.Delay(1000 * 60, stoppingToken);
                }
            }

            // Ensure the command listener task stops when the service shuts down
            await commandListenerTask;
        }

        // --- BEACONING LOGIC (Connects to Server on 55001) ---
        private async Task ConnectAndBeaconAsync(CancellationToken token)
        {
            LogMessage($"[BEACON] Attempting to connect to {_config.ServerIp}:{_config.Port}...");

            using (var client = new TcpClient())
            {
                try
                {
                    await client.ConnectAsync(_config.ServerIp, _config.Port);
                    LogMessage("[BEACON] TCP connection established.");
                }
                catch (Exception ex)
                {
                    LogMessage($"[BEACON ERR] TCP connection failed: {ex.Message}");
                    throw;
                }

                using (var sslStream = new SslStream(client.GetStream(), false, ValidateServerCertificate, null))
                {
                    try
                    {
                        // Client authenticates the server
                        await sslStream.AuthenticateAsClientAsync(_config.ServerIp);
                        LogMessage("[BEACON] SSL handshake completed.");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"[BEACON ERR] SSL handshake failed: {ex.Message}");
                        throw;
                    }

                    // 1. Send Beacon Data
                    var beaconData = new Program.BeaconData();
                    string beaconJson = JsonConvert.SerializeObject(beaconData);
                    await WriteMessageAsync(sslStream, beaconJson, token);

                    LogMessage($"[BEACON] Sent: Host={beaconData.HostName}, IP={beaconData.IpAddress}");

                    // 2. Check for Acknowledgment (Listening for server response)
                    string responseJson = await ReadMessageAsync(sslStream, token);

                    if (responseJson == "ACK")
                    {
                        LogMessage("[BEACON] Acknowledged by server.");
                    }
                    else
                    {
                        LogMessage($"[BEACON] Unexpected response: {responseJson}");
                    }
                }
            }

            // Wait for 1 minute before next beacon attempt
            await Task.Delay(1000 * 60, token);
        }

        // --- COMMAND LISTENER LOGIC (PASSIVE: Listens on 55002 for Server to connect) ---
        private async Task StartCommandListenerAsync(CancellationToken token)
        {
            X509Certificate2 clientCert;
            try
            {
                // Build the full path to the certificate using proper path separators
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string certFolder = System.IO.Path.Combine(baseDir, Program.CERT_FOLDER);
                string fullCertPath = System.IO.Path.Combine(certFolder, Program.CERT_FILENAME);

                LogMessage($"[INFO] Looking for certificate at: {fullCertPath}");

                if (!System.IO.File.Exists(fullCertPath))
                {
                    LogMessage($"[CRIT] Certificate file not found at: {fullCertPath}");
                    LogMessage($"[CRIT] Please place '{Program.CERT_FILENAME}' in the '{certFolder}' folder");
                    return;
                }

                // Load the shared certificate for the command listener (Client acts as server here)
                clientCert = new X509Certificate2(fullCertPath, Program.CERT_PASS);
                LogMessage($"[INFO] Certificate loaded successfully from {fullCertPath}");
            }
            catch (Exception ex)
            {
                LogMessage($"[CRIT] Failed to load certificate: {ex.Message}");
                LogMessage($"[CRIT] Make sure the certificate file exists and password '{Program.CERT_PASS}' is correct.");
                return;
            }

            var listener = new TcpListener(System.Net.IPAddress.Any, Program.COMMAND_PORT);

            try
            {
                listener.Start();
                LogMessage($"[INFO] Command Listener ACTIVE on Port {Program.COMMAND_PORT}");

                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        // Wait for the server (GUI app) to connect back to us
                        TcpClient client = await listener.AcceptTcpClientAsync(token);
                        LogMessage($"[CMD] Incoming connection from {client.Client.RemoteEndPoint}");

                        // Handle the command securely in a new task
                        _ = Task.Run(() => HandleCommandConnection(client, clientCert, token), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                LogMessage("[INFO] Command listener shutting down...");
            }
            catch (Exception ex)
            {
                LogMessage($"[CRIT] Command Listener Failed: {ex.Message}");
            }
            finally
            {
                listener?.Stop();
                LogMessage("[INFO] Command listener stopped.");
            }
        }

        // --- COMMAND HANDLING LOGIC ---
        private async Task HandleCommandConnection(TcpClient tcpClient, X509Certificate2 serverCert, CancellationToken token)
        {
            using (tcpClient)
            {
                // Enable keep-alive to prevent timeout during long searches
                tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

                using (var sslStream = new SslStream(tcpClient.GetStream(), false))
                {
                    try
                    {
                        LogMessage("[CMD] Starting SSL handshake...");

                        // This service acts as the SSL SERVER for incoming command connections
                        // The sender must connect as an SSL CLIENT
                        await sslStream.AuthenticateAsServerAsync(serverCert,
                            clientCertificateRequired: false,
                            checkCertificateRevocation: false);

                        LogMessage("[CMD] SSL handshake completed successfully.");

                        // 1. Read the command type ("SEARCH")
                        string commandType = await ReadMessageAsync(sslStream, token);
                        LogMessage($"[CMD] Received command type: {commandType}");

                        if (commandType == "SEARCH")
                        {
                            // 2. Read the JSON payload (RemoteSearchCommand)
                            string jsonPayload = await ReadMessageAsync(sslStream, token);
                            LogMessage($"[CMD] Received search payload ({jsonPayload.Length} bytes)");

                            var command = JsonConvert.DeserializeObject<RemoteSearchCommand>(jsonPayload);

                            LogMessage($"[CMD] Starting search: {command.SearchDirectories.Count} dirs, " +
                                            $"{command.SearchExtensions.Count} extensions, " +
                                            $"{command.ContentKeywords.Count} keywords");

                            // 3. Perform the multi-threaded search
                            var searchStartTime = DateTime.Now;
                            RemoteSearchResult result = Program.PerformSearch(command);
                            var searchDuration = DateTime.Now - searchStartTime;

                            LogMessage($"[SEARCH] Search completed in {searchDuration.TotalSeconds:F1} seconds. Found {result.FoundFiles.Count} matches.");
                            LogMessage($"[SEARCH] Result metadata: Host={result.HostName}, IP={result.IpAddress}, UTC={result.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC");

                            // 4. Send the result back as JSON
                            string resultJson = JsonConvert.SerializeObject(result);
                            LogMessage($"[RSP] Preparing to send {resultJson.Length} bytes of results...");

                            await WriteMessageAsync(sslStream, resultJson, token);
                            await sslStream.FlushAsync(token);

                            LogMessage($"[RSP] Successfully sent {result.FoundFiles.Count} results back to sender.");
                        }
                        else
                        {
                            LogMessage($"[CMD] Unknown command type: {commandType}");
                        }
                    }
                    catch (System.IO.IOException ioEx)
                    {
                        LogMessage($"[NET ERR] Connection lost during command handling: {ioEx.Message}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"[NET ERR] Command handling failed: {ex.GetType().Name} - {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            LogMessage($"[NET ERR] Inner exception: {ex.InnerException.Message}");
                        }
                    }
                }
            }
        }


        // --- Protocol Helpers ---

        private async Task<string> ReadMessageAsync(SslStream ssl, CancellationToken token)
        {
            byte[] lengthBuffer = new byte[4];
            int read = await ssl.ReadAsync(lengthBuffer, 0, 4, token);

            if (read == 0)
            {
                return string.Empty;
            }

            if (read < 4)
            {
                throw new InvalidOperationException($"Incomplete length header (got {read} bytes, expected 4)");
            }

            int length = BitConverter.ToInt32(lengthBuffer, 0);

            if (length < 0 || length > 100 * 1024 * 1024) // 100MB sanity check
            {
                throw new InvalidOperationException($"Invalid message length: {length}");
            }

            byte[] buffer = new byte[length];
            int bytesRead = 0;
            while (bytesRead < length)
            {
                int chunk = await ssl.ReadAsync(buffer, bytesRead, length - bytesRead, token);
                if (chunk == 0)
                {
                    throw new InvalidOperationException($"Connection closed while reading message (got {bytesRead}/{length} bytes)");
                }
                bytesRead += chunk;
            }
            return Encoding.UTF8.GetString(buffer);
        }

        private async Task WriteMessageAsync(SslStream ssl, string message, CancellationToken token)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            byte[] length = BitConverter.GetBytes(data.Length);
            await ssl.WriteAsync(length, 0, length.Length, token);
            await ssl.WriteAsync(data, 0, data.Length, token);
            await ssl.FlushAsync(token);
        }

        public static bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            // For production, implement proper certificate validation
            // For now, accept all certificates (insecure but functional for testing)
            if (sslPolicyErrors != SslPolicyErrors.None)
            {
                LogMessage($"[SSL WARN] Certificate validation issues: {sslPolicyErrors}");
            }
            return true;
        }
    }
}