using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;

namespace TiktokStreakSaver
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [Microsoft.Maui.Controls.Internals.Preserve(AllMembers = true)]
    public class MainActivity : MauiAppCompatActivity
    {
        private const int NotificationPermissionRequestCode = 1001;

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            
            // Create notification channel on app start
            CreateNotificationChannel();
            
            // Request notification permission for Android 13+
            RequestNotificationPermission();

            // Configure dynamic native status bar
            if (Application.Current != null)
            {
                Application.Current.RequestedThemeChanged += (s, e) => UpdateStatusBar(e.RequestedTheme);
                UpdateStatusBar(Application.Current.RequestedTheme);
            }
        }

        private void UpdateStatusBar(AppTheme? theme)
        {
#pragma warning disable CA1422 // Validate platform compatibility
            if (Window == null) return;
            var windowInsetsController = AndroidX.Core.View.WindowCompat.GetInsetsController(Window, Window.DecorView);
            if (windowInsetsController != null)
            {
                if (theme == AppTheme.Dark)
                {
                    Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#121212"));
                    windowInsetsController.AppearanceLightStatusBars = false; // Light icons on dark background
                }
                else
                {
                    Window.SetStatusBarColor(Android.Graphics.Color.ParseColor("#F2F2F2"));
                    windowInsetsController.AppearanceLightStatusBars = true;  // Dark icons on light background
                }
            }
#pragma warning restore CA1422
        }

        private void CreateNotificationChannel()
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                var channelId = "streak_service_channel";
                var channelName = "Streak Service";
                var channelDescription = "Notifications for TikTok streak automation";
                var importance = NotificationImportance.Low;
                
                var channel = new NotificationChannel(channelId, channelName, importance)
                {
                    Description = channelDescription
                };
                
                var notificationManager = (NotificationManager?)GetSystemService(NotificationService);
                notificationManager?.CreateNotificationChannel(channel);
            }
        }

        private void RequestNotificationPermission()
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(33))
            {
                if (ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.PostNotifications) 
                    != Permission.Granted)
                {
                    ActivityCompat.RequestPermissions(this, 
                        new[] { Android.Manifest.Permission.PostNotifications }, 
                        NotificationPermissionRequestCode);
                }
            }
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }
    }
}
