using NeeView.Properties;
using System.Windows.Data;

namespace NeeView
{
    public class ToggleStereoModeCommand : CommandElement
    {
        public ToggleStereoModeCommand()
        {
            this.Group = TextResources.GetString("CommandGroup.Video");
            this.IsShowMessage = true;
        }

        public override BindingBase CreateIsCheckedBinding()
        {
            return new Binding(nameof(MediaArchiveConfig.StereoMode)) { Source = Config.Current.Archive.Media, Converter = new StereoModeToBoolConverter() };
        }

        public override string ExecuteMessage(object? sender, CommandContext e)
        {
            var isEnabled = Config.Current.Archive.Media.StereoMode != StereoMode.None;
            return isEnabled
                ? TextResources.GetString("ToggleStereoModeCommand.Off")
                : TextResources.GetString("ToggleStereoModeCommand.On");
        }

        public override bool CanExecute(object? sender, CommandContext e)
        {
            return BookOperation.Current.MediaExists();
        }

        public override void Execute(object? sender, CommandContext e)
        {
            Config.Current.Archive.Media.StereoMode = Config.Current.Archive.Media.StereoMode == StereoMode.None
                ? StereoMode.SideBySide
                : StereoMode.None;
        }
    }

    /// <summary>
    /// Converts StereoMode to bool for IsChecked binding
    /// </summary>
    public class StereoModeToBoolConverter : IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value is StereoMode mode && mode != StereoMode.None;
        }

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (bool)value ? StereoMode.SideBySide : StereoMode.None;
        }
    }
}
