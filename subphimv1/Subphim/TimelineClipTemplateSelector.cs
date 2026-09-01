using subphimv1.Models;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace subphimv1.Selectors
{
    public class TimelineClipTemplateSelector : DataTemplateSelector
    {
        public DataTemplate VideoClipTemplate { get; set; }
        public DataTemplate AudioClipTemplate { get; set; }
        public DataTemplate ImageClipTemplate { get; set; }
        public DataTemplate SubtitleClipTemplate { get; set; }
        public DataTemplate TextClipTemplate { get; set; }
        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is TimelineClipViewModel clipVM)
            {
                switch (clipVM.ClipType)
                {
                    case TimelineClipType.Video:
                        return VideoClipTemplate;
                    case TimelineClipType.Audio:
                        return AudioClipTemplate;
                    case TimelineClipType.Image:
                        return ImageClipTemplate;
                    case TimelineClipType.Subtitle:
                        return SubtitleClipTemplate;
                    case TimelineClipType.Text:
                        return TextClipTemplate;
                }
            }
            return base.SelectTemplate(item, container);
        }
    }
}