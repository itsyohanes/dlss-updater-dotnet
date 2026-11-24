using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using dlss_updater.Services;

namespace dlss_updater
{
    // Menu bar: File (Delete all backups, Remove cached files and settings, Exit),
    // Language, and Help (Visit website, About).
    public partial class MainWindow
    {
        private const string WebsiteUrl = "https://www.dlssupdater.com";
        private const string SourceUrl = "https://github.com/itsyohanes/dlss-updater-dotnet";

        private static readonly Brush MutedBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xA4, 0xAE, 0xA2)));
        private static readonly Brush TextBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xED, 0xE8)));
        private static readonly Brush LinkBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x86, 0xCC, 0x0A)));

        private static Brush Freeze(Brush brush)
        {
            brush.Freeze();
            return brush;
        }

        // ---- Language ----

        private void BuildLanguageMenu()
        {
            foreach (LanguageOption option in Translator.Languages)
            {
                MenuItem item = new MenuItem
                {
                    Header = option.Name,
                    Tag = option.Code,
                    Icon = new Image
                    {
                        Source = option.Flag,
                        Width = 18,
                        Height = 12,
                        Stretch = Stretch.Fill
                    }
                };
                item.Click += LanguageMenuItem_Click;
                LanguageMenu.Items.Add(item);
            }

            UpdateLanguageMenuChecks();
        }

        private void UpdateLanguageMenuChecks()
        {
            foreach (object entry in LanguageMenu.Items)
            {
                MenuItem item = entry as MenuItem;
                if (item != null)
                {
                    item.IsChecked = string.Equals(
                        item.Tag as string,
                        Translator.Instance.Language,
                        StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private void LanguageMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string language = ((MenuItem)sender).Tag as string;
            if (language != null)
            {
                Translator.Instance.Language = language;
            }
        }

        // ---- File ----

        // Everything DLSS Updater keeps on this PC: the DLSS download cache,
        // the saved folders list and the language setting. Backups live next
        // to the DLSS files and are not in here.
        private static string GetAppDataFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DLSS Updater");
        }

        private void RemoveAppDataMenuItem_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                Translator.T("RemoveAppDataConfirm"),
                Translator.T("RemoveAppDataTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                string folder = GetAppDataFolder();
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    Translator.T("RemoveAppDataFailed", ex.Message),
                    Translator.T("RemoveAppDataTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Close straight away so nothing is written back to the folder.
            Application.Current.Shutdown();
        }

        // Deletes every backup in backups.json: the ones this app made, plus
        // any found while scanning a folder.
        private async void DeleteAllBackupsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            string title = WithoutAccessKey(Translator.T("DeleteAllBackups"));
            List<string> backups = await Task.Run(() => BackupIndexStore.GetExistingBackups());

            if (backups.Count == 0)
            {
                MessageBox.Show(
                    this,
                    Translator.T("NoKnownBackups"),
                    title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            int folderCount = backups
                .Select(Path.GetDirectoryName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

            MessageBoxResult confirmation = MessageBox.Show(
                this,
                Translator.T("DeleteAllBackupsConfirm", backups.Count, folderCount),
                title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            DeleteAllBackupsMenuItem.IsEnabled = false;

            try
            {
                List<ScannedFile> files = backups
                    .Select(path => new ScannedFile { Path = path })
                    .ToList();
                BackupOperationResult result = await Task.Run(() =>
                {
                    BackupOperationResult deleted = BackupArchiveService.DeleteBackups(files);
                    BackupIndexStore.GetExistingBackups(); // drops the deleted ones
                    return deleted;
                });

                // The scanned folder's Backup section may have just changed.
                string folder = _scannedFolderPath;
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    _backupFiles = await Task.Run(() => BackupScanner.FindBackupFiles(folder));
                    UpdateDlssSummary();
                    PopulateDlssDetails(folder);
                    UpdateBackupResult(_backupFiles);
                }

                ShowOperationResult(result.GetSummary(false), result.Errors, title);
            }
            finally
            {
                DeleteAllBackupsMenuItem.IsEnabled = !_updateInProgress;
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ---- Help ----

        private void VisitWebsiteMenuItem_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl(WebsiteUrl);
        }

        private static void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // No default browser; nothing else to do.
            }
        }

        // Menu text without its access key: "_About DLSS Updater" and
        // "关于 DLSS Updater(_A)" become plain titles.
        private static string WithoutAccessKey(string text)
        {
            int marker = text.IndexOf("(_", StringComparison.Ordinal);
            if (marker >= 0 && text.EndsWith(")", StringComparison.Ordinal))
            {
                text = text.Substring(0, marker);
            }

            return text.Replace("_", string.Empty).TrimEnd('…', ' ');
        }

        private static string GetAppVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            AssemblyInformationalVersionAttribute informational =
                assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

            return informational != null
                ? informational.InformationalVersion
                : assembly.GetName().Version.ToString(3);
        }

        private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
        {
            Window about = new Window
            {
                Title = WithoutAccessKey(Translator.T("AboutApp")),
                Owner = this,
                Icon = Icon,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = Background,
                FontFamily = new FontFamily("Segoe UI"),
                Language = Language
            };

            StackPanel content = new StackPanel { Margin = new Thickness(28, 24, 28, 24), Width = 380 };

            StackPanel header = new StackPanel { Orientation = Orientation.Horizontal };
            header.Children.Add(new Image
            {
                Source = Icon,
                Width = 40,
                Height = 40,
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            });
            StackPanel titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock
            {
                Text = "DLSS Updater",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = TextBrush
            });
            titles.Children.Add(new TextBlock
            {
                Text = Translator.T("AboutVersion", GetAppVersion()),
                FontSize = 13,
                Foreground = MutedBrush
            });
            header.Children.Add(titles);
            content.Children.Add(header);

            content.Children.Add(new TextBlock
            {
                Text = Translator.T("AboutDescription"),
                FontSize = 13,
                Foreground = TextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 18, 0, 0)
            });

            content.Children.Add(BuildLink("www.dlssupdater.com", WebsiteUrl, new Thickness(0, 14, 0, 0)));
            content.Children.Add(BuildLink(Translator.T("AboutSource"), SourceUrl, new Thickness(0, 4, 0, 0)));

            content.Children.Add(new TextBlock
            {
                Text = Translator.T("AboutLicence"),
                FontSize = 12,
                Foreground = MutedBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 14, 0, 0)
            });

            Button close = new Button
            {
                Content = Translator.T("Close"),
                Style = (Style)FindResource(typeof(Button)),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 22, 0, 0),
                IsDefault = true,
                IsCancel = true
            };
            close.Click += (s, args) => about.Close();
            content.Children.Add(close);

            about.Content = content;
            about.ShowDialog();
        }

        private static TextBlock BuildLink(string text, string url, Thickness margin)
        {
            Hyperlink link = new Hyperlink(new Run(text))
            {
                NavigateUri = new Uri(url),
                Foreground = LinkBrush,
                TextDecorations = null,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            link.RequestNavigate += (s, e) =>
            {
                OpenUrl(e.Uri.AbsoluteUri);
                e.Handled = true;
            };
            link.MouseEnter += (s, e) => link.TextDecorations = TextDecorations.Underline;
            link.MouseLeave += (s, e) => link.TextDecorations = null;

            TextBlock block = new TextBlock { FontSize = 13, Margin = margin };
            block.Inlines.Add(link);
            return block;
        }
    }
}
