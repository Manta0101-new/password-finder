using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace PasswordFileFinderGUI
{
    //passes and recieves search variables 
    public class RemoteSearchCommand
    {
        public List<string> SearchDirectories { get; set; }
        public List<string> SearchExtensions { get; set; }
        public List<string> ContentKeywords { get; set; }
    }

    /// <summary>
    /// Data structure containing the results returned from the remote client to the server.
    /// </summary>
    public class RemoteSearchResult
    {
        public string HostName { get; set; }
        public string IpAddress { get; set; }
        public DateTime TimestampUtc { get; set; }
        public List<(string FilePath, string KeywordMatch)> FoundFiles { get; set; } = new List<(string, string)>();
        public bool AccessDeniedEncountered { get; set; }
    }
    // --- 1. Network Data Structures ---

    /// <summary>
    /// Data structure for tracking clients on the server/main app.
    /// Implements INotifyPropertyChanged for clean DataGridView updates.
    /// </summary>
    public class RemoteClientInfo : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string HostName { get; set; }
        public string IpAddress { get; set; }

        private DateTime _lastCheckIn;
        public DateTime LastCheckIn
        {
            get => _lastCheckIn;
            set { _lastCheckIn = value; OnPropertyChanged(nameof(LastCheckIn)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public string ConnectionKey => $"{HostName}|{IpAddress}";

        protected void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>
    /// Data structure sent by the remote client during a beacon check-in.
    /// </summary>
    public class BeaconData
    {
        public string HostName { get; set; }
        public string IpAddress { get; set; }
        public string ClientVersion { get; set; }
    }

    // --- 2. Search Configuration Structures ---

    /// <summary>
    /// Holds static, reusable lists and constants for search logic.
    /// </summary>
    public static class SearchConstants
    {
        // Default directories to search if the user provides none.
        public static readonly List<string> DefaultSearchDirectories = new List<string>
        {
            "C:\\Users\\",
            "C:\\ProgramData\\",
            "C:\\inetpub\\wwwroot\\"
        };

        // File extensions commonly associated with password storage or sensitive configuration.
        public static readonly List<string> DefaultPasswordExtensions = new List<string>
        {
            ".cfg", ".conf", ".ini", ".log", ".txt",
            ".kdbx", ".keypass", ".pfx", ".p12", ".cer",
            ".json", ".xml", ".yaml", ".yml", ".bak",
            ".sqlite", ".db", ".dat", ".rdp", ".vnc"
        };

        // Keywords to search for *inside* the files.
        public static readonly List<string> BaseContentKeywords = new List<string>
        {
            "password", "secret", "apikey", "auth", "token", "hash",
            "key", "credential", "ssh", "private_key", "azure_key", "aws_access"
        };
    }
}