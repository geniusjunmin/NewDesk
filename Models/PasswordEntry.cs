using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NewDesk.Models;

public class PasswordEntry : INotifyPropertyChanged
{
    private Guid _id = Guid.NewGuid();
    private string _title = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _notes = string.Empty;

    // Vault 2.0 Extended Properties
    private string _websiteUrl = string.Empty;
    private List<string> _tags = new();
    private string? _totpSecret;
    private DateTime _lastModified = DateTime.Now;

    public Guid Id { get => _id; set => SetField(ref _id, value); }
    public string Title { get => _title; set => SetField(ref _title, value); }
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string Password { get => _password; set => SetField(ref _password, value); }
    public string Notes { get => _notes; set => SetField(ref _notes, value); }

    public string WebsiteUrl { get => _websiteUrl; set => SetField(ref _websiteUrl, value); }
    public List<string> Tags { get => _tags; set => SetField(ref _tags, value); }
    public string? TotpSecret { get => _totpSecret; set => SetField(ref _totpSecret, value); }
    public DateTime LastModified { get => _lastModified; set => SetField(ref _lastModified, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
