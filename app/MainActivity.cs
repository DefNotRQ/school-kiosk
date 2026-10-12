using Android.Animation;
using Android.App;
using Android.App.Admin;
using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Provider;
using Android.Views;
using Android.Webkit;
using Android.Widget;

namespace SchoolKiosk;

[Activity(Name = "com.ahura.schoolkiosk.MainActivity", Label = "School Kiosk", MainLauncher = true, Exported = true, LaunchMode = Android.Content.PM.LaunchMode.SingleTask,
    ScreenOrientation = Android.Content.PM.ScreenOrientation.Unspecified, LockTaskMode = "if_whitelisted")]
public sealed class MainActivity : Activity
{
    internal static bool IsVisible { get; private set; }
    private const int MaxTabs = 20;
    private KioskConfig config = null!;
    private FrameLayout browserHost = null!;
    private readonly List<WebView> tabs = new();
    private WebView web = null!;
    private TextView status = null!;
    private TextView clockOverlay = null!;
    private TextView connectionOverlay = null!;
    private TextView batteryOverlay = null!;
    private ProgressBar loadingProgress = null!;
    private LinearLayout offlinePanel = null!;
    private TextView offlineDetail = null!;
    private bool scheduleWasActive;
    private bool pinRequestPending;
    private bool resumed;
    private bool? appliedPolicy;
    private bool systemBarHideQueued;
    private bool wasOnline;
    private bool pageLoadFailed;
    private long lastRecoveryAttempt;
    private ValueAnimator? chargingPulse;
    private bool? chargingVisualActive;
    private long lastInteraction;
    private int failedPinAttempts;
    private long pinLockedUntil;
    private int adminSessionGeneration;
    private bool adminSessionOpen;
    private int secretTaps;
    private bool pinDialogOpen;
    private long lastTap;
    private readonly Handler handler = new(Looper.MainLooper!);

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        config = new KioskConfig(this);
        failedPinAttempts = config.FailedPinAttempts;
        pinLockedUntil = config.PinLockedUntilUnixMs;
        lastInteraction = SystemClock.ElapsedRealtime();
        BuildUi();
        ApplyBrightness();
        ConfigureDeviceOwner();
        ScheduleMonitorService.EnsureRunning(this);
        ScheduleReceiver.ScheduleNext(this);
        ApplySchedule();
        if (!config.SetupComplete) ShowSettings(firstRun: true);
        handler.PostDelayed(CheckSchedule, 15_000);
        wasOnline = IsNetworkOnline();
        handler.PostDelayed(CheckConnectivityAndRecover, 2_000);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        ScheduleReceiver.ScheduleNext(this);
        ApplySchedule();
    }

    protected override void OnResume()
    {
        base.OnResume();
        resumed = true;
        IsVisible = true;
        if (config is not null) handler.Post(ApplySchedule);
    }

    protected override void OnPause()
    {
        resumed = false;
        IsVisible = false;
        base.OnPause();
    }

    public override void OnWindowFocusChanged(bool hasFocus)
    {
        base.OnWindowFocusChanged(hasFocus);
        if (hasFocus)
        {
            ApplySystemBarMode(config?.IsKioskTime(DateTime.Now) == true);
            UpdateIndicators();
        }
        if (hasFocus && config is not null)
            handler.PostDelayed(ApplySchedule, 100);
    }

    public override bool DispatchTouchEvent(MotionEvent? ev)
    {
        if (ev?.ActionMasked == MotionEventActions.Down)
            lastInteraction = SystemClock.ElapsedRealtime();
        return base.DispatchTouchEvent(ev);
    }

    private void BuildUi()
    {
        HideSystemBars(hideNavigation: true);
        Window.SetNavigationBarColor(Color.ParseColor("#070B14"));
        var root = new FrameLayout(this);
        browserHost = new FrameLayout(this) { LayoutParameters = new FrameLayout.LayoutParams(-1, -1) };
        root.AddView(browserHost);
        web = CreateTab(makeActive: true)!;

        offlinePanel = BuildOfflinePanel();
        root.AddView(offlinePanel);

        loadingProgress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal)
        {
            Max = 100,
            Progress = 0,
            Visibility = ViewStates.Gone,
            Alpha = 0f,
            ContentDescription = "Page loading progress"
        };
        loadingProgress.ProgressTintList = Android.Content.Res.ColorStateList.ValueOf(Color.ParseColor("#18BDF2"));
        loadingProgress.ProgressBackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Color.Transparent);
        loadingProgress.LayoutParameters = new FrameLayout.LayoutParams(-1, Dp(3), GravityFlags.Top);
        root.AddView(loadingProgress);

        status = new TextView(this) { Text = "⚙", TextSize = 22, Gravity = GravityFlags.Center, ContentDescription = "Kiosk settings" };
        status.SetTextColor(Color.ParseColor("#F7FAFF"));
        status.Background = Rounded(Color.Argb(245, 20, 34, 70), 18, Color.Argb(100, 174, 199, 255), 1);
        status.Elevation = Dp(10);
        var statusParams = new FrameLayout.LayoutParams(Dp(48), Dp(48), GravityFlags.Bottom | GravityFlags.Left);
        statusParams.SetMargins(Dp(14), 0, 0, Dp(14));
        status.LayoutParameters = statusParams;
        status.Click += (_, _) =>
        {
            status.Animate()!.ScaleX(0.88f).ScaleY(0.88f).SetDuration(80).WithEndAction(new Java.Lang.Runnable(() =>
                status.Animate()!.ScaleX(1f).ScaleY(1f).SetDuration(120).Start())).Start();
            HandleSecretTap();
        };
        root.AddView(status);

        clockOverlay = StatusIndicator(GravityFlags.Left, "00:00", "Current time");
        connectionOverlay = StatusIndicator(GravityFlags.Left, "●", "Connection status");
        if (connectionOverlay.LayoutParameters is FrameLayout.LayoutParams connectionLayout)
        {
            connectionLayout.Width = Dp(32);
            connectionLayout.SetMargins(Dp(78), Dp(5), 0, 0);
            connectionOverlay.LayoutParameters = connectionLayout;
        }
        batteryOverlay = StatusIndicator(GravityFlags.Right, "--%", "Battery charge");
        root.AddView(clockOverlay);
        root.AddView(connectionOverlay);
        root.AddView(batteryOverlay);
        SetContentView(root);
        Window.DecorView.SystemUiVisibilityChange += OnSystemUiVisibilityChanged;
        UpdateIndicators();
    }

    private LinearLayout BuildOfflinePanel()
    {
        var panel = new LinearLayout(this)
        {
            Orientation = Orientation.Vertical,
            Visibility = ViewStates.Gone,
            Clickable = true,
            Focusable = true,
            ContentDescription = "Offline connection screen"
        };
        panel.SetGravity(GravityFlags.Center);
        panel.SetBackgroundColor(Color.ParseColor("#F4F7FC"));
        panel.SetPadding(Dp(48), Dp(64), Dp(48), Dp(48));
        panel.LayoutParameters = new FrameLayout.LayoutParams(-1, -1);

        var icon = new TextView(this) { Text = "◎", TextSize = 58, Gravity = GravityFlags.Center };
        icon.SetTextColor(Color.ParseColor("#2463EB"));
        panel.AddView(icon, new LinearLayout.LayoutParams(Dp(100), Dp(100)) { BottomMargin = Dp(12) });

        var title = new TextView(this) { Text = "You're offline", TextSize = 27, Gravity = GravityFlags.Center };
        title.SetTextColor(Color.ParseColor("#10182B"));
        title.SetTypeface(Android.Graphics.Typeface.Default, Android.Graphics.TypefaceStyle.Bold);
        panel.AddView(title, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(10) });

        offlineDetail = new TextView(this)
        {
            Text = "Connect to Wi-Fi and the school page will reload automatically.",
            TextSize = 16,
            Gravity = GravityFlags.Center
        };
        offlineDetail.SetTextColor(Color.ParseColor("#53627A"));
        offlineDetail.SetMaxWidth(Dp(540));
        panel.AddView(offlineDetail, new LinearLayout.LayoutParams(-1, -2) { BottomMargin = Dp(28) });

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetGravity(GravityFlags.Center);
        var retry = ActionButton("Try again", primary: true);
        var wifi = ActionButton("Wi-Fi settings", primary: false);
        retry.Click += (_, _) => RetryCurrentPage();
        wifi.Click += (_, _) => OpenWifiPanel();
        actions.AddView(retry, new LinearLayout.LayoutParams(Dp(154), Dp(54)) { RightMargin = Dp(12) });
        actions.AddView(wifi, new LinearLayout.LayoutParams(Dp(174), Dp(54)));
        panel.AddView(actions);
        return panel;
    }

    private void OnSystemUiVisibilityChanged(object? sender, View.SystemUiVisibilityChangeEventArgs args)
    {
        if (systemBarHideQueued) return;
        systemBarHideQueued = true;
        handler.PostDelayed(() =>
        {
            systemBarHideQueued = false;
            if (!IsFinishing)
                ApplySystemBarMode(config?.IsKioskTime(DateTime.Now) == true);
        }, 120);
    }

    private TextView StatusIndicator(GravityFlags horizontalGravity, string text, string description)
    {
        var indicator = new TextView(this)
        {
            Text = text,
            TextSize = 12.5f,
            Gravity = GravityFlags.Center,
            ContentDescription = description,
            Clickable = false,
            Focusable = false
        };
        indicator.SetTextColor(Color.White);
        indicator.SetTypeface(Android.Graphics.Typeface.Default, Android.Graphics.TypefaceStyle.Bold);
        indicator.SetPadding(Dp(7), 0, Dp(7), 0);
        indicator.Background = Rounded(Color.ParseColor("#D9111826"), 12, Color.ParseColor("#22364B68"), 1);
        indicator.Elevation = Dp(20);
        var width = description == "Battery charge" ? 60 : 66;
        var layout = new FrameLayout.LayoutParams(Dp(width), Dp(29), GravityFlags.Top | horizontalGravity);
        layout.SetMargins(Dp(7), Dp(5), Dp(7), 0);
        indicator.LayoutParameters = layout;
        return indicator;
    }

    private void UpdateIndicators()
    {
        if (clockOverlay is null || connectionOverlay is null || batteryOverlay is null) return;
        clockOverlay.Text = DateTime.Now.ToString("HH:mm");
        UpdateConnectionIndicator(IsNetworkOnline());

        var manager = (BatteryManager?)GetSystemService(BatteryService);
        var level = manager?.GetIntProperty((int)BatteryProperty.Capacity) ?? -1;
        var batteryIntent = RegisterReceiver(null, new IntentFilter(Intent.ActionBatteryChanged));
        var batteryStatus = batteryIntent?.GetIntExtra(BatteryManager.ExtraStatus, -1) ?? -1;
        var charging = batteryStatus == (int)BatteryStatus.Charging || batteryStatus == (int)BatteryStatus.Full;
        batteryOverlay.Text = level >= 0 ? $"{level}%" : "--%";
        UpdateBatteryChargingVisual(charging);
        var batteryColor = level >= 0 && level < 20 ? "#F28B94" : level > 70 ? "#8FD3A8" : "#F4F7FC";
        batteryOverlay.SetTextColor(Color.ParseColor(batteryColor));
    }

    private void UpdateConnectionIndicator(bool online)
    {
        if (connectionOverlay is null) return;
        connectionOverlay.Text = online ? "●" : "!";
        connectionOverlay.ContentDescription = online ? "Internet connected" : "Internet offline";
        connectionOverlay.SetTextColor(Color.ParseColor(online ? "#62D991" : "#F28B94"));
    }

    private void UpdateBatteryChargingVisual(bool charging)
    {
        if (chargingVisualActive == charging) return;
        chargingVisualActive = charging;
        chargingPulse?.Cancel();
        chargingPulse?.Dispose();
        chargingPulse = null;
        batteryOverlay.Alpha = 1f;

        if (charging)
        {
            var glow = Rounded(Color.ParseColor("#FF35F27D"), 14);
            glow.Alpha = 80;
            var greenRing = Rounded(Color.ParseColor("#FF20E86F"), 13);
            var surface = Rounded(Color.ParseColor("#FF111826"), 10);
            var layers = new LayerDrawable(new Drawable[] { glow, greenRing, surface });
            layers.SetLayerInset(1, Dp(1), Dp(1), Dp(1), Dp(1));
            layers.SetLayerInset(2, Dp(4), Dp(4), Dp(4), Dp(4));
            batteryOverlay.Background = layers;
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            {
                batteryOverlay.SetOutlineAmbientShadowColor(Color.ParseColor("#9A00E676"));
                batteryOverlay.SetOutlineSpotShadowColor(Color.ParseColor("#C800E676"));
            }
            batteryOverlay.Elevation = Dp(14);
            chargingPulse = ValueAnimator.OfInt(65, 205, 65);
            chargingPulse!.AddUpdateListener(new GlowAnimatorListener(glow));
            chargingPulse.SetDuration(1700);
            chargingPulse.RepeatCount = ValueAnimator.Infinite;
            chargingPulse.RepeatMode = ValueAnimatorRepeatMode.Restart;
            chargingPulse.Start();
            return;
        }

        if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
        {
            batteryOverlay.SetOutlineAmbientShadowColor(Color.Black);
            batteryOverlay.SetOutlineSpotShadowColor(Color.Black);
        }
        batteryOverlay.Elevation = Dp(20);
        batteryOverlay.Background = Rounded(Color.ParseColor("#D9111826"), 12, Color.ParseColor("#22364B68"), 1);
    }

    private bool IsNetworkOnline()
    {
        try
        {
            var connectivity = (Android.Net.ConnectivityManager?)GetSystemService(ConnectivityService);
            var network = connectivity?.ActiveNetwork;
            var capabilities = network is null ? null : connectivity?.GetNetworkCapabilities(network);
            return capabilities?.HasCapability(Android.Net.NetCapability.Internet) == true &&
                capabilities.HasCapability(Android.Net.NetCapability.Validated);
        }
        catch { return false; }
    }

    private void ShowOfflineScreen(string detail)
    {
        pageLoadFailed = true;
        offlineDetail.Text = detail;
        clockOverlay.SetTextColor(Android.Graphics.Color.White);
        var wasVisible = offlinePanel.Visibility == ViewStates.Visible;
        offlinePanel.Visibility = ViewStates.Visible;
        if (!wasVisible)
        {
            offlinePanel.Alpha = 0f;
            offlinePanel.Animate()!.Alpha(1f).SetDuration(180).Start();
        }
        UpdateLoadingProgress(100);
    }

    private void HideOfflineScreen()
    {
        pageLoadFailed = false;
        clockOverlay.SetTextColor(Android.Graphics.Color.White);
        offlinePanel.Visibility = ViewStates.Gone;
    }

    private void RetryCurrentPage()
    {
        lastRecoveryAttempt = SystemClock.ElapsedRealtime();
        if (!IsNetworkOnline())
        {
            ShowOfflineScreen("No internet connection yet. Open Wi-Fi settings or try again in a moment.");
            return;
        }
        offlineDetail.Text = "Connection restored. Reloading the school page…";
        offlinePanel.Visibility = ViewStates.Visible;
        if (string.IsNullOrWhiteSpace(web.Url) || web.Url == "about:blank") web.LoadUrl(config.Url);
        else web.Reload();
    }

    private void CheckConnectivityAndRecover()
    {
        var online = IsNetworkOnline();
        UpdateConnectionIndicator(online);
        if (!online)
        {
            ShowOfflineScreen("Connect to Wi-Fi and the school page will reload automatically.");
        }
        else if (!wasOnline || (pageLoadFailed && SystemClock.ElapsedRealtime() - lastRecoveryAttempt >= 12_000))
        {
            RetryCurrentPage();
        }
        wasOnline = online;
        handler.PostDelayed(CheckConnectivityAndRecover, 5_000);
    }

    private void UpdateLoadingProgress(int progress)
    {
        if (loadingProgress is null) return;
        var value = Math.Clamp(progress, 0, 100);
        if (value < 100)
        {
            loadingProgress.Animate()?.Cancel();
            loadingProgress.Visibility = ViewStates.Visible;
            loadingProgress.Alpha = 1f;
            loadingProgress.SetProgress(Math.Max(3, value), true);
            return;
        }

        loadingProgress.SetProgress(100, true);
        loadingProgress.Animate()!.Alpha(0f).SetStartDelay(140).SetDuration(220)
            .WithEndAction(new Java.Lang.Runnable(() =>
            {
                loadingProgress.Visibility = ViewStates.Gone;
                loadingProgress.Progress = 0;
            })).Start();
    }

    private void HideSystemBars(bool hideNavigation)
    {
        Window?.AddFlags(WindowManagerFlags.Fullscreen);
        if (Window?.DecorView is not null)
        {
            var flags = SystemUiFlags.Fullscreen |
                SystemUiFlags.LayoutFullscreen |
                SystemUiFlags.LayoutStable |
                SystemUiFlags.ImmersiveSticky;
            if (hideNavigation)
                flags |= SystemUiFlags.HideNavigation | SystemUiFlags.LayoutHideNavigation;
            Window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
        }
    }

    private void ApplySystemBarMode(bool kioskActive)
    {
        if (kioskActive)
        {
            HideSystemBars(hideNavigation: true);
            return;
        }

        // Immersive-sticky can leave OEM launchers unable to enter their
        // Recents UI even after the navigation buttons become visible. Fully
        // clear immersive/fullscreen outside kiosk hours so Honor's Recents
        // implementation receives the button normally.
        Window?.ClearFlags(WindowManagerFlags.Fullscreen);
        if (Window?.DecorView is not null)
            Window.DecorView.SystemUiVisibility = StatusBarVisibility.Visible;
    }

    private WebView? CreateTab(bool makeActive)
    {
        if (tabs.Count >= MaxTabs)
        {
            ShowNotice("Tab limit reached", "You already have 20 tabs open. Use the settings gear and choose Clear tabs to start fresh.");
            return null;
        }

        var tab = new WebView(this) { LayoutParameters = new FrameLayout.LayoutParams(-1, -1) };
        tab.Settings.JavaScriptEnabled = true;
        tab.Settings.DomStorageEnabled = true;
        tab.Settings.SetSupportZoom(true);
        tab.Settings.BuiltInZoomControls = true;
        tab.Settings.DisplayZoomControls = false;
        tab.Settings.SetSupportMultipleWindows(true);
        tab.Settings.JavaScriptCanOpenWindowsAutomatically = true;
        tab.Settings.MixedContentMode = MixedContentHandling.NeverAllow;
        tab.Settings.UserAgentString += " SchoolKiosk/HonorPad10";
        tab.SetWebViewClient(new OpenWebClient(this));
        tab.SetWebChromeClient(new TabChromeClient(this));
        tab.SetDownloadListener(new NoDownloadListener(this));
        tab.Visibility = ViewStates.Gone;
        browserHost.AddView(tab);
        tabs.Add(tab);
        if (makeActive) ActivateTab(tab);
        return tab;
    }

    private void ActivateTab(WebView tab)
    {
        if (tabs.Contains(web) && web != tab)
        {
            web.OnPause();
            web.Visibility = ViewStates.Gone;
        }
        web = tab;
        web.Visibility = ViewStates.Visible;
        web.OnResume();
        web.RequestFocus();
    }

    private void CloseCurrentTab()
    {
        if (tabs.Count <= 1) return;
        var closing = web;
        var next = tabs[^2];
        tabs.Remove(closing);
        browserHost.RemoveView(closing);
        closing.StopLoading();
        closing.Destroy();
        ActivateTab(next);
    }

    private void ResetTabs()
    {
        foreach (var tab in tabs.ToArray())
        {
            browserHost.RemoveView(tab);
            tab.StopLoading();
            tab.Destroy();
        }
        tabs.Clear();
        web = CreateTab(makeActive: true)!;
        web.LoadUrl(config.Url);
    }

    private bool RecoverCrashedWebView(WebView? crashed, RenderProcessGoneDetail? detail)
    {
        if (crashed is null) return false;
        var wasActive = ReferenceEquals(crashed, web);
        tabs.Remove(crashed);
        browserHost.RemoveView(crashed);
        try
        {
            crashed.StopLoading();
            crashed.Destroy();
        }
        catch { }

        if (wasActive || tabs.Count == 0)
        {
            var replacement = CreateTab(makeActive: true);
            replacement?.LoadUrl(config.Url);
        }

        lastInteraction = SystemClock.ElapsedRealtime();
        var reason = detail?.DidCrash() == true ? "The browser recovered from a crash" : "The browser was restarted to keep it responsive";
        Toast.MakeText(this, reason, ToastLength.Short)!.Show();
        return true;
    }

    private void CheckInactivityReset()
    {
        if (!config.IsKioskTime(DateTime.Now) || adminSessionOpen) return;
        var timeout = config.IdleResetMinutes * 60_000L;
        var now = SystemClock.ElapsedRealtime();
        if (now - lastInteraction < timeout) return;
        lastInteraction = now;
        ResetTabs();
        Toast.MakeText(this, "Returned to the school home page after inactivity", ToastLength.Short)!.Show();
    }

    private void ApplySchedule()
    {
        // Never pin a background task or the keyguard: this can strand the user
        // on the launcher/wallpaper on OEM Android builds.
        if (!resumed || IsFinishing || IsDestroyed) return;
        var keyguard = (KeyguardManager?)GetSystemService(KeyguardService);
        if (keyguard?.IsKeyguardLocked == true) return;
        var shouldLock = config.SetupComplete && config.IsKioskTime(DateTime.Now);
        // Keep the real system controls available outside scheduled kiosk
        // hours. During a kiosk period, lock task and immersive mode suppress
        // Home/Recents as intended.
        ApplySystemBarMode(shouldLock);
        var indicatorVisibility = shouldLock ? ViewStates.Visible : ViewStates.Gone;
        clockOverlay.Visibility = indicatorVisibility;
        connectionOverlay.Visibility = indicatorVisibility;
        batteryOverlay.Visibility = indicatorVisibility;
        var previousScheduleState = scheduleWasActive;
        scheduleWasActive = shouldLock;
        var activityManager = (ActivityManager?)GetSystemService(ActivityService);
        var isActuallyLocked = activityManager is not null && (int)activityManager.LockTaskModeState != 0;
        if (isActuallyLocked) pinRequestPending = false;
        if (!shouldLock) pinRequestPending = false;
        if (appliedPolicy != shouldLock)
        {
            UpdateDevicePolicy(shouldLock);
            appliedPolicy = shouldLock;
        }
        status.Alpha = shouldLock ? 0.34f : 0.52f;

        if (tabs.Count == 1 && web.Url is null) web.LoadUrl(config.Url);
        if (shouldLock && !isActuallyLocked && !pinRequestPending)
        {
            try
            {
                pinRequestPending = true;
                StartLockTask();
                handler.PostDelayed(() =>
                {
                    var manager = (ActivityManager?)GetSystemService(ActivityService);
                    var pinned = manager is not null && (int)manager.LockTaskModeState != 0;
                    if (!pinned && config.IsKioskTime(DateTime.Now))
                    {
                        pinRequestPending = false;
                        // Retry only from the regular foreground schedule check.
                    }
                }, 10_000);
            }
            catch { pinRequestPending = false; }
        }
        else if (!shouldLock && isActuallyLocked)
        {
            pinRequestPending = false;
            try { StopLockTask(); } catch { }
        }
        if (previousScheduleState && !shouldLock) handler.PostDelayed(LaunchHonorHome, 250);
    }

    private void CheckSchedule()
    {
        UpdateIndicators();
        CheckInactivityReset();
        ApplySchedule();
        // User-level screen pinning can be manually unpinned by Android. Re-check
        // quickly during kiosk hours so the app immediately requests pinning again.
        handler.PostDelayed(CheckSchedule, 15_000);
    }

    private void HandleSecretTap()
    {
        if (!config.IsKioskTime(DateTime.Now)) { ShowPin(); return; }
        var now = SystemClock.ElapsedRealtime();
        secretTaps = now - lastTap < 800 ? secretTaps + 1 : 1;
        lastTap = now;
        if (secretTaps >= 5) { secretTaps = 0; ShowPin(); }
    }

    private void LaunchHonorHome()
    {
        try
        {
            var honorHome = new Intent(Intent.ActionMain);
            honorHome.AddCategory(Intent.CategoryHome);
            honorHome.SetComponent(new ComponentName("com.hihonor.android.launcher", "com.hihonor.android.launcher.unihome.UniHomeLauncher"));
            honorHome.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop);
            StartActivity(honorHome);
        }
        catch { }
    }

    private void ShowPin()
    {
        if (pinDialogOpen || adminSessionOpen) return;
        pinDialogOpen = true;
        var dialog = NewDialog();
        dialog.DismissEvent += (_, _) => pinDialogOpen = false;
        var card = NewCard(Dp(28));
        card.AddView(Heading("Administrator access", 26));
        card.AddView(Body("Enter your PIN to manage the kiosk, network, schedule, and browser tabs."), Margin(bottom: 22));

        var quickClear = SecondaryButton($"Clear browser tabs · {tabs.Count} open", "No PIN required — returns to the school home page", destructive: true);
        quickClear.Click += (_, _) =>
        {
            ResetTabs();
            dialog.Dismiss();
        };
        card.AddView(quickClear, new LinearLayout.LayoutParams(-1, Dp(64)) { BottomMargin = Dp(18) });

        var input = ModernInput("Admin PIN", config.Pin.Length);
        input.InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberVariationPassword;
        card.AddView(input, new LinearLayout.LayoutParams(-1, Dp(58)));
        var error = ErrorText();
        card.AddView(error, Margin(top: 8, bottom: 18));

        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetGravity(GravityFlags.Right);
        var cancel = ActionButton("Cancel", primary: false);
        var unlock = ActionButton("Unlock settings", primary: true);
        actions.AddView(cancel, new LinearLayout.LayoutParams(Dp(142), Dp(52)) { RightMargin = Dp(10) });
        actions.AddView(unlock, new LinearLayout.LayoutParams(Dp(190), Dp(52)));
        card.AddView(actions);
        cancel.Click += (_, _) => dialog.Dismiss();

        void RefreshLockout()
        {
            if (!dialog.IsShowing) return;
            var remainingMs = pinLockedUntil - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var locked = remainingMs > 0;
            input.Enabled = !locked;
            unlock.Enabled = !locked;
            unlock.Alpha = locked ? 0.45f : 1f;
            if (!locked)
            {
                if (pinLockedUntil != 0)
                {
                    pinLockedUntil = 0;
                    config.PinLockedUntilUnixMs = 0;
                }
                return;
            }
            var seconds = Math.Max(1, (int)Math.Ceiling(remainingMs / 1000d));
            error.Text = $"Too many incorrect attempts. Try again in {seconds} seconds.";
            error.Visibility = ViewStates.Visible;
            handler.PostDelayed(RefreshLockout, 1_000);
        }

        unlock.Click += (_, _) =>
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() < pinLockedUntil)
            {
                RefreshLockout();
                return;
            }
            if (input.Text == config.Pin)
            {
                failedPinAttempts = 0;
                pinLockedUntil = 0;
                config.FailedPinAttempts = 0;
                config.PinLockedUntilUnixMs = 0;
                dialog.Dismiss();
                ShowSettings(firstRun: false);
            }
            else
            {
                failedPinAttempts++;
                if (failedPinAttempts >= 5)
                {
                    failedPinAttempts = 0;
                    pinLockedUntil = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60_000;
                    config.FailedPinAttempts = 0;
                    config.PinLockedUntilUnixMs = pinLockedUntil;
                    input.Text = "";
                    RefreshLockout();
                    return;
                }
                config.FailedPinAttempts = failedPinAttempts;
                error.Text = $"That PIN is not correct. {5 - failedPinAttempts} attempts remaining.";
                error.Visibility = ViewStates.Visible;
                input.Background = Rounded(Color.ParseColor("#FFF7F7"), 14, Color.ParseColor("#DC5454"), 1);
                input.SelectAll();
            }
        };
        dialog.SetContentView(card);
        ShowDialog(dialog, widthDp: 560);
        RefreshLockout();
        input.RequestFocus();
    }

    private void ShowSettings(bool firstRun)
    {
        var dialog = NewDialog();
        var brightnessValue = config.Brightness;
        var adaptiveBrightness = config.AdaptiveBrightness;
        var idleResetMinutes = config.IdleResetMinutes;

        var shell = new LinearLayout(this) { Orientation = Orientation.Vertical };
        shell.Background = Rounded(Color.ParseColor("#F5F7FC"), 26);
        shell.SetPadding(Dp(28), Dp(24), Dp(28), Dp(20));

        var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        header.SetGravity(GravityFlags.CenterVertical);
        var mark = new TextView(this) { Text = "◆", TextSize = 19, Gravity = GravityFlags.Center };
        mark.SetTextColor(Color.White);
        mark.Background = Rounded(Color.ParseColor("#3157D5"), 15);
        header.AddView(mark, new LinearLayout.LayoutParams(Dp(46), Dp(46)) { RightMargin = Dp(14) });
        var headerCopy = new LinearLayout(this) { Orientation = Orientation.Vertical };
        headerCopy.AddView(Heading(firstRun ? "Set up School Kiosk" : "Kiosk settings", 25));
        headerCopy.AddView(Body(firstRun ? "A focused, scheduled browser for this tablet." : "Control access, schedule, connectivity, and tabs."));
        header.AddView(headerCopy, new LinearLayout.LayoutParams(0, -2, 1));
        shell.AddView(header, Margin(bottom: 20));

        var scroll = new ScrollView(this) { FillViewport = true, VerticalScrollBarEnabled = false };
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        scroll.AddView(content, new ScrollView.LayoutParams(-1, -2));

        var websiteCard = SectionCard("School website", "The home page that opens after setup or clearing tabs.");
        var url = ModernInput("https://school.example", null);
        url.Text = config.Url;
        url.InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationUri;
        websiteCard.AddView(url, new LinearLayout.LayoutParams(-1, Dp(58)) { TopMargin = Dp(14) });
        content.AddView(websiteCard, Margin(bottom: 14));

        var scheduleCard = SectionCard("School schedule", "Monday through Friday, 08:30 to 15:30 (tablet local time). Weekends are unlocked. This schedule is fixed.");
        content.AddView(scheduleCard, Margin(bottom: 14));

        var behaviorCard = SectionCard("Browser recovery", "Automatically return to the school home page after the tablet is left unused.");
        var idleLabel = Label($"INACTIVITY RESET  {idleResetMinutes} MINUTES");
        behaviorCard.AddView(idleLabel, Margin(top: 18, bottom: 4));
        var idleReset = new SeekBar(this) { Max = 59, Progress = idleResetMinutes - 1 };
        idleReset.ProgressChanged += (_, e) =>
        {
            idleResetMinutes = e.Progress + 1;
            idleLabel.Text = $"INACTIVITY RESET  {idleResetMinutes} MINUTES";
        };
        behaviorCard.AddView(idleReset, new LinearLayout.LayoutParams(-1, Dp(48)));
        behaviorCard.AddView(Body("Any touch restarts the timer. Resetting closes extra tabs and reloads the configured home page."), Margin(top: 2));
        content.AddView(behaviorCard, Margin(bottom: 14));

        var accessCard = SectionCard("Access & maintenance", "The PIN protects settings during and outside kiosk hours.");
        var pin = ModernInput("At least 4 digits", config.Pin.Length);
        pin.Text = config.Pin;
        pin.InputType = Android.Text.InputTypes.ClassNumber | Android.Text.InputTypes.NumberVariationPassword;
        accessCard.AddView(pin, new LinearLayout.LayoutParams(-1, Dp(58)) { TopMargin = Dp(14), BottomMargin = Dp(8) });
        var pinHelp = Body("Choose a private administrator PIN. There is no default PIN on new installations.");
        accessCard.AddView(pinHelp, Margin(bottom: 16));
        var testLock = SecondaryButton("Test pinning for 10 minutes", "Automatically releases after the test, unless school hours are active");
        testLock.Click += (_, _) =>
        {
            if (firstRun) { ShowNotice("Finish setup first", "Save your administrator PIN before starting a pinning test."); return; }
            config.TestLockUntilUtcMs = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeMilliseconds();
            dialog.Dismiss();
            ScheduleMonitorService.EnsureRunning(this);
            ScheduleReceiver.ScheduleNext(this);
            ApplySchedule();
        };
        accessCard.AddView(testLock, new LinearLayout.LayoutParams(-1, Dp(66)) { BottomMargin = Dp(12) });

        var connectivityRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var wifi = SecondaryButton("Wi-Fi", "Networks & internet");
        wifi.Click += (_, _) => OpenWifiPanel();
        var bluetooth = SecondaryButton("Bluetooth", "Devices & accessories");
        bluetooth.Click += (_, _) => OpenBluetoothPanel();
        connectivityRow.AddView(wifi, new LinearLayout.LayoutParams(0, Dp(66), 1) { RightMargin = Dp(6) });
        connectivityRow.AddView(bluetooth, new LinearLayout.LayoutParams(0, Dp(66), 1) { LeftMargin = Dp(6) });
        accessCard.AddView(connectivityRow, new LinearLayout.LayoutParams(-1, Dp(66)) { BottomMargin = Dp(9) });

        var reliabilityReady = HasOverlayAccess() && HasExactAlarmAccess();
        var reliability = SecondaryButton("Background launch access",
            reliabilityReady ? "Ready — scheduled kiosk starts can open automatically" : "Action required — allow the missing system access");
        reliability.Click += (_, _) => OpenNextReliabilityPermission();
        accessCard.AddView(reliability, new LinearLayout.LayoutParams(-1, Dp(62)) { BottomMargin = Dp(9) });

        var clearTabs = SecondaryButton($"Clear browser tabs · {tabs.Count} open", "Close every tab and return to the school site", destructive: true);
        clearTabs.Click += (_, _) =>
        {
            ResetTabs();
            ((TextView)((LinearLayout)clearTabs).GetChildAt(0)!).Text = "Tabs cleared · 1 open";
        };
        accessCard.AddView(clearTabs, new LinearLayout.LayoutParams(-1, Dp(62)));
        content.AddView(accessCard, Margin(bottom: 10));

        var displayCard = SectionCard("Display", "Set a comfortable kiosk brightness or let Android adjust it automatically.");
        var brightnessLabel = Label($"BRIGHTNESS  {Math.Round(brightnessValue / 255d * 100)}%");
        displayCard.AddView(brightnessLabel, Margin(top: 18, bottom: 4));
        var brightness = new SeekBar(this) { Max = 245, Progress = brightnessValue - 10 };
        brightness.ProgressChanged += (_, e) =>
        {
            brightnessValue = e.Progress + 10;
            brightnessLabel.Text = $"BRIGHTNESS  {Math.Round(brightnessValue / 255d * 100)}%";
            if (!adaptiveBrightness) ApplyBrightness(brightnessValue, false);
        };
        displayCard.AddView(brightness, new LinearLayout.LayoutParams(-1, Dp(48)) { BottomMargin = Dp(8) });
        var automatic = new Android.Widget.Switch(this)
        {
            Text = "Automatic brightness",
            TextSize = 15,
            Checked = adaptiveBrightness,
            Gravity = GravityFlags.CenterVertical
        };
        automatic.SetTextColor(Color.ParseColor("#111A33"));
        automatic.CheckedChange += (_, e) =>
        {
            adaptiveBrightness = e.IsChecked;
            brightness.Enabled = !adaptiveBrightness;
            brightness.Alpha = adaptiveBrightness ? 0.45f : 1f;
            ApplyBrightness(brightnessValue, adaptiveBrightness);
        };
        brightness.Enabled = !adaptiveBrightness;
        brightness.Alpha = adaptiveBrightness ? 0.45f : 1f;
        displayCard.AddView(automatic, new LinearLayout.LayoutParams(-1, Dp(52)));
        content.AddView(displayCard, Margin(bottom: 14));
        shell.AddView(scroll, new LinearLayout.LayoutParams(-1, 0, 1));

        var error = ErrorText();
        shell.AddView(error, Margin(top: 10, bottom: 8));
        var footer = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        footer.SetGravity(GravityFlags.Right);
        if (!firstRun)
        {
            var cancel = ActionButton("Cancel", primary: false);
            cancel.Click += (_, _) => dialog.Dismiss();
            footer.AddView(cancel, new LinearLayout.LayoutParams(Dp(130), Dp(52)) { RightMargin = Dp(10) });
        }
        var save = ActionButton(firstRun ? "Finish setup" : "Save changes", primary: true);
        footer.AddView(save, new LinearLayout.LayoutParams(Dp(190), Dp(52)));
        shell.AddView(footer);
        save.Click += (_, _) =>
        {
            var value = (url.Text ?? "").Trim();
            string? message = null;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) || parsed.Scheme != "https" || string.IsNullOrWhiteSpace(parsed.Host))
                message = "Enter a complete HTTPS website address.";
            else if ((pin.Text ?? "").Length < 4) message = "The admin PIN needs at least four digits.";
            if (message is not null)
            {
                error.Text = message;
                error.Visibility = ViewStates.Visible;
                return;
            }
            config.Url = value;
            config.Pin = pin.Text!;
            config.Brightness = brightnessValue;
            config.AdaptiveBrightness = adaptiveBrightness;
            config.IdleResetMinutes = idleResetMinutes;
            config.SetupComplete = true;
            ScheduleMonitorService.EnsureRunning(this);
            ApplyBrightness();
            ResetTabs();
            ScheduleReceiver.ScheduleNext(this);
            ApplySchedule();
            dialog.Dismiss();
            if (firstRun && (!HasOverlayAccess() || !HasExactAlarmAccess()))
                handler.PostDelayed(OpenNextReliabilityPermission, 250);
        };

        dialog.SetContentView(shell);
        dialog.SetCancelable(!firstRun);
        dialog.SetCanceledOnTouchOutside(false);
        ShowDialog(dialog, widthFraction: 0.78f, heightFraction: 0.90f);
        if (!firstRun) ArmAdminSessionTimeout(dialog);
    }

    private void ArmAdminSessionTimeout(Dialog dialog)
    {
        adminSessionOpen = true;

        void RenewSession()
        {
            var generation = ++adminSessionGeneration;
            handler.PostDelayed(() =>
            {
                if (generation != adminSessionGeneration || !dialog.IsShowing) return;
                dialog.Dismiss();
                Toast.MakeText(this, "Administrator session closed after 5 minutes", ToastLength.Short)!.Show();
            }, 5 * 60_000);
        }

        dialog.DismissEvent += (_, _) =>
        {
            adminSessionGeneration++;
            adminSessionOpen = false;
            lastInteraction = SystemClock.ElapsedRealtime();
        };
        if (dialog.Window?.DecorView is View decor)
        {
            decor.Touch += (_, e) =>
            {
                if (e.Event?.ActionMasked == MotionEventActions.Down) RenewSession();
            };
        }
        RenewSession();
    }

    private Dialog NewDialog()
    {
        var dialog = new Dialog(this);
        dialog.RequestWindowFeature((int)WindowFeatures.NoTitle);
        return dialog;
    }

    private void ShowDialog(Dialog dialog, int? widthDp = null, float? widthFraction = null, float? heightFraction = null)
    {
        dialog.Show();
        var window = dialog.Window!;
        window.SetBackgroundDrawable(new ColorDrawable(Color.Transparent));
        window.AddFlags(WindowManagerFlags.DimBehind);
        var attrs = window.Attributes!;
        attrs.DimAmount = 0.68f;
        window.Attributes = attrs;
        var screenWidth = Resources!.DisplayMetrics!.WidthPixels;
        var screenHeight = Resources.DisplayMetrics.HeightPixels;
        var width = widthDp.HasValue ? Math.Min(Dp(widthDp.Value), (int)(screenWidth * 0.92f)) : (int)(screenWidth * (widthFraction ?? 0.86f));
        var height = heightFraction.HasValue ? (int)(screenHeight * heightFraction.Value) : -2;
        window.SetLayout(width, height);
        window.SetSoftInputMode(SoftInput.AdjustResize);
    }

    private LinearLayout NewCard(int padding)
    {
        var card = new LinearLayout(this) { Orientation = Orientation.Vertical };
        card.SetPadding(padding, padding, padding, padding);
        card.Background = Rounded(Color.ParseColor("#F8FAFF"), 26, Color.ParseColor("#DCE5F7"), 1);
        card.Elevation = Dp(14);
        return card;
    }

    private LinearLayout SectionCard(string title, string subtitle)
    {
        var card = NewCard(Dp(18));
        card.Elevation = Dp(2);
        card.AddView(Heading(title, 18));
        card.AddView(Body(subtitle), Margin(top: 3));
        return card;
    }

    private TextView Heading(string text, float size)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(Color.ParseColor("#111A33"));
        view.SetTypeface(Android.Graphics.Typeface.Create("sans-serif", Android.Graphics.TypefaceStyle.Bold), Android.Graphics.TypefaceStyle.Bold);
        return view;
    }

    private TextView Body(string text)
    {
        var view = new TextView(this) { Text = text, TextSize = 14 };
        view.SetTextColor(Color.ParseColor("#667085"));
        view.SetLineSpacing(0, 1.12f);
        return view;
    }

    private EditText ModernInput(string hint, int? maskedLength)
    {
        var input = new EditText(this) { Hint = hint, TextSize = 16 };
        input.SetSingleLine(true);
        input.SetPadding(Dp(16), 0, Dp(16), 0);
        input.SetTextColor(Color.ParseColor("#111A33"));
        input.SetHintTextColor(Color.ParseColor("#98A2B3"));
        input.Background = Rounded(Color.White, 14, Color.ParseColor("#CBD5E7"), 1);
        return input;
    }

    private TextView ErrorText()
    {
        var error = new TextView(this) { TextSize = 13, Visibility = ViewStates.Invisible };
        error.SetMinHeight(Dp(20));
        error.SetTextColor(Color.ParseColor("#C63F3F"));
        return error;
    }

    private TextView ActionButton(string text, bool primary)
    {
        var button = new TextView(this) { Text = text, TextSize = 15, Gravity = GravityFlags.Center, Clickable = true, Focusable = true };
        button.SetTypeface(Android.Graphics.Typeface.Default, Android.Graphics.TypefaceStyle.Bold);
        button.SetTextColor(primary ? Color.White : Color.ParseColor("#3157D5"));
        button.Background = Rounded(primary ? Color.ParseColor("#3157D5") : Color.ParseColor("#E9EEFC"), 14);
        button.Elevation = primary ? Dp(3) : 0;
        return button;
    }

    private LinearLayout SecondaryButton(string title, string subtitle, bool destructive = false)
    {
        var button = new LinearLayout(this) { Orientation = Orientation.Vertical, Clickable = true, Focusable = true };
        button.SetGravity(GravityFlags.CenterVertical);
        button.SetPadding(Dp(16), 0, Dp(16), 0);
        button.Background = Rounded(destructive ? Color.ParseColor("#FFF3F2") : Color.ParseColor("#EEF3FF"), 14,
            destructive ? Color.ParseColor("#FFD0CC") : Color.ParseColor("#D7E2FF"), 1);
        var titleView = Heading(title, 15);
        titleView.SetTextColor(destructive ? Color.ParseColor("#B63B35") : Color.ParseColor("#2549B8"));
        var subtitleView = Body(subtitle);
        subtitleView.TextSize = 12;
        button.AddView(titleView);
        button.AddView(subtitleView);
        return button;
    }

    private TextView TimeButton(string caption, int minutes)
    {
        var button = new TextView(this) { Gravity = GravityFlags.CenterVertical, Clickable = true, Focusable = true };
        button.SetPadding(Dp(16), 0, Dp(16), 0);
        button.Background = Rounded(Color.ParseColor("#EEF3FF"), 14, Color.ParseColor("#D7E2FF"), 1);
        SetTimeButton(button, caption, minutes);
        return button;
    }

    private void SetTimeButton(TextView button, string caption, int minutes)
    {
        button.Text = $"{caption.ToUpperInvariant()}\n{KioskConfig.Format(minutes)}";
        button.TextSize = 16;
        button.SetTextColor(Color.ParseColor("#2549B8"));
    }

    private void StyleDay(ToggleButton button, bool selected)
    {
        button.SetTextColor(selected ? Color.White : Color.ParseColor("#52617A"));
        button.Background = Rounded(selected ? Color.ParseColor("#3157D5") : Color.ParseColor("#EDF1F8"), 12,
            selected ? Color.ParseColor("#3157D5") : Color.ParseColor("#DCE3EF"), 1);
        button.Elevation = selected ? Dp(2) : 0;
    }

    private void ShowModernTimePicker(string title, int current, Action<int> onPicked)
    {
        var dialog = NewDialog();
        var card = NewCard(Dp(26));
        card.AddView(Heading(title, 23));
        card.AddView(Body("Use 24-hour time."), Margin(top: 3, bottom: 18));
        var pickers = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        pickers.SetGravity(GravityFlags.Center);
        var hour = new NumberPicker(this) { MinValue = 0, MaxValue = 23, Value = current / 60 };
        var minute = new NumberPicker(this) { MinValue = 0, MaxValue = 59, Value = current % 60 };
        hour.SetFormatter(new TwoDigitFormatter());
        minute.SetFormatter(new TwoDigitFormatter());
        pickers.AddView(hour, new LinearLayout.LayoutParams(Dp(130), Dp(190)) { RightMargin = Dp(18) });
        pickers.AddView(minute, new LinearLayout.LayoutParams(Dp(130), Dp(190)));
        card.AddView(pickers, Margin(bottom: 18));
        var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        actions.SetGravity(GravityFlags.Right);
        var cancel = ActionButton("Cancel", false);
        var done = ActionButton("Set time", true);
        actions.AddView(cancel, new LinearLayout.LayoutParams(Dp(128), Dp(52)) { RightMargin = Dp(10) });
        actions.AddView(done, new LinearLayout.LayoutParams(Dp(150), Dp(52)));
        card.AddView(actions);
        cancel.Click += (_, _) => dialog.Dismiss();
        done.Click += (_, _) => { onPicked(hour.Value * 60 + minute.Value); dialog.Dismiss(); };
        dialog.SetContentView(card);
        ShowDialog(dialog, widthDp: 500);
    }

    private void ShowNotice(string title, string message)
    {
        var dialog = NewDialog();
        var card = NewCard(Dp(26));
        card.AddView(Heading(title, 23));
        card.AddView(Body(message), Margin(top: 8, bottom: 22));
        var okay = ActionButton("Got it", true);
        var row = new LinearLayout(this);
        row.SetGravity(GravityFlags.Right);
        row.AddView(okay, new LinearLayout.LayoutParams(Dp(140), Dp(52)));
        card.AddView(row);
        okay.Click += (_, _) => dialog.Dismiss();
        dialog.SetContentView(card);
        ShowDialog(dialog, widthDp: 540);
    }

    private LinearLayout.LayoutParams Margin(int top = 0, int bottom = 0)
        => new(-1, -2) { TopMargin = Dp(top), BottomMargin = Dp(bottom) };

    private sealed class TwoDigitFormatter : Java.Lang.Object, NumberPicker.IFormatter
    {
        public string? Format(int value) => value.ToString("00");
    }

    private sealed class GlowAnimatorListener(GradientDrawable glow) : Java.Lang.Object, ValueAnimator.IAnimatorUpdateListener
    {
        public void OnAnimationUpdate(ValueAnimator animation)
        {
            if (animation.AnimatedValue is Java.Lang.Integer value)
                glow.Alpha = value.IntValue();
        }
    }

    private int Dp(int value) => (int)(value * Resources!.DisplayMetrics!.Density + 0.5f);

    private TextView Label(string text)
    {
        var label = new TextView(this) { Text = text, TextSize = 12 };
        label.SetTextColor(Color.ParseColor("#52617A"));
        label.SetTypeface(Android.Graphics.Typeface.Default, Android.Graphics.TypefaceStyle.Bold);
        label.LetterSpacing = 0.08f;
        label.SetPadding(0, 0, 0, Dp(5));
        return label;
    }

    private GradientDrawable Rounded(Color color, int radiusDp, Color? stroke = null, int strokeDp = 0)
    {
        var drawable = new GradientDrawable();
        drawable.SetColor(color);
        drawable.SetCornerRadius(Dp(radiusDp));
        if (stroke.HasValue && strokeDp > 0) drawable.SetStroke(Dp(strokeDp), stroke.Value);
        return drawable;
    }

    private void ApplyBrightness()
        => ApplyBrightness(config.Brightness, config.AdaptiveBrightness);

    private void ApplyBrightness(int brightness, bool automatic)
    {
        if (Window is null) return;
        var attributes = Window.Attributes;
        if (attributes is null) return;
        attributes.ScreenBrightness = automatic ? -1f : Math.Clamp(brightness, 10, 255) / 255f;
        Window.Attributes = attributes;
    }

    private void OpenWifiPanel()
    {
        void LaunchPanel()
        {
            try
            {
                StartActivity(new Intent(Settings.Panel.ActionWifi));
            }
            catch
            {
                try { StartActivity(new Intent(Settings.ActionWifiSettings)); }
                catch
                {
                    ApplySchedule();
                    Toast.MakeText(this, "Wi-Fi settings are unavailable", ToastLength.Long)!.Show();
                }
            }
        }

        OpenMaintenancePanel(LaunchPanel);
    }

    private void OpenBluetoothPanel()
    {
        void LaunchPanel()
        {
            try
            {
                StartActivity(new Intent(Settings.ActionBluetoothSettings));
            }
            catch
            {
                try { StartActivity(new Intent(Settings.ActionWirelessSettings)); }
                catch
                {
                    ApplySchedule();
                    Toast.MakeText(this, "Bluetooth settings are unavailable", ToastLength.Long)!.Show();
                }
            }
        }

        OpenMaintenancePanel(LaunchPanel);
    }

    private void OpenMaintenancePanel(System.Action launchPanel)
    {

        var activityManager = (ActivityManager?)GetSystemService(ActivityService);
        var isLocked = activityManager is not null && (int)activityManager.LockTaskModeState != 0;
        var kioskIsActive = config.IsKioskTime(DateTime.Now);
        if (!isLocked && !kioskIsActive)
        {
            launchPanel();
            return;
        }

        ShowNotice("Settings stay locked", "Android connectivity settings can leave the kiosk. Configure Wi-Fi and Bluetooth outside school hours; the app will not unpin to open them.");
    }

    private bool HasOverlayAccess()
        => Build.VERSION.SdkInt < BuildVersionCodes.M || Settings.CanDrawOverlays(this);

    private bool HasExactAlarmAccess()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.S) return true;
        var alarm = (AlarmManager?)GetSystemService(AlarmService);
        return alarm?.CanScheduleExactAlarms() == true;
    }

    private void OpenNextReliabilityPermission()
    {
        if (config.SetupComplete && config.IsKioskTime(DateTime.Now))
        {
            ShowNotice("Setup outside school hours", "System permission settings are unavailable while the school schedule is active.");
            return;
        }
        try
        {
            var packageUri = Android.Net.Uri.Parse($"package:{PackageName}");
            if (!HasOverlayAccess())
            {
                StartActivity(new Intent(Settings.ActionManageOverlayPermission, packageUri));
                return;
            }
            if (!HasExactAlarmAccess() && Build.VERSION.SdkInt >= BuildVersionCodes.S)
            {
                StartActivity(new Intent(Settings.ActionRequestScheduleExactAlarm, packageUri));
                return;
            }
            ShowNotice("Background launch is ready", "The tablet can wake School Kiosk at the scheduled start time, even while another app or the Home screen is visible.");
        }
        catch
        {
            StartActivity(new Intent(Settings.ActionApplicationDetailsSettings,
                Android.Net.Uri.Parse($"package:{PackageName}")));
        }
    }

    private void ConfigureDeviceOwner()
    {
        var dpm = (DevicePolicyManager?)GetSystemService(DevicePolicyService);
        var admin = new ComponentName(this, Java.Lang.Class.FromType(typeof(KioskAdminReceiver))!);
        if (dpm?.IsDeviceOwnerApp(PackageName) != true) return;
        try
        {
            dpm.SetLockTaskPackages(admin, new[] { PackageName! });
            if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                dpm.SetLockTaskFeatures(admin, LockTaskFeatures.SystemInfo);
            dpm.AddUserRestriction(admin, UserManager.DisallowSafeBoot);
            dpm.AddUserRestriction(admin, UserManager.DisallowFactoryReset);
            dpm.AddUserRestriction(admin, UserManager.DisallowAddUser);
        }
        catch { }
    }

    private void UpdateDevicePolicy(bool shouldLock)
    {
        var dpm = (DevicePolicyManager?)GetSystemService(DevicePolicyService);
        var admin = new ComponentName(this, Java.Lang.Class.FromType(typeof(KioskAdminReceiver))!);
        if (dpm?.IsDeviceOwnerApp(PackageName) != true) return;
        try
        {
            // SYSTEM_INFO keeps clock/battery visible while lock-task mode prevents shade expansion.
            dpm.SetStatusBarDisabled(admin, shouldLock);
            if (shouldLock)
            {
                dpm.AddUserRestriction(admin, UserManager.DisallowAppsControl);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                    dpm.AddUserRestriction(admin, UserManager.DisallowConfigDateTime);
            }
            else
            {
                dpm.ClearUserRestriction(admin, UserManager.DisallowAppsControl);
                if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
                    dpm.ClearUserRestriction(admin, UserManager.DisallowConfigDateTime);
            }
            dpm.ClearPackagePersistentPreferredActivities(admin, PackageName!);
            if (shouldLock)
            {
                var filter = new IntentFilter(Intent.ActionMain);
                filter.AddCategory(Intent.CategoryHome);
                filter.AddCategory(Intent.CategoryDefault);
                dpm.AddPersistentPreferredActivity(admin, filter, new ComponentName(this, Java.Lang.Class.FromType(typeof(MainActivity))!));
            }
        }
        catch { }
    }

    public override void OnBackPressed()
    {
        if (web.CanGoBack()) web.GoBack();
        else CloseCurrentTab();
    }

    protected override void OnDestroy()
    {
        if (Window?.DecorView is not null)
            Window.DecorView.SystemUiVisibilityChange -= OnSystemUiVisibilityChanged;
        chargingPulse?.Cancel();
        chargingPulse?.Dispose();
        handler.RemoveCallbacksAndMessages(null);
        base.OnDestroy();
    }

    private sealed class OpenWebClient(MainActivity owner) : WebViewClient
    {
        public override bool ShouldOverrideUrlLoading(WebView? view, IWebResourceRequest? request)
            => false;

        public override void OnPageStarted(WebView? view, string? url, Bitmap? favicon)
        {
            base.OnPageStarted(view, url, favicon);
            if (view == owner.web)
            {
                owner.pageLoadFailed = false;
                owner.UpdateLoadingProgress(4);
            }
        }

        public override void OnPageFinished(WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            if (view != owner.web) return;
            owner.UpdateLoadingProgress(100);
            if (owner.IsNetworkOnline() && !owner.pageLoadFailed) owner.HideOfflineScreen();
        }

        public override void OnReceivedError(WebView? view, IWebResourceRequest? request, WebResourceError? error)
        {
            base.OnReceivedError(view, request, error);
            if (view == owner.web && request?.IsForMainFrame == true)
                owner.ShowOfflineScreen("The school website couldn't be reached. We'll keep trying automatically.");
        }

        public override bool OnRenderProcessGone(WebView? view, RenderProcessGoneDetail? detail)
            => owner.RecoverCrashedWebView(view, detail);
    }

    private sealed class TabChromeClient(MainActivity owner) : WebChromeClient
    {
        public override void OnProgressChanged(WebView? view, int newProgress)
        {
            base.OnProgressChanged(view, newProgress);
            if (view == owner.web) owner.UpdateLoadingProgress(newProgress);
        }

        public override bool OnCreateWindow(WebView? view, bool isDialog, bool isUserGesture, Message? resultMsg)
        {
            var transport = resultMsg?.Obj as WebView.WebViewTransport;
            if (transport is null) return false;
            var tab = owner.CreateTab(makeActive: true);
            if (tab is null) return false;
            transport.WebView = tab;
            resultMsg!.SendToTarget();
            return true;
        }
    }

    private sealed class NoDownloadListener(Context context) : Java.Lang.Object, IDownloadListener
    {
        public void OnDownloadStart(string? url, string? userAgent, string? contentDisposition, string? mimetype, long contentLength) =>
            Toast.MakeText(context, "Downloads are disabled in school mode", ToastLength.Short)!.Show();
    }
}
