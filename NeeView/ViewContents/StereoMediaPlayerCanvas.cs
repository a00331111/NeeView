#define USE_IMAGEBRUSH

using NeeView.PageFrames;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Vlc.DotNet.Wpf;

namespace NeeView
{
    /// <summary>
    /// MediaPlayerCanvas that renders video in side-by-side 3D stereo mode.
    /// Shows the same video twice (left half and right half) with an adjustable gap.
    /// </summary>
    public class StereoMediaPlayerCanvas : MediaPlayerCanvas, IDisposable, IHasScalingMode
    {
        private readonly VlcMediaPlayer _player;
#if USE_IMAGEBRUSH
        private readonly ImageBrush _leftBrush;
        private readonly ImageBrush _rightBrush;
        private readonly Rectangle _leftLayer;
        private readonly Rectangle _rightLayer;
        private readonly ImageBrush _imageBrush;
        private readonly Rectangle _imageLayer;
#else
        private readonly Image _leftLayer;
        private readonly Image _rightLayer;
        private readonly Image _imageLayer;
#endif
        private readonly StackPanel _stereoPanel;
        private readonly AudioCard _audioCard;
        private readonly TextBlock _errorMessageTextBlock;
        private bool _disposedValue;
        private BitmapScalingMode? _scalingMode;
        private PageFrameElement _element;
        private ViewContentSize _contentSize;
        private bool _imageInitialized;

        public StereoMediaPlayerCanvas(PageFrameElement element, MediaViewData source, ViewContentSize contentSize, Rect viewbox, VlcMediaPlayer player)
        {
            Debug.WriteLine($"Create.StereoMediaPlayer: {source.MediaSource}");

            _element = element;
            _contentSize = contentSize;

            _player = player;
            _player.MediaPlayed += Player_MediaPlayed;
            _player.MediaFailed += Player_MediaFailed;
            _player.PropertyChanged += Player_PropertyChanged;

            // Left eye viewbox: left half of the video
            var leftViewbox = new Rect(viewbox.X, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);
            // Right eye viewbox: right half of the video
            var rightViewbox = new Rect(viewbox.X + viewbox.Width * 0.5, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);

#if USE_IMAGEBRUSH
            _leftBrush = new ImageBrush()
            {
                ImageSource = _player.SourceProvider.VideoSource,
                Stretch = Stretch.Fill,
                Viewbox = leftViewbox,
            };

            _rightBrush = new ImageBrush()
            {
                ImageSource = _player.SourceProvider.VideoSource,
                Stretch = Stretch.Fill,
                Viewbox = rightViewbox,
            };

            _player.SourceProvider.PropertyChanged += SourceProvider_PropertyChanged;

            _leftLayer = new Rectangle()
            {
                Fill = _leftBrush,
                Visibility = Visibility.Hidden,
            };

            _rightLayer = new Rectangle()
            {
                Fill = _rightBrush,
                Visibility = Visibility.Hidden,
            };

            _imageBrush = new ImageBrush()
            {
                ImageSource = source.ImageSource,
                Stretch = Stretch.Fill,
                Viewbox = viewbox,
            };

            _imageLayer = new Rectangle()
            {
                Fill = _imageBrush,
            };
#else
            _leftLayer = new Image()
            {
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(_leftLayer, BitmapScalingMode.Fant);
            _leftLayer.SetBinding(Image.SourceProperty, new Binding(nameof(VlcVideoSourceProvider.VideoSource)) { Source = _player.SourceProvider });

            _rightLayer = new Image()
            {
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(_rightLayer, BitmapScalingMode.Fant);
            _rightLayer.SetBinding(Image.SourceProperty, new Binding(nameof(VlcVideoSourceProvider.VideoSource)) { Source = _player.SourceProvider });

            _imageLayer = new Image()
            {
                Source = source.ImageSource
            };
#endif

            // Stereo panel: two views side by side
            _stereoPanel = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
            };
            _stereoPanel.Children.Add(_leftLayer);
            _stereoPanel.Children.Add(_rightLayer);

            _audioCard = new AudioCard()
            {
                AudioInfo = source.MediaSource.AudioInfo,
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
            };

            _errorMessageTextBlock = new TextBlock()
            {
                Background = Brushes.Black,
                Foreground = Brushes.White,
                Padding = new Thickness(40, 20, 40, 20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 20,
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };

            // root grid
            this.Background = Brushes.Black;
            this.Children.Add(_stereoPanel);
            if (source.ImageSource is not null)
            {
                this.Children.Add(_imageLayer);
            }
            this.Children.Add(_audioCard);
            this.Children.Add(_errorMessageTextBlock);

            UpdateMediaType();
            UpdateStereoGap();

            // Listen for config changes
            Config.Current.Archive.Media.PropertyChanged += MediaConfig_PropertyChanged;

            // image scaling mode
            _contentSize.SizeChanged += ContentSize_SizeChanged;
        }

        private void MediaConfig_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_disposedValue) return;
            if (e.PropertyName == nameof(MediaArchiveConfig.StereoGap))
            {
                AppDispatcher.BeginInvoke(() => UpdateStereoGap());
            }
        }

