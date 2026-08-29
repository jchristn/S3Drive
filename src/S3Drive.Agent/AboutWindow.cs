namespace S3Drive.Agent
{
    using System;
    using System.IO;
    using System.Reflection;
    using Avalonia;
    using Avalonia.Controls;
    using Avalonia.Layout;
    using Avalonia.Media;
    using Avalonia.Media.Imaging;
    using S3Drive.Core;

    /// <summary>
    /// The About window: logo, name, version, copyright, and the repository link.
    /// </summary>
    internal sealed class AboutWindow : Window
    {
        /// <summary>
        /// Initializes the About window.
        /// </summary>
        public AboutWindow()
        {
            Title = "About S3Drive";
            Width = 400;
            Height = 360;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            StackPanel panel = new StackPanel
            {
                Margin = new Thickness(24),
                Spacing = 8,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            Bitmap? logo = LoadLogo();
            if (logo != null)
            {
                Image image = new Image
                {
                    Source = logo,
                    Width = 96,
                    Height = 96,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                panel.Children.Add(image);
            }

            panel.Children.Add(new TextBlock
            {
                Text = Constants.ProductName,
                FontSize = 24,
                FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = Constants.Tagline,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = VersionText(),
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = Constants.Copyright,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            panel.Children.Add(new TextBlock
            {
                Text = Constants.RepositoryUrl,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            Button close = new Button
            {
                Content = "Close",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 0)
            };
            close.Click += (sender, args) => Close();
            panel.Children.Add(close);

            Content = panel;
        }

        private static string VersionText()
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            string number = version != null
                ? version.Major + "." + version.Minor + "." + version.Build
                : "0.1.0";
            return "v" + number + " " + Constants.ReleaseLabel;
        }

        private static Bitmap? LoadLogo()
        {
            try
            {
                Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("S3Drive.Agent.logo.png");
                if (stream == null) return null;
                using (stream)
                {
                    return new Bitmap(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
