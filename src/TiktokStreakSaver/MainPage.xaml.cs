using Microsoft.Maui.Controls.Shapes;
using TiktokStreakSaver.Models;
using TiktokStreakSaver.Services;

namespace TiktokStreakSaver;
[Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
public partial class MainPage : ContentPage
{
    private readonly SettingsService _settingsService;
    private readonly SessionService _sessionService;
    private bool _isCheckingSession = false;
    private bool _sessionCheckCompleted = false;
    private string? _editingFriendId;

    public MainPage()
    {
        InitializeComponent();
        _settingsService = new SettingsService();
        _sessionService = new SessionService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadSettings();
        LoadFriendsList();
        LoadHistory();
        UpdateStatus();
        
        // Check session status
        CheckSessionStatus();
    }

    private void CheckSessionStatus()
    {
        // If we already checked this session, just update the button state
        if (_sessionCheckCompleted)
        {
            UpdateLoginButtonState(_sessionService.IsSessionValid());
            return;
        }

        // On first install, default to not logged in
        // Only trust saved session if user has previously logged in successfully
        var lastCheck = _sessionService.GetLastCheckTime();
        if (lastCheck == null)
        {
            // Never checked before - assume not logged in
            _sessionCheckCompleted = true;
            UpdateLoginButtonState(false);
            return;
        }

        // Start session validation
        _isCheckingSession = true;
        _navigationCount = 0;
        UpdateLoginButtonState(false, isChecking: true);

#if ANDROID
        // Configure WebView for session check using helper
        TikTokWebViewHelper.ConfigureWebView(SessionCheckWebView);
        
        // Load messages page to check if we're logged in
        SessionCheckWebView.Source = TikTokWebViewHelper.MessagesUrl;
        
        // Set a timeout - if no redirect after 10 seconds, check current state
        _sessionCheckTimeout = Dispatcher.CreateTimer();
        _sessionCheckTimeout.Interval = TimeSpan.FromSeconds(10);
        _sessionCheckTimeout.Tick += OnSessionCheckTimeout;
        _sessionCheckTimeout.Start();
#else
        // On non-Android platforms, just check the saved session state
        _sessionCheckCompleted = true;
        UpdateLoginButtonState(_sessionService.IsSessionValid());
#endif
    }

    private int _navigationCount = 0;
#if ANDROID
    private IDispatcherTimer? _sessionCheckTimeout;
#endif

#if ANDROID
    private void OnSessionCheckTimeout(object? sender, EventArgs e)
    {
        _sessionCheckTimeout?.Stop();
        
        if (_isCheckingSession)
        {
            // Timeout reached - assume not logged in for safety
            _isCheckingSession = false;
            _sessionCheckCompleted = true;
            _sessionService.SetSessionValid(false);
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateLoginButtonState(false);
            });
        }
    }
