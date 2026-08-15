using System.Configuration;
using System.IO.Pipes;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace centre_app;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private CancellationTokenSource? _pipeCancellation;
    private string _pipeName = string.Empty;

    protected override void OnStartup(StartupEventArgs e)
    {
        var userId = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var instanceKey = userId.Replace('-', '_').Replace('\\', '_');
        _pipeName = $"Centre_{instanceKey}";
        _instanceMutex = new Mutex(true, $"Local\\Centre_{instanceKey}", out var firstInstance);
        if (!firstInstance)
        {
            SendShowCommand();
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
        _pipeCancellation = new CancellationTokenSource();
        _ = ListenForCommandsAsync(_pipeCancellation.Token);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCancellation?.Cancel();
        _pipeCancellation?.Dispose();
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void SendShowCommand()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine("SHOW");
        }
        catch { }
    }

    private async Task ListenForCommandsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(server);
                if (string.Equals(await reader.ReadLineAsync(cancellationToken), "SHOW", StringComparison.Ordinal))
                    await Dispatcher.InvokeAsync(() => (MainWindow as MainWindow)?.ShowFromExternalInstance());
            }
            catch (OperationCanceledException) { break; }
            catch when (!cancellationToken.IsCancellationRequested) { }
        }
    }
}
