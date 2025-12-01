using System;
using System.Linq;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.IO;
using System.Collections.Generic;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace RemoteSearchClient
{
    public class Program
    {
        // ---Beacon class ---
        public class BeaconData
        {
            public string HostName { get; set; } = Environment.MachineName;
            public string IpAddress { get; set; } = System.Net.Dns.GetHostEntry(Environment.MachineName).AddressList
                .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "Unknown";
            public string Status { get; set; } = "BEACONING";
        }

        // Default Configuration
        static string ConfigFileName = "Configuration.txt";
        static string ServiceName = "RemotePasswordSearchService";

        // --- CONSTANTS FOR COMMAND LISTENER (55002) ---
        public const int COMMAND_PORT = 55002;
        // Certificate path - will be properly resolved at runtime
        public const string CERT_FOLDER = "certificate";
        public const string CERT_FILENAME = "server.pfx";
        public const string CERT_PASS = "12dSecretp@swdd";
        // ------------------------------------------

        // --- MULTI-THREADING CONSTANT ---
        private const int MAX_SEARCH_THREADS = 20;
        // --------------------------------

        // FIX: Made Configuration public to resolve accessibility error
        public class Configuration
        {
            public string ServerIp { get; set; } = "127.0.0.1";
            public int Port { get; set; } = 55001;
        }

        static Configuration AppConfig = new Configuration();

        // --- MAIN ENTRY POINT (Reworked for Host Builder) ---
        static void Main(string[] args)
        {
            // 1. Initial Checks
            if (!IsAdministrator())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("This needs to be run as a local administrator or root, Exiting");
                Console.ResetColor();
                return;
            }
            if (args.Contains("--uninstall"))
            {
                UninstallService();
                return;
            }

            // 2. Load Configuration and Handle CLI Overrides
            LoadConfiguration(args);

            // 3. Mode Selection
            if (args.Contains("--run-service"))
            {
                // MODE A: Host Service Worker (This handles the SCM handshake, fixing FAILED 1053)
                CreateHostBuilder(args).Build().Run();
            }
            else
            {
                // MODE B: Interactive Installer
                RunInstaller();
            }
        }

        // --- HOST BUILDER ---
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .UseWindowsService() // Fixes FAILED 1053
                .ConfigureServices((hostContext, services) =>
                {
                    // Pass the configuration instance to the worker
                    services.AddSingleton(AppConfig);
                    services.AddHostedService<RemoteWorker>();
                });

        // --- ADMIN CHECK ---
        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        // --- UNINSTALL LOGIC ---
        public static void UninstallService()
        {
            Console.Title = "Remote Search Service Uninstaller";
            Console.WriteLine("--- Remote Search Service Uninstaller ---");

            try
            {
                Console.WriteLine($"Attempting to stop service '{ServiceName}'...");
                ExecuteCommand($"stop {ServiceName}");
                Thread.Sleep(2000);

                Console.WriteLine($"Attempting to delete service '{ServiceName}'...");

                ProcessStartInfo psi = new ProcessStartInfo("sc.exe", $"delete {ServiceName}");
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;

                var proc = Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (output.Contains("SUCCESS"))
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("Service removed successfully.");
                }
                else if (output.Contains("The specified service does not exist"))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("Service not found. Deletion successful (nothing to delete).");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Service deletion failed. Check the name and permissions.");
                    Console.WriteLine($"SC Output: {output}");
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Uninstall Error: {ex.Message}");
            }

            Console.ResetColor();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        // --- INSTALL SERVICE ---
        public static void InstallService()
        {
            try
            {
                // FIX: Use the null-forgiving operator (!)
                string exePath = Process.GetCurrentProcess().MainModule!.FileName;
                string binPath = $"\\\"{exePath}\\\" --run-service";

                Console.WriteLine($"Registering service '{ServiceName}'...");

                ExecuteCommand($"stop {ServiceName}");
                ExecuteCommand($"delete {ServiceName}");
                Thread.Sleep(2000);

                string createArgs = $"create {ServiceName} binPath= \"{binPath}\" start= auto";

                ProcessStartInfo psi = new ProcessStartInfo("sc.exe", createArgs);
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;

                var proc = Process.Start(psi);
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();

                if (output.Contains("SUCCESS"))
                {
                    Console.WriteLine("Service Registered Successfully.");
                    Thread.Sleep(5000);

                    Console.WriteLine("Attempting to start service immediately...");

                    string startArgs = $"start {ServiceName}";

                    ProcessStartInfo startPsi = new ProcessStartInfo("sc.exe", startArgs);
                    startPsi.UseShellExecute = false;
                    startPsi.RedirectStandardOutput = true;

                    var startProc = Process.Start(startPsi);
                    string startOutput = startProc.StandardOutput.ReadToEnd();
                    startProc.WaitForExit();

                    if (startOutput.Contains("START_PENDING") || startOutput.Contains("RUNNING"))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine("Service started successfully.");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("Service start failed or is pending. Check services.msc.");
                        Console.WriteLine($"SC Output: {startOutput}");
                        Console.ResetColor();
                    }
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("Service Registration Failed:");
                    Console.WriteLine(output);
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Install Error: {ex.Message}");
                Console.ResetColor();
            }
        }

        static void ExecuteCommand(string scArgs)
        {
            Process p = Process.Start(new ProcessStartInfo("sc.exe", scArgs) { CreateNoWindow = true, UseShellExecute = false });
            p.WaitForExit();
        }

        // --- INSTALLER MODE ---
        static void RunInstaller()
        {
            Console.Title = "Remote Search Service Installer";
            Console.WriteLine("--- Remote Search Service Setup ---");
            Console.WriteLine($"Target Server: {AppConfig.ServerIp}:{AppConfig.Port}");
            Console.WriteLine("-----------------------------------");

            // Check if certificate exists before installing
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string certFolder = Path.Combine(baseDir, CERT_FOLDER);
            string certPath = Path.Combine(certFolder, CERT_FILENAME);

            if (!File.Exists(certPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\nWARNING: Certificate not found at: {certPath}");
                Console.WriteLine("The service will not be able to accept search commands without this certificate.");
                Console.WriteLine("\nPlease:");
                Console.WriteLine($"1. Create a 'certificate' folder in: {baseDir}");
                Console.WriteLine($"2. Place '{CERT_FILENAME}' in that folder");
                Console.WriteLine($"3. Ensure the password is: {CERT_PASS}");
                Console.ResetColor();
                Console.WriteLine("\nDo you want to continue installation anyway? (y/n)");

                if (Console.ReadKey().Key != ConsoleKey.Y)
                {
                    Console.WriteLine("\n\nInstallation cancelled.");
                    Console.WriteLine("Press any key to exit...");
                    Console.ReadKey();
                    return;
                }
                Console.WriteLine("\n");
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Certificate found at: {certPath}");
                Console.ResetColor();
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("Installing service to run as 'LocalSystem' for stability.");
            Console.ResetColor();

            InstallService();

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        // --- CONFIGURATION ---
        static void LoadConfiguration(string[] args)
        {
            // 1. File Config
            if (File.Exists(ConfigFileName))
            {
                foreach (var line in File.ReadAllLines(ConfigFileName))
                {
                    var parts = line.Split('=');
                    if (parts.Length < 2) continue;
                    var key = parts[0].Trim().ToLower();
                    var val = string.Join("=", parts.Skip(1)).Trim();

                    if (key == "host") AppConfig.ServerIp = val;

                    // FIX: Use a local variable for the 'out' parameter to resolve build error
                    if (key == "port")
                    {
                        int tempPort = AppConfig.Port;
                        if (int.TryParse(val, out tempPort))
                        {
                            AppConfig.Port = tempPort;
                        }
                    }
                }
            }

            // 2. Override with CLI Args
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--host" && i + 1 < args.Length) AppConfig.ServerIp = args[i + 1];

                // FIX: Use a local variable for the 'out' parameter to resolve build error
                if (args[i] == "--port" && i + 1 < args.Length)
                {
                    int tempPort = AppConfig.Port;
                    if (int.TryParse(args[i + 1], out tempPort))
                    {
                        AppConfig.Port = tempPort;
                    }
                }
            }
        }

        // --- SEARCH CORE LOGIC (Multi-threaded implementation) ---
        public static RemoteSearchResult PerformSearch(RemoteSearchCommand parameters)
        {
            if (parameters == null) return new RemoteSearchResult
            {
                HostName = Environment.MachineName,
                IpAddress = System.Net.Dns.GetHostEntry(Environment.MachineName).AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "Unknown",
                TimestampUtc = DateTime.UtcNow
            };

            RemoteSearchResult result = new RemoteSearchResult
            {
                HostName = Environment.MachineName,
                IpAddress = System.Net.Dns.GetHostEntry(Environment.MachineName).AddressList
                    .FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.ToString() ?? "Unknown",
                TimestampUtc = DateTime.UtcNow,
                FoundFiles = new List<(string, string)>(),
                AccessDeniedEncountered = false
            };

            // 1. Collect all files from all directories first.
            var allFilesToScan = new ConcurrentBag<string>();
            bool anyAccessDenied = false;
            List<string> searchPatterns = parameters.SearchExtensions.Select(e => $"*{e.Trim()}").ToList();

            // Use Parallel.ForEach to scan directories concurrently
            Parallel.ForEach(parameters.SearchDirectories, new ParallelOptions { MaxDegreeOfParallelism = MAX_SEARCH_THREADS }, directory =>
            {
                if (!Directory.Exists(directory)) return;

                foreach (string pattern in searchPatterns)
                {
                    var safeFiles = SafelyEnumerateFiles(directory, pattern);

                    if (safeFiles.AccessDeniedOccurred)
                    {
                        anyAccessDenied = true;
                    }

                    foreach (var file in safeFiles.FoundFiles)
                    {
                        allFilesToScan.Add(file);
                    }
                }
            });

            // Update the result with any access issues found during enumeration
            result.AccessDeniedEncountered = anyAccessDenied;

            // 2. Scan the content of all collected files in parallel.
            var matchedFiles = new ConcurrentBag<(string FilePath, string KeywordMatch)>();

            Parallel.ForEach(allFilesToScan, new ParallelOptions { MaxDegreeOfParallelism = MAX_SEARCH_THREADS }, filePath =>
            {
                string match = CheckFileContent(filePath, parameters.ContentKeywords);
                if (match != null)
                {
                    matchedFiles.Add((filePath, match));
                }
            });

            // 3. Finalize results
            result.FoundFiles.AddRange(matchedFiles.ToList());

            return result;
        }

        // --- HELPERS ---
        private static (List<string> FoundFiles, bool AccessDeniedOccurred) SafelyEnumerateFiles(string rootPath, string searchPattern)
        {
            List<string> files = new List<string>();
            bool accessDenied = false;

            try
            {
                files.AddRange(Directory.GetFiles(rootPath, searchPattern, SearchOption.TopDirectoryOnly));
                foreach (string dir in Directory.GetDirectories(rootPath))
                {
                    try
                    {
                        var result = SafelyEnumerateFiles(dir, searchPattern);
                        files.AddRange(result.FoundFiles);
                        if (result.AccessDeniedOccurred) accessDenied = true;
                    }
                    catch (UnauthorizedAccessException) { accessDenied = true; }
                    catch { }
                }
            }
            catch (UnauthorizedAccessException) { accessDenied = true; }
            catch { }
            return (files, accessDenied);
        }

        private static string CheckFileContent(string filePath, List<string> keywords)
        {
            try
            {
                // 10 MB limit
                if (new FileInfo(filePath).Length > 10 * 1024 * 1024) return null;
                string content = File.ReadAllText(filePath).ToLowerInvariant();
                foreach (var k in keywords)
                {
                    if (content.Contains(k.ToLowerInvariant())) return k;
                }
            }
            catch { }
            return null;
        }

        public static string ReadPassword()
        {
            string pass = "";
            do
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Backspace && key.Key != ConsoleKey.Enter)
                {
                    pass += key.KeyChar;
                    Console.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && pass.Length > 0)
                {
                    pass = pass.Substring(0, (pass.Length - 1));
                    Console.Write("\b \b");
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
            } while (true);
            return pass;
        }

        // --- DATA CONTRACTS (Moved to DataStructures.cs, but kept here for file-less solution) ---
        public class RemoteSearchCommand
        {
            public List<string> SearchDirectories { get; set; } = new List<string>();
            public List<string> SearchExtensions { get; set; } = new List<string>();
            public List<string> ContentKeywords { get; set; } = new List<string>();
        }

        public class RemoteSearchResult
        {
            public string HostName { get; set; }
            public string IpAddress { get; set; }
            public DateTime TimestampUtc { get; set; }
            public List<(string FilePath, string KeywordMatch)> FoundFiles { get; set; } = new List<(string, string)>();
            public bool AccessDeniedEncountered { get; set; }
        }
    }
}