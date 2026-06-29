using CommunityToolkit.Mvvm.ComponentModel;
using Generator.Equals;
using NeeView.Windows.Property;

namespace NeeView
{
    [Equatable(Explicit = true, IgnoreInheritedMembers = true)]
    public partial class StereoGapCommandParameter : CommandParameter
    {
        [DefaultEquality]
        private double _delta = 10.0;

        [PropertyMember]
        [PropertyRange(1.0, 100.0)]
        public double Delta
        {
            get { return _delta; }
            set { SetProperty(ref _delta, AppMath.Round(value)); }
        }
    }
}
