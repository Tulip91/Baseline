using Microsoft.Win32;
using System.Windows;

namespace BaseLine.Infrastructure;

public interface IFileDialogService
{
    string? PickProfileToOpen();
    string? PickProfileToSave(string suggestedFileName);
}

public sealed class FileDialogService : IFileDialogService
{
    public string? PickProfileToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Baseline profile (*.baseline.json)|*.baseline.json|JSON (*.json)|*.json",
            Title = "Open Baseline profile"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickProfileToSave(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Baseline profile (*.baseline.json)|*.baseline.json|JSON (*.json)|*.json",
            Title = "Save Baseline profile",
            FileName = suggestedFileName
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}

public interface IMessageDialogService
{
    void ShowInfo(string message, string title);
    void ShowError(string message, string title);
    bool Confirm(string message, string title);
}

public sealed class MessageDialogService : IMessageDialogService
{
    public void ShowInfo(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void ShowError(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    public bool Confirm(string message, string title) => MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