        private void UpdateStereoGap()
        {
            var gap = Config.Current.Archive.Media.StereoGap;
            // Apply gap as margin between left and right views
            // Gap is in percentage (0-100), scale to reasonable pixel range
            var gapPixels = gap * 2.0;
            _leftLayer.Margin = new Thickness(0, 0, gapPixels * 0.5, 0);
            _rightLayer.Margin = new Thickness(gapPixels * 0.5, 0, 0, 0);
        }

        private void Player_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(_player.HasVideo))
            {
                AppDispatcher.BeginInvoke(() => UpdateMediaType());
            }
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(_player.HasAudio))
            {
                AppDispatcher.BeginInvoke(() => UpdateMediaType());
            }
        }

        private void UpdateMediaType()
        {
            var isSoundOnly = _player.HasAudio && !_player.HasVideo;
            _audioCard.Visibility = isSoundOnly ? Visibility.Visible : Visibility.Collapsed;
        }


        /// <summary>
        /// BitmapScaleMode指定。Printerで使用される。
        /// </summary>
        public BitmapScalingMode? ScalingMode
        {
            get { return _scalingMode; }
            set
            {
                if (_scalingMode != value)
                {
                    _scalingMode = value;
                    UpdateBitmapScalingMode();
                }
            }
        }


        protected override void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    _contentSize.SizeChanged -= ContentSize_SizeChanged;
                    _player.MediaPlayed -= Player_MediaPlayed;
                    _player.MediaFailed -= Player_MediaFailed;
                    _player.PropertyChanged -= Player_PropertyChanged;
                    Config.Current.Archive.Media.PropertyChanged -= MediaConfig_PropertyChanged;
#if USE_IMAGEBRUSH
                    _player.SourceProvider.PropertyChanged -= SourceProvider_PropertyChanged;
#endif
                }
                _disposedValue = true;
            }
            base.Dispose(disposing);
        }


#if USE_IMAGEBRUSH
        private void SourceProvider_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_disposedValue) return;
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(VlcVideoSourceProvider.VideoSource))
            {
                _leftBrush.ImageSource = _player.SourceProvider.VideoSource;
                _rightBrush.ImageSource = _player.SourceProvider.VideoSource;

                if (!_imageInitialized)
                {
                    _imageInitialized = true;
                    UpdateBitmapScalingMode();
                }
            }
        }
#endif

        private void ContentSize_SizeChanged(object? sender, ViewContentSizeChangedEventArgs e)
        {
            UpdateBitmapScalingMode();
        }

        private void Player_MediaPlayed(object? sender, EventArgs e)
        {
            ShowVideo();
        }

        private void Player_MediaFailed(object? sender, ExceptionEventArgs e)
        {
            if (_errorMessageTextBlock is null) return;

            _stereoPanel.Visibility = Visibility.Collapsed;
            _imageLayer.Visibility = Visibility.Collapsed;

            _errorMessageTextBlock.Text = e.ErrorException.Message;
            _errorMessageTextBlock.Visibility = Visibility.Visible;
        }

        public override void SetViewbox(Rect viewbox)
        {
#if USE_IMAGEBRUSH
            // Split viewbox for stereo: left half and right half
            _leftBrush.Viewbox = new Rect(viewbox.X, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);
            _rightBrush.Viewbox = new Rect(viewbox.X + viewbox.Width * 0.5, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);
            _imageBrush.Viewbox = viewbox;
#endif
            UpdateBitmapScalingMode();
        }

        private void ShowVideo()
        {
            _stereoPanel.Visibility = Visibility.Visible;
            _leftLayer.Visibility = Visibility.Visible;
            _rightLayer.Visibility = Visibility.Visible;
            _imageLayer.Visibility = Visibility.Collapsed;
        }

        private void UpdateBitmapScalingMode()
        {
            var image = _player.SourceProvider.VideoSource;
            if (image is null) return;

            var imageSize = _leftBrush.ImageSource is BitmapSource bitmapSource ? new Size(bitmapSource.PixelWidth, bitmapSource.PixelHeight) : new Size(image.Width, image.Height);

            ViewContentTools.SetBitmapScalingMode(_leftLayer, imageSize, _contentSize, _scalingMode);
            ViewContentTools.SetBitmapScalingMode(_rightLayer, imageSize, _contentSize, _scalingMode);
        }
    }
}
