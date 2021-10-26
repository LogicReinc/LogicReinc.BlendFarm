using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using System.Threading.Tasks;

namespace LogicReinc.BlendFarm.Windows
{
    public partial class BitmapViewer : Window
    {
        public Bitmap Image { get; set; }

        public BitmapViewer() { }

        public BitmapViewer(string title, Bitmap img)
        {
            InitializeComponent();
            Title = title;
            Image image = this.Find<Image>("image");
            image.Source = img;
        }

        private void InitializeComponent()
        {
            DataContext = this;
            AvaloniaXamlLoader.Load(this);
        }


        public static async Task Show(Window owner, string title, Bitmap image)
        {
            await Show(owner, title, image, 500, 400);
        }
        public static async Task ShowOnUIThread(Window owner, string title, Bitmap image)
        {
            await Dispatcher.UIThread.InvokeAsync(() => Show(owner, title, image));
        }

        public static async Task Show(Window owner, string title, Bitmap image, int width, int height)
        {
            var window = new BitmapViewer(title, image);
            window.Width = width;
            window.Height = height;

            window.Position = new PixelPoint((int)(owner.Position.X + ((owner.Width / 2) - window.Width / 2)), (int)(owner.Position.Y + ((owner.Height / 2) - window.Height / 2)));

            await window.ShowDialog(owner);
        }
    }
}
