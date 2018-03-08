using System;
using Urho;
using Urho.Forms;
using Xamarin.Forms;

namespace FormsSample
{
    public class App : Xamarin.Forms.Application
    {
        public App()
        {
            MainPage = new NavigationPage(new StartPage { });
        }
    }

    public class StartPage : ContentPage
    {
        public StartPage()
        {
            var b = new Button { Text = "Launch sample" };
            b.Clicked += (sender, e) => Navigation.PushAsync(new UrhoPage());
            Content = new StackLayout { Children = { b }, VerticalOptions = LayoutOptions.Center };
        }
    }


    public class UrhoPage : ContentPage
    {
        UrhoSurface urhoSurface;
        CharacterDemo urhoApp;
        Slider selectedBarSlider;

        public UrhoPage()
        {
            var restartBtn = new Button { Text = "Restart" };
            restartBtn.Clicked += (sender, e) => StartUrhoApp();

            urhoSurface = new UrhoSurface();
            urhoSurface.VerticalOptions = LayoutOptions.FillAndExpand;

            Slider rotationSlider = new Slider(0, 500, 250);

            selectedBarSlider = new Slider(0, 5, 2.5);
            selectedBarSlider.ValueChanged += OnValuesSliderValueChanged;

            Title = " UrhoSharp + Xamarin.Forms";
            Content = new StackLayout
            {
                Padding = new Thickness(12, 12, 12, 40),
                VerticalOptions = LayoutOptions.FillAndExpand,
                Children = {
                    urhoSurface,
                    restartBtn,
                    new Label { Text = "ROTATION::" },
                    rotationSlider,
                    new Label { Text = "SELECTED VALUE:" },
                    selectedBarSlider,
                }
            };
        }

        protected override void OnDisappearing()
        {
            UrhoSurface.OnDestroy();
            base.OnDisappearing();
        }

        void OnValuesSliderValueChanged(object sender, ValueChangedEventArgs e)
        {
        }

        private void OnBarSelection(Bar bar)
        {
            //reset value
            selectedBarSlider.ValueChanged -= OnValuesSliderValueChanged;
            selectedBarSlider.Value = bar.Value;
            selectedBarSlider.ValueChanged += OnValuesSliderValueChanged;
        }

        protected override async void OnAppearing()
        {
            StartUrhoApp();
        }

        async void StartUrhoApp()
        {
            urhoApp = await urhoSurface.Show<CharacterDemo>(new ApplicationOptions(GetPaths()) { Orientation = ApplicationOptions.OrientationType.LandscapeAndPortrait });
        }

        string GetPaths()
        {
            if (Device.RuntimePlatform == Device.Android)
                return "Data";
            else if (Device.RuntimePlatform == Device.UWP || Device.RuntimePlatform == Device.WinPhone || Device.RuntimePlatform == Device.WinRT)
                return "Assets/Data";
            else if (Device.RuntimePlatform == Device.iOS)
                return "Data";
            else return string.Empty;
        }
    }
}
