using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace JellyPot.App.Behaviors;

public static class SmoothScrollBehavior
{
    public const bool FollowsDisplayRefreshRate = true;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty EnableMotionBlurProperty = DependencyProperty.RegisterAttached(
        "EnableMotionBlur", typeof(bool), typeof(SmoothScrollBehavior), new PropertyMetadata(false));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(SmoothScrollState), typeof(SmoothScrollBehavior));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetEnableMotionBlur(DependencyObject element) => (bool)element.GetValue(EnableMotionBlurProperty);
    public static void SetEnableMotionBlur(DependencyObject element, bool value) => element.SetValue(EnableMotionBlurProperty, value);

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not FrameworkElement host) return;
        if ((bool)eventArgs.NewValue)
        {
            if (host.GetValue(StateProperty) is not null) return;
            var state = new SmoothScrollState(host);
            host.SetValue(StateProperty, state);
            state.Attach();
        }
        else if (host.GetValue(StateProperty) is SmoothScrollState state)
        {
            state.Dispose();
            host.ClearValue(StateProperty);
        }
    }

    private sealed class SmoothScrollState(FrameworkElement host) : IDisposable
    {
        private const double WheelLinePixels = 40d;
        private const double SpringStiffness = 210d;
        private const double SpringDamping = 24d;
        private const double StopDistance = 0.35d;
        private const double StopVelocity = 4d;
        private const double MaximumBlurRadius = 1.25d;
        private readonly FrameworkElement _host = host;
        private ScrollViewer? _scrollViewer;
        private FrameworkElement? _blurTarget;
        private Effect? _originalEffect;
        private BlurEffect? _motionBlur;
        private double _targetOffset;
        private double _lastAppliedOffset = double.NaN;
        private double _velocity;
        private long _lastRenderTimestamp;
        private bool _isAnimating;
        private bool _isApplyingOffset;

        public void Attach()
        {
            _host.Loaded += OnLoaded;
            _host.Unloaded += OnUnloaded;
            _host.PreviewMouseWheel += OnPreviewMouseWheel;
            _host.PreviewMouseLeftButtonDown += OnPreviewMouseLeftButtonDown;
            if (_host.IsLoaded) Connect();
        }

        public void Dispose()
        {
            StopAnimation();
            Disconnect();
            _host.Loaded -= OnLoaded;
            _host.Unloaded -= OnUnloaded;
            _host.PreviewMouseWheel -= OnPreviewMouseWheel;
            _host.PreviewMouseLeftButtonDown -= OnPreviewMouseLeftButtonDown;
        }

        private void OnLoaded(object sender, RoutedEventArgs eventArgs) => Connect();

        private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
        {
            StopAnimation();
            Disconnect();
        }

        private void Connect()
        {
            if (_scrollViewer is not null) return;
            _scrollViewer = FindScrollViewer(_host);
            if (_scrollViewer is null) return;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _targetOffset = _scrollViewer.VerticalOffset;
            _lastAppliedOffset = _targetOffset;
            if (GetEnableMotionBlur(_host))
            {
                _blurTarget = FindVisualChild<ItemsPresenter>(_host) ?? _host;
                _originalEffect = _blurTarget.Effect;
            }
        }

        private void Disconnect()
        {
            if (_scrollViewer is null) return;
            ClearMotionBlur();
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
            _blurTarget = null;
            _originalEffect = null;
        }

        private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs eventArgs)
        {
            Connect();
            if (_scrollViewer is null || eventArgs.Delta == 0 || _scrollViewer.ScrollableHeight <= 0) return;

            var lines = SystemParameters.WheelScrollLines;
            if (lines == 0) return;
            var distance = lines < 0
                ? Math.Max(WheelLinePixels, _scrollViewer.ViewportHeight * 0.9d)
                : Math.Max(1d, lines) * WheelLinePixels;
            var requestedOffset = _targetOffset - eventArgs.Delta / 120d * distance;
            var clampedOffset = Math.Clamp(requestedOffset, 0d, _scrollViewer.ScrollableHeight);
            if (Math.Abs(clampedOffset - _targetOffset) < 0.01d && !_isAnimating) return;

            _targetOffset = clampedOffset;
            eventArgs.Handled = true;
            StartAnimation();
        }

        private void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs eventArgs)
        {
            if (_scrollViewer is null) return;
            StopAnimation();
            _targetOffset = _scrollViewer.VerticalOffset;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs eventArgs)
        {
            if (_scrollViewer is null) return;
            if (eventArgs.VerticalChange == 0d)
            {
                _targetOffset = Math.Clamp(_targetOffset, 0d, _scrollViewer.ScrollableHeight);
                return;
            }
            if (_isApplyingOffset || (_isAnimating && Math.Abs(_scrollViewer.VerticalOffset - _lastAppliedOffset) < 1.5d)) return;
            StopAnimation();
            _targetOffset = _scrollViewer.VerticalOffset;
        }

        private void StartAnimation()
        {
            if (_isAnimating) return;
            _isAnimating = true;
            _lastRenderTimestamp = Stopwatch.GetTimestamp();
            CompositionTarget.Rendering += OnRendering;
        }

        private void StopAnimation()
        {
            if (!_isAnimating) return;
            _isAnimating = false;
            _velocity = 0d;
            ClearMotionBlur();
            CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs eventArgs)
        {
            if (_scrollViewer is null)
            {
                StopAnimation();
                return;
            }

            var timestamp = Stopwatch.GetTimestamp();
            var elapsedSeconds = (timestamp - _lastRenderTimestamp) / (double)Stopwatch.Frequency;
            _lastRenderTimestamp = timestamp;
            if (elapsedSeconds <= 0d) return;
            var frameStep = Math.Min(elapsedSeconds, 0.05d);

            _targetOffset = Math.Clamp(_targetOffset, 0d, _scrollViewer.ScrollableHeight);
            var currentOffset = _scrollViewer.VerticalOffset;
            var remaining = _targetOffset - currentOffset;
            var acceleration = remaining * SpringStiffness - _velocity * SpringDamping;
            _velocity += acceleration * frameStep;
            var nextOffset = Math.Clamp(currentOffset + _velocity * frameStep, 0d, _scrollViewer.ScrollableHeight);
            if (nextOffset is <= 0d || nextOffset >= _scrollViewer.ScrollableHeight) _velocity = 0d;
            if (Math.Abs(remaining) < StopDistance && Math.Abs(_velocity) < StopVelocity) nextOffset = _targetOffset;
            UpdateMotionBlur();

            _lastAppliedOffset = nextOffset;
            _isApplyingOffset = true;
            try { _scrollViewer.ScrollToVerticalOffset(nextOffset); }
            finally { _isApplyingOffset = false; }

            if (nextOffset == _targetOffset && Math.Abs(_velocity) < StopVelocity) StopAnimation();
        }

        private void UpdateMotionBlur()
        {
            if (_blurTarget is null) return;
            var radius = Math.Min(MaximumBlurRadius, Math.Abs(_velocity) / 900d);
            radius = Math.Round(radius * 4d) / 4d;
            if (radius < 0.25d)
            {
                ClearMotionBlur();
                return;
            }

            _motionBlur ??= new BlurEffect { RenderingBias = RenderingBias.Performance };
            if (!ReferenceEquals(_blurTarget.Effect, _motionBlur)) _blurTarget.Effect = _motionBlur;
            if (Math.Abs(_motionBlur.Radius - radius) > 0.01d) _motionBlur.Radius = radius;
        }

        private void ClearMotionBlur()
        {
            if (_blurTarget is not null && ReferenceEquals(_blurTarget.Effect, _motionBlur)) _blurTarget.Effect = _originalEffect;
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject parent)
        {
            if (parent is ScrollViewer scrollViewer) return scrollViewer;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var result = FindScrollViewer(VisualTreeHelper.GetChild(parent, index));
                if (result is not null) return result;
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent is T match) return match;
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
            {
                var result = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, index));
                if (result is not null) return result;
            }
            return null;
        }
    }
}