#endif

    private void OnSessionCheckNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (!_isCheckingSession) return;
        
        _navigationCount++;
        
        // Use helper to check login status
        var result = TikTokWebViewHelper.CheckLoginStatus(e.Url);
        
        // If redirected to login, we're definitely not logged in
        if (result.IsValidUrl && e.Url?.ToLower().Contains("/login") == true)
        {
#if ANDROID
            _sessionCheckTimeout?.Stop();
#endif
            _isCheckingSession = false;
            _sessionCheckCompleted = true;
            
            TikTokWebViewHelper.UpdateSessionStatus(_sessionService, false);
            
            MainThread.BeginInvokeOnMainThread(() =>
            {
                UpdateLoginButtonState(false);
            });
            return;
        }
        
        // If we're on messages page and this is at least the 2nd navigation (after potential redirect)
        // then we're likely logged in
        if (result.IsLoggedIn && _navigationCount >= 1)
        {
            // Wait a bit more to ensure no further redirect to login
            Task.Delay(2000).ContinueWith(_ =>
            {
                if (_isCheckingSession)
                {
#if ANDROID
                    _sessionCheckTimeout?.Stop();
#endif
                    _isCheckingSession = false;
                    _sessionCheckCompleted = true;
                    
                    TikTokWebViewHelper.UpdateSessionStatus(_sessionService, true);
                    
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        UpdateLoginButtonState(true);
                    });
                }
            });
        }
    }

    private void UpdateLoginButtonState(bool isSessionValid, bool isChecking = false)
    {
        if (isChecking)
        {
            LoginButton.Text = "Checking session...";
            LoginButton.BackgroundColor = Color.FromArgb("#888888");
            LoginButton.IsEnabled = false;
            SessionCheckingIndicator.IsVisible = true;
            RunNowButton.IsEnabled = false;
            RunNowButton.Opacity = 0.5;
        }
        else if (isSessionValid)
        {
            LoginButton.Text = "Session Connected";
            LoginButton.BackgroundColor = Color.FromArgb("#4CAF50"); // Green
            LoginButton.IsEnabled = false;
            SessionCheckingIndicator.IsVisible = false;
            RunNowButton.IsEnabled = true;
            RunNowButton.Opacity = 1.0;
        }
        else
        {
            LoginButton.Text = "Login to TikTok";
            LoginButton.BackgroundColor = Color.FromArgb("#FE2C55"); // Primary red
            LoginButton.IsEnabled = true;
            SessionCheckingIndicator.IsVisible = false;
            RunNowButton.IsEnabled = false;
            RunNowButton.Opacity = 0.5;
        }
    }

    private void LoadSettings()
    {
        // Load message
        MessageEditor.Text = _settingsService.GetMessageText();

        // Load schedule state
        ScheduleSwitch.IsToggled = _settingsService.IsScheduled();
    }

    private void UpdateStatus()
    {
        var isScheduled = _settingsService.IsScheduled();
        var lastRun = _settingsService.GetLastRunTime();
        var friendsCount = _settingsService.GetEnabledFriends().Count;

        // Update status label
        if (isScheduled && friendsCount > 0)
        {
            StatusLabel.Text = $"Active • {friendsCount} friend{(friendsCount != 1 ? "s" : "")}";
            StatusLabel.TextColor = Color.FromArgb("#4CAF50");
        }
        else if (friendsCount == 0)
        {
            StatusLabel.Text = "Add friends to get started";
            StatusLabel.TextColor = Color.FromArgb("#FFC107");
        }
        else
        {
            StatusLabel.Text = "Scheduler disabled";
            StatusLabel.TextColor = Color.FromArgb("#888888");
        }

        // Update last run
        if (lastRun.HasValue)
        {
            var timeSince = DateTime.Now - lastRun.Value;
            if (timeSince.TotalMinutes < 60)
                LastRunLabel.Text = $"{(int)timeSince.TotalMinutes} minutes ago";
            else if (timeSince.TotalHours < 24)
                LastRunLabel.Text = $"{(int)timeSince.TotalHours} hours ago";
            else
                LastRunLabel.Text = lastRun.Value.ToString("MMM dd, HH:mm");
        }
        else
        {
            LastRunLabel.Text = "Never";
        }

        // Update next run
        if (isScheduled)
        {
            var nextRun = _settingsService.GetNextRunTime();
            var timeUntil = nextRun - DateTime.Now;
            if (timeUntil.TotalMinutes < 60)
                NextRunLabel.Text = $"In {(int)timeUntil.TotalMinutes} minutes";
            else if (timeUntil.TotalHours < 24)
                NextRunLabel.Text = $"In {(int)timeUntil.TotalHours} hours";
            else
                NextRunLabel.Text = nextRun.ToString("MMM dd, HH:mm");
        }
        else
        {
            NextRunLabel.Text = "Not scheduled";
        }
    }

    private void LoadFriendsList()
    {
        var friends = _settingsService.GetFriendsList()
            .OrderBy(f => f.DisplayName)
            .ThenBy(f => f.Username)
            .ToList();

        var query = FriendsSearchBar.Text?.ToLower();
        if (!string.IsNullOrWhiteSpace(query))
        {
            friends = friends.Where(f => 
                f.Username.ToLower().Contains(query) || 
                f.DisplayName.ToLower().Contains(query)).ToList();
        }

        // Clear existing friend items (except NoFriendsLabel)
        var itemsToRemove = FriendsListContainer.Children
            .Where(c => c != NoFriendsLabel)
            .ToList();

        foreach (var item in itemsToRemove)
        {
            FriendsListContainer.Children.Remove(item);
        }

        NoFriendsLabel.IsVisible = friends.Count == 0;

        foreach (var friend in friends)
        {
            var friendView = CreateFriendView(friend);
            FriendsListContainer.Children.Add(friendView);
        }
    }

    private View CreateFriendView(FriendConfig friend)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8,
            Padding = new Thickness(0, 4)
        };

        var infoStack = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        
        var displayName = string.IsNullOrEmpty(friend.DisplayName) ? friend.Username : friend.DisplayName;
        infoStack.Children.Add(new Label
        {
            Text = displayName,
            FontSize = 16,
            FontAttributes = FontAttributes.Bold
        });
        
        infoStack.Children.Add(new Label
        {
            Text = $"@{friend.Username}",
            FontSize = 13,
            TextColor = Color.FromArgb("#888888")
        });

        if (friend.LastMessageSent.HasValue)
        {
            var successIcon = friend.SuccessCount > 0 ? "✓" : "";
            infoStack.Children.Add(new Label
            {
                Text = $"{successIcon} Last: {friend.LastMessageSent.Value:MMM dd}",
                FontSize = 11,
                TextColor = Color.FromArgb("#4CAF50")
            });
        }

        grid.Children.Add(infoStack);

        var toggleSwitch = new Switch
        {
            IsToggled = friend.IsEnabled,
            VerticalOptions = LayoutOptions.Center
        };
        toggleSwitch.Toggled += (s, e) =>
        {
            friend.IsEnabled = e.Value;
            _settingsService.UpdateFriend(friend);
            UpdateStatus();
        };
        Grid.SetColumn(toggleSwitch, 1);
        grid.Children.Add(toggleSwitch);

        if (Application.Current?.Resources.TryGetValue("TextButton", out var textButtonStyle) != true)
        {
            textButtonStyle = null;
        }

        var editButton = new Button
        {
            Text = "Edit",
            Style = textButtonStyle as Style,
            VerticalOptions = LayoutOptions.Center
        };
        editButton.Clicked += (s, e) =>
        {
            _editingFriendId = friend.Id;
            NewFriendUsernameEntry.Text = friend.Username;
            NewFriendDisplayNameEntry.Text = friend.DisplayName;
            AddFriendPanel.IsVisible = true;
            NewFriendUsernameEntry.Focus();
        };
        Grid.SetColumn(editButton, 2);
        grid.Children.Add(editButton);

        var deleteButton = new Button
        {
            Text = "Remove",
            Style = textButtonStyle as Style,
            TextColor = Color.FromArgb("#F44336"),
            VerticalOptions = LayoutOptions.Center
        };
        deleteButton.Clicked += async (s, e) =>
        {
            var confirm = await DisplayAlert("Remove Friend", 
                $"Remove {displayName} from the list?", "Remove", "Cancel");
            if (confirm)
            {
                _settingsService.RemoveFriend(friend.Id);
                LoadFriendsList();
                UpdateStatus();
            }
        };
        Grid.SetColumn(deleteButton, 3);
        grid.Children.Add(deleteButton);

        return grid;
    }

    private void LoadHistory()
    {
        var history = _settingsService.GetRunHistory().Take(5).ToList();

        // Clear existing history items (except NoHistoryLabel)
        var itemsToRemove = HistoryContainer.Children
            .Where(c => c != NoHistoryLabel)
            .ToList();

        foreach (var item in itemsToRemove)
        {
            HistoryContainer.Children.Remove(item);
        }

        NoHistoryLabel.IsVisible = history.Count == 0;

        foreach (var run in history)
        {
            var historyView = CreateHistoryView(run);
            HistoryContainer.Children.Add(historyView);
        }
    }

    private View CreateHistoryView(StreakRunResult run)
    {
        var successCount = run.FriendResults.Count(r => r.Success);
        var totalCount = run.FriendResults.Count;
        var statusIcon = run.Success ? "✓" : "✗";
        var statusColor = run.Success ? Color.FromArgb("#4CAF50") : Color.FromArgb("#F44336");

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto }
            },
            ColumnSpacing = 8
        };

        var iconLabel = new Label
        {
            Text = statusIcon,
            FontSize = 16,
            TextColor = statusColor,
            VerticalOptions = LayoutOptions.Center
        };
        grid.Children.Add(iconLabel);

        var infoStack = new VerticalStackLayout { Spacing = 2 };
        infoStack.Children.Add(new Label
        {
            Text = run.RunTime.ToString("MMM dd, HH:mm"),
            FontSize = 14
        });
        
        if (totalCount > 0)
        {
            infoStack.Children.Add(new Label
            {
                Text = $"{successCount}/{totalCount} messages sent",
                FontSize = 12,
                TextColor = Color.FromArgb("#888888")
            });
        }
        else if (!string.IsNullOrEmpty(run.ErrorMessage))
        {
            infoStack.Children.Add(new Label
            {
                Text = run.ErrorMessage,
                FontSize = 12,
                TextColor = statusColor,
                LineBreakMode = LineBreakMode.TailTruncation
            });
        }
        
        Grid.SetColumn(infoStack, 1);
        grid.Children.Add(infoStack);

        return grid;
    }

    private void OnScheduleToggled(object? sender, ToggledEventArgs e)
    {
#if ANDROID
        if (e.Value)
        {
            // Enable scheduling
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            TiktokStreakSaver.Platforms.Android.StreakScheduler.ScheduleNextRun(context);
        }
        else
        {
            // Disable scheduling
            var context = Platform.CurrentActivity ?? Android.App.Application.Context;
            TiktokStreakSaver.Platforms.Android.StreakScheduler.CancelSchedule(context);
        }
#endif
        UpdateStatus();
    }

    private void OnMessageChanged(object? sender, TextChangedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.NewTextValue))
        {
            _settingsService.SetMessageText(e.NewTextValue);
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        LoadFriendsList();
    }

    private async void OnLoginClicked(object? sender, EventArgs e)
    {
        // Reset session check so it will revalidate when returning
        _sessionCheckCompleted = false;
        await Navigation.PushAsync(new LoginPage());
    }

    private void OnAddFriendClicked(object? sender, EventArgs e)
    {
        _editingFriendId = null;
        AddFriendPanel.IsVisible = true;
        NewFriendUsernameEntry.Text = string.Empty;
        NewFriendDisplayNameEntry.Text = string.Empty;
        NewFriendUsernameEntry.Focus();
    }

    private void OnCancelAddFriend(object? sender, EventArgs e)
    {
        _editingFriendId = null;
        AddFriendPanel.IsVisible = false;
    }

    private async void OnSaveFriend(object? sender, EventArgs e)
    {
        var username = NewFriendUsernameEntry.Text?.Trim().TrimStart('@');
        var displayName = NewFriendDisplayNameEntry.Text?.Trim();

        if (string.IsNullOrEmpty(username))
        {
            await DisplayAlert("Error", "Please enter a username", "OK");
            return;
        }

        var existingFriends = _settingsService.GetFriendsList();
        
        // Check for duplicate ONLY if it's a new friend or username changed
        if (string.IsNullOrEmpty(_editingFriendId) || 
            existingFriends.FirstOrDefault(f => f.Id == _editingFriendId)?.Username != username)
        {
            if (existingFriends.Any(f => f.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
            {
                await DisplayAlert("Error", "This friend is already in your list", "OK");
                return;
            }
        }

        if (!string.IsNullOrEmpty(_editingFriendId))
        {
            var editingFriend = existingFriends.FirstOrDefault(f => f.Id == _editingFriendId);
            if (editingFriend != null)
            {
                editingFriend.Username = username;
                editingFriend.DisplayName = displayName ?? string.Empty;
                _settingsService.UpdateFriend(editingFriend);
            }
        }
        else
        {
            var friend = new FriendConfig
            {
                Username = username,
                DisplayName = displayName ?? string.Empty,
                IsEnabled = true
            };
            _settingsService.AddFriend(friend);
        }

        _editingFriendId = null;
        AddFriendPanel.IsVisible = false;
        LoadFriendsList();
        UpdateStatus();
    }

    private async void OnPermissionsClicked(object? sender, EventArgs e)
    {
#if ANDROID
        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        
        var actions = new List<string>
        {
            "Request Battery Optimization Exemption",
            "Request Exact Alarm Permission",
            "Request Notification Permission"
        };

        var action = await DisplayActionSheet("Permissions", "Cancel", null, actions.ToArray());

        switch (action)
        {
            case "Request Battery Optimization Exemption":
                TiktokStreakSaver.Platforms.Android.StreakScheduler.RequestBatteryOptimizationExemption(context);
                break;
            case "Request Exact Alarm Permission":
                TiktokStreakSaver.Platforms.Android.StreakScheduler.RequestExactAlarmPermission(context);
                break;
            case "Request Notification Permission":
                await RequestNotificationPermission();
                break;
        }
#else
        await DisplayAlert("Info", "Permissions are only required on Android", "OK");
#endif
    }

    private async Task RequestNotificationPermission()
    {
#if ANDROID
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.Tiramisu)
        {
            var status = await Permissions.RequestAsync<Permissions.PostNotifications>();
            if (status != PermissionStatus.Granted)
            {
                await DisplayAlert("Permission Required", 
                    "Notification permission is required to show status while sending streaks.", "OK");
            }
        }
#endif
    }

    private async void OnRunNowClicked(object? sender, EventArgs e)
    {
        var friends = _settingsService.GetEnabledFriends();
        if (friends.Count == 0)
        {
            await DisplayAlert("No Friends", "Please add at least one friend before running.", "OK");
            return;
        }

        var confirm = await DisplayAlert("Run Now", 
            $"This will send your streak message to {friends.Count} friend{(friends.Count != 1 ? "s" : "")}. Continue?", 
            "Run", "Cancel");

        if (!confirm) return;

#if ANDROID
        // Request notification permission first on Android 13+
        await RequestNotificationPermission();

        var context = Platform.CurrentActivity ?? Android.App.Application.Context;
        TiktokStreakSaver.Platforms.Android.StreakScheduler.RunNow(context);
        
        await DisplayAlert("Started", "Streak service started. Check the notification for progress.", "OK");
#else
        await DisplayAlert("Info", "This feature is only available on Android", "OK");
#endif
    }
}
