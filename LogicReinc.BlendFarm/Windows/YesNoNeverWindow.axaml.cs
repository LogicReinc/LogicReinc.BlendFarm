using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public enum YesNoNever
    {
        Yes,
        No,
        Always,
        Never
    }
    public class YesNoNeverWindow : Window
    {
        private static Dictionary<string, bool> neverResponses = new Dictionary<string, bool>();

        public string MsgTitle { get; set; }
        public string Description { get; set; }

        public bool Response { get; set; } = false;
        public bool Never { get; set; } = false;

        public YesNoNeverWindow(string title, string desc)
        {
            MsgTitle = title;
            Description = desc;
            Init();
        }
        public YesNoNeverWindow()
        {
            Init();
        }

        public void Init()
        {
            this.DataContext = this;
            Height = 200;
            Width = 500;
            MinHeight = 200;
            MaxHeight = 200;
            MinWidth = 500;
            MaxWidth = 500;
            CanResize = false;

            this.InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }


        public void TriggerYes()
        {
            Response = true;
            this.Close();
        }

        public void TriggerNo()
        {
            Response = false;
            this.Close();
        }

        public static async Task<YesNoNever> Show(Window owner, string title, string desc)
        {
            var window = new YesNoNeverWindow(title, desc);

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);

            if(window.Never)
                return (window.Response) ? YesNoNever.Always : YesNoNever.Never;
            else
                return (window.Response) ? YesNoNever.Yes : YesNoNever.No;
        }
        public static async Task<bool> Show(Window owner, string title, string desc, string rememberID)
        {
            if (rememberID == null)
                throw new ArgumentNullException(nameof(rememberID));

            if (neverResponses.ContainsKey(rememberID))
                return neverResponses[rememberID];

            YesNoNever resp = await Show(owner, title, desc);

            switch (resp)
            {
                case YesNoNever.Always:
                    neverResponses.Add(rememberID, true);
                    return true;
                case YesNoNever.Never:
                    neverResponses.Add(rememberID, false);
                    return false;
                case YesNoNever.Yes:
                    return true;
                case YesNoNever.No:
                    return false;
                default:
                    return false;
            }
        }
    }
}
