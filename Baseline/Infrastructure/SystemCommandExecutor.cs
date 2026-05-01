using System.Diagnostics;
using System.Text;

namespace BaseLine.Infrastructure;

public interface ISystemCommandExecutor
{
    Task<CommandExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken = default);
}

public sealed class SystemCommandExecutor : ISystemCommandExecutor
{
    public async Task<CommandExecutionResult> ExecuteAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return new CommandExecutionResult(process.ExitCode, await stdoutTask, await stderrTask);
        }
        catch (Exception ex)
        {
            return new CommandExecutionResult(-1, string.Empty, ex.Message);
        }
    }
}

public sealed record CommandExecutionResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool IsSuccess => ExitCode == 0;
    public string ErrorSummary => string.IsNullOrWhiteSpace(StandardError)
        ? string.IsNullOrWhiteSpace(StandardOutput)
            ? $"Command exited with code {ExitCode}."
            : StandardOutput.Trim()
        : StandardError.Trim();
}
