using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LogicReinc.BlendFarm.Meta;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace LogicReinc.BlendFarm.Windows
{
    public class AnnouncementWindow : Window, INotifyPropertyChanged
    {
        public Announcement Announcement { get; set; }
        public List<Announcement> Announcements { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public AnnouncementWindow()
        {
            //Announcements = new List<Announcement>()
            //{
            //    new Announcement()
            //    {
            //        Name = "Version 1.0.7 is out!",
            //        Date = DateTime.Now,
            //        Segments = new List<StorySegment>()
            //        {
            //            new StorySegment()
            //            {
            //                Type = "Text",
            //                Text = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vivamus bibendum nibh eu dolor dapibus fringilla vel in velit. Nunc eleifend tellus id neque tincidunt, sed tempus sapien euismod."
            //            },
            //            new StorySegment()
            //            {
            //                Type = "Image",
            //                Text = "https://raw.githubusercontent.com/LogicReinc/LogicReinc.BlendFarm/dev-1.0.7/.data/demo1.png"
            //            },
            //            new StorySegment()
            //            {
            //                Type = "Button",
            //                Text = "Open Releases|https://github.com/LogicReinc/LogicReinc.BlendFarm/releases"
            //            }
            //        }
            //    },
            //    new Announcement()
            //    {
            //        Name = "Older announcement",
            //        Date = DateTime.Now.Subtract(TimeSpan.FromDays(2)),
            //        Segments = new List<StorySegment>()
            //        {
            //            new StorySegment()
            //            {
            //                Type = "Text",
            //                Text = "Old Segment"
            //            },
            //            new StorySegment()
            //            {
            //                Type = "Image",
            //                Text = "https://raw.githubusercontent.com/LogicReinc/LogicReinc.BlendFarm/dev-1.0.7/.data/demo2.png"
            //            },
            //            new StorySegment()
            //            {
            //                Type = "Button",
            //                Text = "Open Releases|https://github.com/LogicReinc/LogicReinc.BlendFarm/releases"
            //            }
            //        }
            //    }
            //}?.OrderByDescending(x => x.Date).ToList();
            Announcements = Announcement.GetAnnouncements(Constants.AnnouncementUrl);
            Announcement = Announcements.FirstOrDefault();
            DataContext = this;

            InitializeComponent();
        }
        public AnnouncementWindow(List<Announcement> announcements)
        {
            Announcements = announcements?.OrderByDescending(x => x.Date).ToList();
            Announcement = announcements?.FirstOrDefault();
            DataContext = this;

            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            this.AttachDevTools(new Avalonia.Input.KeyGesture(Avalonia.Input.Key.K));
            int width = 600;
            int height = 700;
            MinWidth = width;
            MinHeight = height;
            Width = width;
            Height = height;
            MaxHeight = height;
            MaxWidth = width;

            Title = "Announcements";

            this.Find<ComboBox>("announcementSelection").SelectionChanged += (a, b) =>
            {
                if (b.AddedItems.Count > 0)
                {
                    Announcement = b.AddedItems[0] as Announcement;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Announcement)));
                }
            };
        }

    }
}
