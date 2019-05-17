using System;

using GraphNotificationSample.ViewModels;

using Windows.UI.Xaml.Controls;

namespace GraphNotificationSample.Views
{
    public sealed partial class MainPage : Page
    {
        public MainViewModel ViewModel { get; } = new MainViewModel();

        public MainPage()
        {
            InitializeComponent();
        }
    }
}
