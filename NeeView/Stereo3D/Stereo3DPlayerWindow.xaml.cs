using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NeeView
{
    /// <summary>
    /// 裸眼 3D 播放器窗口 - 使用 FFmpeg 解码视频，支持左右并排立体显示
    /// </summary>
    public partial class Stereo3DPlayerWindow : Window
    {
        private FFmpegVideoDecoder? _decoder;
        private readonly string _videoPath;
        private bool _isStereoMode = true;
        private bool _isPlaying;
        private bool _isMuted;
        private bool _isFullscreen = true;
        private bool _isDraggingSlider;
        private bool _showControls = true;
        private DispatcherTimer? _controlsTimer;
        private DispatcherTimer? _positionTimer;
        private double _stereoGap;

        // Singleton guard
        private static Stereo3DPlayerWindow? _current;

        public Stereo3DPlayerWindow(string videoPath)
        {
            InitializeComponent();
            _videoPath = videoPath;
            _current = this;

            // Controls auto-hide timer
            _controlsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _controlsTimer.Tick += (s, e) =>
            {
                if (_isPlaying)
                {
                    _showControls = false;
                    ControlsOverlay.Visibility = Visibility.Collapsed;
                }
            };

            // Position update timer
            _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _positionTimer.Tick += PositionTimer_Tick;

            Loaded += Window_Loaded;
        }

        /// <summary>
        /// Open the 3D player window for a video file (singleton pattern)
        /// </summary>
        public static void Open(string videoPath)
        {
            if (_current != null)
            {
                _current.Activate();
                return;
            }

            var window = new Stereo3DPlayerWindow(videoPath);
            window.Show();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _decoder = new FFmpegVideoDecoder(_videoPath);
                _decoder.FrameReady += Decoder_FrameReady;
                _decoder.PlaybackEnded += Decoder_PlaybackEnded;
                _decoder.ErrorOccurred += Decoder_ErrorOccurred;

                LoadingText.Text = "正在探测视频信息...";

                var success = await _decoder.ProbeAsync();
                if (!success)
                {
                    LoadingText.Text = "无法打开视频文件，请确保已安装 FFmpeg";
                    return;
                }

                LoadingText.Text = $"加载中... {_decoder.Width}x{_decoder.Height}";
                Title = $"裸眼 3D 播放器 - {_videoPath}";

                // Update time display
                UpdateTimeDisplay(0, _decoder.Duration);

                // Start playback
                _decoder.Play();
                _isPlaying = true;
                _positionTimer?.Start();
                UpdateStereoMode();
            }
            catch (Exception ex)
            {
                LoadingText.Text = $"错误: {ex.Message}";
            }
        }

        private void Decoder_FrameReady(object? sender, VideoFrameEventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                LoadingText.Visibility = Visibility.Collapsed;

                if (_isStereoMode)
                {
                    // Split frame into left and right halves
                    var halfWidth = e.Bitmap.PixelWidth / 2;
                    var leftBitmap = new CroppedBitmap(e.Bitmap, new Int32Rect(0, 0, halfWidth, e.Bitmap.PixelHeight));
                    var rightBitmap = new CroppedBitmap(e.Bitmap, new Int32Rect(halfWidth, 0, halfWidth, e.Bitmap.PixelHeight));
                    leftBitmap.Freeze();
                    rightBitmap.Freeze();
                    LeftEye.Source = leftBitmap;
                    RightEye.Source = rightBitmap;
                }
                else
                {
                    VideoImage.Source = e.Bitmap;
                }

                // Update progress
                if (!_isDraggingSlider && _decoder != null)
                {
                    ProgressSlider.Value = _decoder.Duration > 0 ? e.Position / _decoder.Duration : 0;
                }
            });
        }

        private void Decoder_PlaybackEnded(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isPlaying = false;
                PlayPauseIcon.Text = "▶";
                _positionTimer?.Stop();
            });
        }

        private void Decoder_ErrorOccurred(object? sender, string error)
        {
            Dispatcher.BeginInvoke(() =>
            {
                LoadingText.Text = error;
                LoadingText.Visibility = Visibility.Visible;
            });
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_decoder != null)
            {
                UpdateTimeDisplay(_decoder.Position, _decoder.Duration);
            }
        }

        private void UpdateTimeDisplay(double current, double total)
        {
            TimeText.Text = $"{FormatTime(current)} / {FormatTime(total)}";
        }

        private static string FormatTime(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }

        private void UpdateStereoMode()
        {
            if (_isStereoMode)
            {
                VideoImage.Visibility = Visibility.Collapsed;
                StereoPanel.Visibility = Visibility.Visible;
            }
            else
            {
                VideoImage.Visibility = Visibility.Visible;
                StereoPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateStereoGap()
        {
            var gap = _stereoGap * 2.0;
            LeftEye.Margin = new Thickness(0, 0, gap * 0.5, 0);
            RightEye.Margin = new Thickness(gap * 0.5, 0, 0, 0);
        }

        #region Event Handlers

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_decoder == null) return;

            if (_isPlaying)
            {
                _decoder.Stop();
                _isPlaying = false;
                PlayPauseIcon.Text = "▶";
                _positionTimer?.Stop();
            }
            else
            {
                _decoder.Play();
                _isPlaying = true;
                PlayPauseIcon.Text = "⏸";
                _positionTimer?.Start();
            }
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            MuteIcon.Text = _isMuted ? "🔇" : "🔊";
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // FFmpeg raw output doesn't have audio control in this simple implementation
        }

        private void ProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void ProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isDraggingSlider = false;
            if (_decoder != null && _decoder.Duration > 0)
            {
                var targetSeconds = ProgressSlider.Value * _decoder.Duration;
                _decoder.Seek(targetSeconds);
            }
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // Handled in drag completed
        }

        private void GapSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _stereoGap = GapSlider.Value;
            GapText.Text = $"{(int)_stereoGap}%";
            UpdateStereoGap();
        }

        private void StereoToggle_Click(object sender, RoutedEventArgs e)
        {
            _isStereoMode = !_isStereoMode;
            UpdateStereoMode();
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                _isFullscreen = false;
                FullscreenIcon.Text = "⛶";
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                _isFullscreen = true;
                FullscreenIcon.Text = "⊡";
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Space:
                    PlayPause_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.F:
                    Fullscreen_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.S:
                    StereoToggle_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.M:
                    Mute_Click(sender, e);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    if (_isFullscreen)
                    {
                        Fullscreen_Click(sender, e);
                    }
                    else
                    {
                        Close();
                    }
                    e.Handled = true;
                    break;
                case Key.Left:
                    if (_decoder != null)
                    {
                        _decoder.Seek(Math.Max(0, _decoder.Position - 5));
                    }
                    e.Handled = true;
                    break;
                case Key.Right:
                    if (_decoder != null)
                    {
                        _decoder.Seek(_decoder.Position + 5);
                    }
                    e.Handled = true;
                    break;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            _showControls = true;
            ControlsOverlay.Visibility = Visibility.Visible;
            _controlsTimer?.Stop();
            if (_isPlaying)
            {
                _controlsTimer?.Start();
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isPlaying)
            {
                _showControls = false;
                ControlsOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _decoder?.Stop();
            _decoder?.Dispose();
            _controlsTimer?.Stop();
            _positionTimer?.Stop();
            _current = null;
        }

        #endregion
    }
}
