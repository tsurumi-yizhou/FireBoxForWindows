using System;
using System.Text;
using System.Threading.Tasks;
using Demo.Search;
using Demo.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Demo;

public partial class App : Application
{
    private Window? _window;
    private DispatcherQueue? _uiDispatcherQueue;

    public static Client.FireBoxClient Client { get; private set; } = null!;
    public static string? ConnectionError { get; private set; }
    public static Window? MainWindowInstance { get; private set; }
    public static ConversationJsonlStore ConversationStore { get; } = new();

    public App()
    {
        InitializeComponent();
        _uiDispatcherQueue = DispatcherQueue.GetForCurrentThread();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            ConnectionError = null;
            _window = CreateWindow();
            MainWindowInstance = _window;
            _window.Activate();

            var client = new Client.FireBoxClient();
            try
            {
                client.Connect();
                Client = client;
            }
            catch (Exception ex)
            {
                ConnectionError = ex.Message;
            }

            if (_window is MainWindow mainWindow)
            {
                try
                {
                    mainWindow.UpdateStatus();
                }
                catch (Exception ex)
                {
                    mainWindow.ShowFatalError("Failed to finish startup", FormatExceptionForDisplay(ex));
                }
            }
        }
        catch (Exception ex)
        {
            EnsureWindow().Content = BuildErrorContent(
                "FireChatBox failed to start",
                FormatExceptionForDisplay(ex));
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        ShowGuiException("Unexpected UI error", e.Exception);
    }

    private void OnAppDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception
            ?? new InvalidOperationException("A non-Exception object was thrown.");

        ShowGuiException("Unexpected fatal error", exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        ShowGuiException("Background task failed", e.Exception);
    }

    private Window CreateWindow()
    {
        try
        {
            var searchService = new ConversationSearchService();
            return new MainWindow(searchService);
        }
        catch (Exception ex)
        {
            return new Window
            {
                Title = "FireChatBox",
                Content = BuildErrorContent(
                    "FireChatBox UI failed to initialize",
                    FormatExceptionForDisplay(ex)),
            };
        }
    }

    private Window EnsureWindow()
    {
        if (_window is not null)
            return _window;

        _window = new Window { Title = "FireChatBox" };
        MainWindowInstance = _window;
        _window.Activate();
        return _window;
    }

    private void ShowGuiException(string title, Exception exception)
    {
        var message = FormatExceptionForDisplay(exception);

        void Show()
        {
            var window = EnsureWindow();
            if (window is MainWindow mainWindow)
            {
                mainWindow.ShowFatalError(title, message);
                return;
            }

            window.Content = BuildErrorContent(title, message);
        }

        if (_uiDispatcherQueue?.HasThreadAccess == true)
        {
            Show();
            return;
        }

        if (_uiDispatcherQueue is not null)
        {
            _uiDispatcherQueue.TryEnqueue(Show);
            return;
        }

        Show();
    }

    private static UIElement BuildErrorContent(string title, string message)
    {
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
        };

        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 28,
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = "FireChatBox hit an exception. Details are shown below so the failure is visible in the GUI.",
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBox
        {
            Text = message,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 220,
        });

        return new ScrollViewer
        {
            Content = panel,
        };
    }

    private static string FormatExceptionForDisplay(Exception exception)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"{exception.GetType().Name}: {exception.Message}");

        if (exception.InnerException is not null)
        {
            builder.AppendLine();
            builder.AppendLine("Inner exception:");
            builder.AppendLine($"{exception.InnerException.GetType().Name}: {exception.InnerException.Message}");
        }

        if (!string.IsNullOrWhiteSpace(exception.StackTrace))
        {
            builder.AppendLine();
            builder.AppendLine("Stack trace:");
            builder.AppendLine(exception.StackTrace);
        }

        return builder.ToString();
    }
}
