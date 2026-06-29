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
    public class VlcMediaPlayerCanvas : MediaPlayerCanvas, IDisposable, IHasScalingMode
    {
        private readonly VlcMediaPlayer _player;
#if USE_IMAGEBRUSH
        private readonly ImageBrush _videoBrush;
        private readonly Rectangle _videoLayer;
        private readonly ImageBrush _imageBlush;
        private readonly Rectangle _imageLayer;

        // Stereo mode elements
        private readonly ImageBrush _leftBrush;
        private readonly ImageBrush _rightBrush;
        private readonly Rectangle _leftLayer;
        private readonly Rectangle _rightLayer;
        private readonly StackPanel _stereoPanel;
#endif
        private readonly AudioCard _audioCard;
        private readonly TextBlock _errorMessageTextBlock;
        private bool _disposedValue;
        private BitmapScalingMode? _scalingMode;
        private PageFrameElement _element;
        private ViewContentSize _contentSize;
        private bool _imageInitialized;
        private Rect _currentViewbox;
        private bool _isStereoMode;

        public VlcMediaPlayerCanvas(PageFrameElement element, MediaViewData source, ViewContentSize contentSize, Rect viewbox, VlcMediaPlayer player)
        {
            Debug.WriteLine($"Create.VlcMediaPlayer: {source.MediaSource}");

            _element = element;
            _contentSize = contentSize;
            _currentViewbox = viewbox;

            _player = player;
            _player.MediaPlayed += Player_MediaPlayed;
            _player.MediaFailed += Player_MediaFailed;
            _player.PropertyChanged += Player_PropertyChanged;

#if USE_IMAGEBRUSH

            _videoBrush = new ImageBrush()
            {
                ImageSource = _player.SourceProvider.VideoSource,
                Stretch = Stretch.Fill,
                Viewbox = viewbox,
            };

            _player.SourceProvider.PropertyChanged += SourceProvider_PropertyChanged;

            _videoLayer = new Rectangle()
            {
                Fill = _videoBrush,
                Visibility = Visibility.Hidden,
            };

            _imageBlush = new ImageBrush()
            {
                ImageSource = source.ImageSource,
                Stretch = Stretch.Fill,
                Viewbox = viewbox,
            };

            _imageLayer = new Rectangle()
            {
                Fill = _imageBlush,
            };

            // Stereo mode elements
            var leftViewbox = new Rect(viewbox.X, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);
            var rightViewbox = new Rect(viewbox.X + viewbox.Width * 0.5, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);

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

            _stereoPanel = new StackPanel()
            {
                Orientation = Orientation.Horizontal,
                Visibility = Visibility.Collapsed,
            };
            _stereoPanel.Children.Add(_leftLayer);
            _stereoPanel.Children.Add(_rightLayer);

#else
            _videoLayer = new Image()
            {
                Stretch = Stretch.Fill
            };
            RenderOptions.SetBitmapScalingMode(_videoLayer, BitmapScalingMode.Fant);
            _videoLayer.SetBinding(Image.SourceProperty, new Binding(nameof(VlcVideoSourceProvider.VideoSource)) { Source = _player.SourceProvider });

            _imageLayer = new Image()
            {
                Source = source.ImageSource
            };

#endif


            var drawingImage = new DrawingImage()
            {
                Drawing = new GeometryDrawing()
                {
                    Brush = Brushes.Gray,
                    Geometry = App.Current.Resources["g_music"] as PathGeometry,
                },
            };
            drawingImage.Freeze();


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
            this.Children.Add(_videoLayer);
            this.Children.Add(_stereoPanel);
            if (source.ImageSource is not null)
            {
                this.Children.Add(_imageLayer);
            }
            this.Children.Add(_audioCard);
            this.Children.Add(_errorMessageTextBlock);

            UpdateMediaType();

            // Listen for stereo mode config changes
            Config.Current.Archive.Media.PropertyChanged += MediaConfig_PropertyChanged;
            _isStereoMode = Config.Current.Archive.Media.StereoMode != StereoMode.None;
            UpdateStereoMode();

            // image scaling mode
            _contentSize.SizeChanged += ContentSize_SizeChanged;
        }

        private void MediaConfig_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_disposedValue) return;
            if (e.PropertyName == nameof(MediaArchiveConfig.StereoMode))
            {
                AppDispatcher.BeginInvoke(() =>
                {
                    _isStereoMode = Config.Current.Archive.Media.StereoMode != StereoMode.None;
                    UpdateStereoMode();
                });
            }
            if (e.PropertyName == nameof(MediaArchiveConfig.StereoGap))
            {
                AppDispatcher.BeginInvoke(() => UpdateStereoGap());
            }
        }

        private void UpdateStereoMode()
        {
            if (_isStereoMode)
            {
                _videoLayer.Visibility = Visibility.Collapsed;
                _stereoPanel.Visibility = Visibility.Visible;
                UpdateStereoGap();
            }
            else
            {
                _stereoPanel.Visibility = Visibility.Collapsed;
                if (_player.SourceProvider.VideoSource != null)
                {
                    _videoLayer.Visibility = Visibility.Visible;
                }
            }
        }

        private void UpdateStereoGap()
        {
            var gap = Config.Current.Archive.Media.StereoGap;
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
                _videoBrush.ImageSource = _player.SourceProvider.VideoSource;
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

            _videoLayer.Visibility = Visibility.Collapsed;
            _stereoPanel.Visibility = Visibility.Collapsed;
            _imageLayer.Visibility = Visibility.Collapsed;

            _errorMessageTextBlock.Text = e.ErrorException.Message;
            _errorMessageTextBlock.Visibility = Visibility.Visible;
        }

        public override void SetViewbox(Rect viewbox)
        {
            _currentViewbox = viewbox;
#if USE_IMAGEBRUSH
            _videoBrush.Viewbox = viewbox;
            _imageBlush.Viewbox = viewbox;

            // Update stereo viewboxes
            _leftBrush.Viewbox = new Rect(viewbox.X, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);
            _rightBrush.Viewbox = new Rect(viewbox.X + viewbox.Width * 0.5, viewbox.Y, viewbox.Width * 0.5, viewbox.Height);
#endif
            UpdateBitmapScalingMode();
        }

        private void ShowVideo()
        {
            if (_isStereoMode)
            {
                _videoLayer.Visibility = Visibility.Collapsed;
                _stereoPanel.Visibility = Visibility.Visible;
                _leftLayer.Visibility = Visibility.Visible;
                _rightLayer.Visibility = Visibility.Visible;
            }
            else
            {
                _videoLayer.Visibility = Visibility.Visible;
                _stereoPanel.Visibility = Visibility.Collapsed;
            }
            _imageLayer.Visibility = Visibility.Collapsed;
        }

        private void UpdateBitmapScalingMode()
        {
            var image = _player.SourceProvider.VideoSource;
            if (image is null) return;

#if USE_IMAGEBRUSH
            var imageSize = _videoBrush.ImageSource is BitmapSource bitmapSource ? new Size(bitmapSource.PixelWidth, bitmapSource.PixelHeight) : new Size(image.Width, image.Height);
#else
            var imageSize = new Size(image.Width, image.Height);
#endif

            ViewContentTools.SetBitmapScalingMode(_videoLayer, imageSize, _contentSize, _scalingMode);
            ViewContentTools.SetBitmapScalingMode(_leftLayer, imageSize, _contentSize, _scalingMode);
            ViewContentTools.SetBitmapScalingMode(_rightLayer, imageSize, _contentSize, _scalingMode);
        }
    }
}
