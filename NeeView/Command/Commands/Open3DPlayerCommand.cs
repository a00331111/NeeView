using NeeView.Properties;

namespace NeeView
{
    /// <summary>
    /// 使用 FFmpeg 裸眼 3D 播放器打开当前视频
    /// </summary>
    public class Open3DPlayerCommand : CommandElement
    {
        public Open3DPlayerCommand()
        {
            this.Group = TextResources.GetString("CommandGroup.Video");
            this.IsShowMessage = true;
        }

        public override string ExecuteMessage(object? sender, CommandContext e)
        {
            return TextResources.GetString("Open3DPlayerCommand");
        }

        public override bool CanExecute(object? sender, CommandContext e)
        {
            // Check if current page is a media file
            var book = BookOperation.Current.Book;
            if (book == null) return false;

            var page = book.CurrentPage;
            if (page == null) return false;

            var entry = page.ArchiveEntry;
            if (entry == null) return false;

            // Check if it's a video file
            var ext = entry.Extension?.ToLower();
            if (ext == null) return false;

            var supportedExtensions = Config.Current.Archive.Media.SupportFileTypes;
            return supportedExtensions.Contains(ext);
        }

        public override void Execute(object? sender, CommandContext e)
        {
            var book = BookOperation.Current.Book;
            if (book == null) return;

            var page = book.CurrentPage;
            if (page == null) return;

            var entry = page.ArchiveEntry;
            if (entry == null) return;

            // Get the file path
            var filePath = entry.EntityPath;
            if (string.IsNullOrEmpty(filePath)) return;

            // Open the 3D player window
            Stereo3DPlayerWindow.Open(filePath);
        }
    }
}
