using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace NeeView
{
    /// <summary>
    /// FFmpeg-based video decoder that outputs raw frames to stdout
    /// </summary>
    public class FFmpegVideoDecoder : IDisposable
    {
        private Process? _ffmpegProcess;
        private bool _disposedValue;
        private readonly string _videoPath;
        private int _width;
        private int _height;
        private double _fps;
        private double _duration;
        private CancellationTokenSource? _cts;
        private Task? _decodeTask;

        public event EventHandler<VideoFrameEventArgs>? FrameReady;
        public event EventHandler? PlaybackEnded;
        public event EventHandler<string>? ErrorOccurred;

        public FFmpegVideoDecoder(string videoPath)
        {
            _videoPath = videoPath;
        }

        public int Width => _width;
        public int Height => _height;
        public double Fps => _fps;
        public double Duration => _duration;
        public bool IsPlaying { get; private set; }
        public double Position { get; private set; }

        /// <summary>
        /// Probe video metadata using ffprobe
        /// </summary>
        public async Task<bool> ProbeAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{_videoPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse basic info from JSON
                // Simple parsing to avoid adding JSON dependency
                _width = ExtractInt(output, "\"width\":");
                _height = ExtractInt(output, "\"height\":");
                _fps = ExtractDouble(output, "\"r_frame_rate\":");
                _duration = ExtractDouble(output, "\"duration\":");

                if (_width == 0 || _height == 0)
                {
                    // Try to get from codec_width/codec_height
                    _width = ExtractInt(output, "\"codec_width\":");
                    _height = ExtractInt(output, "\"codec_height\":");
                }

                if (_fps <= 0) _fps = 30.0;
                if (_duration <= 0) _duration = 0;

                return _width > 0 && _height > 0;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, $"ffprobe failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start decoding and playing video
        /// </summary>
        public void Play()
        {
            if (IsPlaying) return;
            IsPlaying = true;
            StartDecode();
        }

        /// <summary>
        /// Stop decoding
        /// </summary>
        public void Stop()
        {
            IsPlaying = false;
            _cts?.Cancel();
            try
            {
                _ffmpegProcess?.Kill();
            }
            catch { }
        }

        /// <summary>
        /// Seek to position (in seconds)
        /// </summary>
        public void Seek(double seconds)
        {
            Position = seconds;
            if (IsPlaying)
            {
                Stop();
                Play();
            }
        }

        private void StartDecode()
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            _decodeTask = Task.Run(() =>
            {
                try
                {
                    var args = $"-i \"{_videoPath}\" -ss {Position:F3} -f rawvideo -pix_fmt bgr24 -vf scale={_width}:{_height} -";
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    _ffmpegProcess = Process.Start(psi);
                    if (_ffmpegProcess == null) return;

                    var frameSize = _width * _height * 3; // BGR24
                    var buffer = new byte[frameSize];
                    var frameInterval = _fps > 0 ? 1000.0 / _fps : 33.33; // ms per frame
                    var sw = Stopwatch.StartNew();
                    long frameCount = 0;

                    while (!token.IsCancellationRequested && IsPlaying)
                    {
                        var bytesRead = 0;
                        while (bytesRead < frameSize)
                        {
                            var read = _ffmpegProcess.StandardOutput.BaseStream.Read(buffer, bytesRead, frameSize - bytesRead);
                            if (read == 0) break;
                            bytesRead += read;
                        }

                        if (bytesRead < frameSize) break; // End of stream

                        // Calculate position
                        frameCount++;
                        Position += 1.0 / _fps;

                        // Create bitmap from raw data
                        var bitmap = CreateBitmapFromBgr24(buffer, _width, _height);
                        FrameReady?.Invoke(this, new VideoFrameEventArgs(bitmap, Position));

                        // Frame timing
                        var elapsed = sw.ElapsedMilliseconds;
                        var targetTime = frameCount * frameInterval;
                        if (targetTime > elapsed)
                        {
                            var delay = (int)(targetTime - elapsed);
                            if (delay > 0) Thread.Sleep(Math.Min(delay, 100));
                        }
                    }

                    _ffmpegProcess.WaitForExit();
                    if (!token.IsCancellationRequested)
                    {
                        PlaybackEnded?.Invoke(this, EventArgs.Empty);
                    }
                }
                catch (Exception ex) when (!token.IsCancellationRequested)
                {
                    ErrorOccurred?.Invoke(this, $"FFmpeg decode error: {ex.Message}");
                }
                finally
                {
                    IsPlaying = false;
                }
            }, token);
        }

        private static WriteableBitmap CreateBitmapFromBgr24(byte[] data, int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            bitmap.Lock();
            System.Runtime.InteropServices.Marshal.Copy(data, 0, bitmap.BackBuffer, data.Length);
            bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, width, height));
            bitmap.Unlock();
            bitmap.Freeze(); // Make it accessible from any thread
            return bitmap;
        }

        private static int ExtractInt(string json, string key)
        {
            var idx = json.IndexOf(key);
            if (idx < 0) return 0;
            idx += key.Length;
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '"')) idx++;
            var start = idx;
            while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '"')) idx++;
            var str = json[start..idx].TrimEnd('"');
            return int.TryParse(str, out var val) ? val : 0;
        }

        private static double ExtractDouble(string json, string key)
        {
            var idx = json.IndexOf(key);
            if (idx < 0) return 0;
            idx += key.Length;
            // Handle "num/den" format for fps
            while (idx < json.Length && (json[idx] == ' ' || json[idx] == '"')) idx++;
            var start = idx;
            while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '.' || json[idx] == '/' || json[idx] == '"')) idx++;
            var str = json[start..idx].TrimEnd('"');
            if (str.Contains('/'))
            {
                var parts = str.Split('/');
                if (parts.Length == 2 && double.TryParse(parts[0], out var num) && double.TryParse(parts[1], out var den) && den > 0)
                    return num / den;
            }
            return double.TryParse(str, out var val) ? val : 0;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    Stop();
                    _cts?.Dispose();
                    _ffmpegProcess?.Dispose();
                }
                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    public class VideoFrameEventArgs : EventArgs
    {
        public WriteableBitmap Bitmap { get; }
        public double Position { get; }

        public VideoFrameEventArgs(WriteableBitmap bitmap, double position)
        {
            Bitmap = bitmap;
            Position = position;
        }
    }
}
