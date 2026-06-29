using NeeView.Properties;
using System;

namespace NeeView
{
    public class SetStereoGapCommand : CommandElement
    {
        public SetStereoGapCommand()
        {
            this.Group = TextResources.GetString("CommandGroup.Video");
            this.IsShowMessage = true;
            this.ParameterSource = new CommandParameterSource(new StereoGapCommandParameter());
        }

        public override string ExecuteMessage(object? sender, CommandContext e)
        {
            var parameter = e.Parameter.Cast<StereoGapCommandParameter>();
            var gap = Config.Current.Archive.Media.StereoGap + parameter.Delta;
            gap = Math.Clamp(gap, 0.0, 100.0);
            return string.Format(TextResources.GetString("SetStereoGapCommand.Message"), gap);
        }

        public override bool CanExecute(object? sender, CommandContext e)
        {
            return Config.Current.Archive.Media.StereoMode != StereoMode.None;
        }

        public override void Execute(object? sender, CommandContext e)
        {
            var parameter = e.Parameter.Cast<StereoGapCommandParameter>();
            var gap = Config.Current.Archive.Media.StereoGap + parameter.Delta;
            Config.Current.Archive.Media.StereoGap = Math.Clamp(gap, 0.0, 100.0);
        }
    }
}
