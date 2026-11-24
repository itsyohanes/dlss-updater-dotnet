using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using dlss_updater.Services;

namespace dlss_updater
{
    // Update check after a scan, the "Your games" list and the startup banner.
    public partial class MainWindow
    {
        private enum UpdateCheckState
        {
            None,
            Checking,
            Done,
            Failed
        }

        private enum SavedGameStatus
        {
            Unknown,
            Missing,
            UpdateAvailable,
            UpToDate
        }

        private static readonly SolidColorBrush RowSeparatorBrush =
            new SolidColorBrush(Color.FromRgb(44, 50, 44));

        private static readonly string[] DlssFileOrder =
        {
            "nvngx_dlss.dll",
            "nvngx_dlssd.dll",
            "nvngx_dlssg.dll"
        };

        // Latest versions from the manifest (file name -> version), or null
        // until the first successful fetch.
        private Dictionary<string, string> _latestVersions;
        private UpdateCheckState _folderCheckState;
        private int _folderCheckGeneration;
        private int _folderUpdatableCount;
        private bool _folderAllUpToDate;

        // Folder added to Your folders by the last Update, shown in the
        // "Just added" notice until it's removed or another folder is added.
        private string _justAddedFolder;

        // Set once the user opens the list: the notice goes away for good,
        // but the folder's row keeps its "Just added" badge.
        private bool _justAddedNoticeSeen;
        private Version _folderTargetVersion;
        private List<KeyValuePair<string, string>> _bannerNewVersions =
            new List<KeyValuePair<string, string>>();

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshSavedGames();

            Dictionary<string, string> versions;

            try
            {
                versions = await DlssReleaseClient.GetLatestVersionsAsync();
            }
            catch
            {
                // Offline or GitHub unavailable: no banner, no statuses.
                return;
            }

            _latestVersions = versions;

            if (!string.IsNullOrWhiteSpace(_scannedFolderPath))
            {
                ApplyFolderUpdateCheck();
            }

            RefreshSavedGames();
            ShowStartupBannerIfNeeded(versions);
        }

        // ---- Update check for the scanned folder ----

        private void ResetFolderUpdateCheck()
        {
            _folderCheckGeneration++;
            _folderCheckState = UpdateCheckState.None;
            _folderUpdatableCount = 0;
            _folderAllUpToDate = false;
            _folderTargetVersion = null;
        }

        private void OnFolderScanned(string folder, int scanGeneration)
        {
            List<ScannedFile> files = GetScannedFiles();

            if (files.Count == 0)
            {
                return;
            }

            SavedGamesStore.RecordScan(folder, files, false);
            MarkVersionsSeen();
            RefreshSavedGames();
            CheckFolderForUpdatesAsync();
        }

        private async void CheckFolderForUpdatesAsync()
        {
            int checkGeneration = ++_folderCheckGeneration;
            _folderCheckState = UpdateCheckState.Checking;
            UpdateDlssSummary();

            Dictionary<string, string> versions;

            try
            {
                versions = await DlssReleaseClient.GetLatestVersionsAsync();
            }
            catch
            {
                versions = null;
            }

            if (checkGeneration != _folderCheckGeneration)
            {
                return;
            }

            if (versions == null)
            {
                _folderCheckState = UpdateCheckState.Failed;
                UpdateDlssSummary();
                ApplyUpdateButtonState();
                return;
            }

            _latestVersions = versions;
            ApplyFolderUpdateCheck();
            RefreshSavedGames();
        }

        private void ApplyFolderUpdateCheck()
        {
            EvaluateFolderUpdates();
            UpdateDlssSummary();
            ApplyUpdateButtonState();

            if (DlssDetailsPage.Visibility == Visibility.Visible)
            {
                PopulateDlssDetails(_scannedFolderPath);
            }
        }

        private void EvaluateFolderUpdates()
        {
            if (_latestVersions == null)
            {
                return;
            }

            int updatable = 0;
            bool allUpToDate = true;
            HashSet<Version> targets = new HashSet<Version>();

            foreach (ScannedFile file in GetScannedFiles())
            {
                Version target;
                DlssFileStatus status = DlssVersionRules.Evaluate(
                    file.Path,
                    file.Version,
                    _latestVersions,
                    out target);

                if (status == DlssFileStatus.UpdateAvailable)
                {
                    updatable++;
                    targets.Add(target);
                }

                if (status != DlssFileStatus.UpToDate)
                {
                    allUpToDate = false;
                }
            }

            _folderUpdatableCount = updatable;
            _folderAllUpToDate = allUpToDate;
            _folderTargetVersion = targets.Count == 1 ? targets.First() : null;
            _folderCheckState = UpdateCheckState.Done;
        }

        private string GetUpdateCheckSuffix()
        {
            switch (_folderCheckState)
            {
                case UpdateCheckState.Checking:
                    return Translator.T("CheckingForUpdatesSuffix");

                case UpdateCheckState.Done:
                    if (_folderUpdatableCount == 0)
                    {
                        return _folderAllUpToDate
                            ? Translator.T("AllUpToDateSuffix")
                            : string.Empty;
                    }

                    string suffix = _folderUpdatableCount == 1
                        ? Translator.T("UpdateAvailableOne")
                        : Translator.T("UpdatesAvailableMany", _folderUpdatableCount);

                    if (_folderTargetVersion != null)
                    {
                        suffix += " (" + _folderTargetVersion + ")";
                    }

                    return suffix;

                default:
                    return string.Empty;
            }
        }

        private void ApplyUpdateButtonState()
        {
            if (_updateInProgress)
            {
                UpdateButton.IsEnabled = false;
                return;
            }

            bool hasFiles = GetScannedFiles().Count > 0;
            bool checkDone = _folderCheckState == UpdateCheckState.Done;
            bool nothingToUpdate = checkDone && _folderUpdatableCount == 0;
            bool enabled = hasFiles && !nothingToUpdate;
            int count = _folderUpdatableCount;

            UpdateButton.IsEnabled = enabled;
            UpdateButton.Opacity = enabled ? 1.0 : 0.5;
            SetLocalized(
                UpdateButton,
                () => UpdateButton.Content = checkDone && count > 0
                    ? Translator.T("UpdateCount", count)
                    : Translator.T("Update"));
        }

        private string GetLatestColumnText(ScannedFile file, out Brush brush)
        {
            brush = NeutralStatusBrush;

            if (_folderCheckState != UpdateCheckState.Done || _latestVersions == null)
            {
                return string.Empty;
            }

            Version target;
            DlssFileStatus status = DlssVersionRules.Evaluate(
                file.Path,
                file.Version,
                _latestVersions,
                out target);

            switch (status)
            {
                case DlssFileStatus.UpdateAvailable:
                    brush = WarningStatusBrush;
                    return "→ " + target;

                case DlssFileStatus.UpToDate:
                    brush = SuccessStatusBrush;
                    return "✓ " + Translator.T("UpToDate");

                case DlssFileStatus.Incompatible:
                    return Translator.T("NotCompatible");

                default:
                    return string.Empty;
            }
        }

        private List<ScannedFile> GetScannedFiles()
        {
            List<ScannedFile> files = new List<ScannedFile>();
            files.AddRange(_dlssFiles);
            files.AddRange(_dlssdFiles);
            files.AddRange(_dlssgFiles);
            return files;
        }

        // ---- Your games ----

        private void RefreshSavedGames()
        {
            SavedGamesPanel.Children.Clear();
            IList<SavedGame> games = SavedGamesStore.Games;
            int updatableGames = 0;

            for (int i = 0; i < games.Count; i++)
            {
                Version target;
                SavedGameStatus status = GetSavedGameStatus(games[i], out target);

                if (status == SavedGameStatus.UpdateAvailable)
                {
                    updatableGames++;
                }

                SavedGamesPanel.Children.Add(BuildSavedGameRow(games[i], status, target));
            }

            SavedGamesSection.Visibility = games.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            UpdateAllButton.Visibility = updatableGames > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            UpdateAllButton.Content = Translator.T("UpdateAllCount", updatableGames);

            // Header reads "Your folders (3) · 2 can be updated" so the
            // collapsed list still says whether anything needs attention.
            SavedFoldersTitle.Text = Translator.T("YourFolders") + " (" + games.Count + ")";
            SavedFoldersSummary.Text = updatableGames > 0
                ? " · " + Translator.T("FoldersCanUpdate", updatableGames)
                : string.Empty;
            ApplySavedFoldersExpanded();
            ApplyJustAddedNotice();
        }

        private void ApplyJustAddedNotice()
        {
            if (_justAddedFolder != null && SavedGamesStore.Find(_justAddedFolder) == null)
            {
                _justAddedFolder = null;
            }

            bool showNotice = _justAddedFolder != null &&
                !_justAddedNoticeSeen &&
                !SavedGamesStore.FoldersExpanded;
            JustAddedNotice.Visibility = showNotice
                ? Visibility.Visible
                : Visibility.Collapsed;
            JustAddedFolderText.Text = _justAddedFolder ?? string.Empty;
            JustAddedFolderText.ToolTip = _justAddedFolder;
        }

        private void JustAddedRemoveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_justAddedFolder == null)
            {
                return;
            }

            SavedGamesStore.Remove(_justAddedFolder);
            _justAddedFolder = null;
            RefreshSavedGames();
        }

        private void ApplySavedFoldersExpanded()
        {
            bool expanded = SavedGamesStore.FoldersExpanded;
            SavedFoldersList.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            SavedFoldersExpandButton.Content = expanded ? "▲" : "▼";
            SavedFoldersExpandButton.ToolTip = Translator.T(expanded ? "CollapseList" : "ExpandList");
        }

        private void ToggleSavedFolders()
        {
            SavedGamesStore.FoldersExpanded = !SavedGamesStore.FoldersExpanded;
            if (SavedGamesStore.FoldersExpanded)
            {
                _justAddedNoticeSeen = true;
            }

            ApplySavedFoldersExpanded();
            ApplyJustAddedNotice();
        }

        private void SavedFoldersExpandButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSavedFolders();
        }

        private void SavedFoldersHeader_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Buttons in the header handle their own clicks.
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null && !ReferenceEquals(source, sender))
            {
                if (source is ButtonBase)
                {
                    return;
                }

                source = source is Visual || source is System.Windows.Media.Media3D.Visual3D
                    ? VisualTreeHelper.GetParent(source)
                    : LogicalTreeHelper.GetParent(source);
            }

            ToggleSavedFolders();
        }

        private SavedGameStatus GetSavedGameStatus(SavedGame game, out Version target)
        {
            target = null;

            if (!Directory.Exists(game.Folder))
            {
                return SavedGameStatus.Missing;
            }

            if (_latestVersions == null || game.Files.Count == 0)
            {
                return SavedGameStatus.Unknown;
            }

            bool anyUpdate = false;
            bool allUpToDate = true;

            foreach (SavedGameFile file in game.Files)
            {
                Version fileTarget;
                DlssFileStatus status = DlssVersionRules.Evaluate(
                    file.Path,
                    file.Version,
                    _latestVersions,
                    out fileTarget);

                if (status == DlssFileStatus.UpdateAvailable)
                {
                    anyUpdate = true;
                    if (target == null || fileTarget > target)
                    {
                        target = fileTarget;
                    }
                }

                if (status != DlssFileStatus.UpToDate)
                {
                    allUpToDate = false;
                }
            }

            if (anyUpdate)
            {
                return SavedGameStatus.UpdateAvailable;
            }

            return allUpToDate ? SavedGameStatus.UpToDate : SavedGameStatus.Unknown;
        }

        private UIElement BuildSavedGameRow(
            SavedGame game,
            SavedGameStatus status,
            Version target)
        {
            Grid row = new Grid
            {
                Margin = new Thickness(18, 9, 10, 9)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel names = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = game.Folder
            };
            TextBlock nameText = new TextBlock
            {
                Text = game.Name,
                FontSize = 14,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            if (string.Equals(game.Folder, _justAddedFolder, StringComparison.OrdinalIgnoreCase))
            {
                StackPanel nameLine = new StackPanel { Orientation = Orientation.Horizontal };
                nameLine.Children.Add(nameText);
                nameLine.Children.Add(BuildJustAddedBadge());
                names.Children.Add(nameLine);
            }
            else
            {
                names.Children.Add(nameText);
            }
            names.Children.Add(new TextBlock
            {
                Text = GetSavedGameDetail(game),
                FontSize = 12,
                Foreground = NeutralStatusBrush,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            row.Children.Add(names);

            TextBlock statusText = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = NeutralStatusBrush
            };

            switch (status)
            {
                case SavedGameStatus.UpdateAvailable:
                    statusText.Text = Translator.T("VersionAvailable", target);
                    statusText.Foreground = WarningStatusBrush;
                    break;

                case SavedGameStatus.UpToDate:
                    statusText.Text = "✓ " + Translator.T("UpToDate");
                    statusText.Foreground = SuccessStatusBrush;
                    break;

                case SavedGameStatus.Missing:
                    statusText.Text = Translator.T("FolderNotFound");
                    break;
            }

            Grid.SetColumn(statusText, 1);
            row.Children.Add(statusText);

            string folder = game.Folder;

            // Greyed out when there's nothing to do (already current) or
            // nothing to update (folder gone).
            Button updateButton = new Button
            {
                Content = Translator.T("Update"),
                Height = 30,
                MinWidth = 70,
                FontSize = 13,
                Margin = new Thickness(12, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = status != SavedGameStatus.Missing &&
                    status != SavedGameStatus.UpToDate
            };
            updateButton.Click += async (sender, args) => await UpdateSavedFolderAsync(folder);
            Grid.SetColumn(updateButton, 2);
            row.Children.Add(updateButton);

            Button removeButton = new Button
            {
                Style = (Style)FindResource("ExpandButton"),
                Content = "\uE711",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Translator.T("RemoveFromList")
            };
            removeButton.Click += (sender, args) =>
            {
                SavedGamesStore.Remove(folder);
                RefreshSavedGames();
            };
            Grid.SetColumn(removeButton, 3);
            row.Children.Add(removeButton);

            return new Border
            {
                Child = row,
                BorderBrush = RowSeparatorBrush,
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
        }

        // Same pill as the "Just added" notice under the Your folders header.
        private static Border BuildJustAddedBadge()
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x2A, 0x14)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x4E, 0x7A, 0x10)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(8, 1, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = Translator.T("JustAdded"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xCC, 0x0A))
                }
            };
        }

        // "DLSS 310.2.1 · D:\Games\Cyberpunk 2077": the oldest version of the
        // first DLSS type found there, then the folder (trimmed if too long).
        private static string GetSavedGameDetail(SavedGame game)
        {
            foreach (string fileName in DlssFileOrder)
            {
                Version oldest = null;

                foreach (SavedGameFile file in game.Files)
                {
                    Version version;
                    if (string.Equals(Path.GetFileName(file.Path), fileName, StringComparison.OrdinalIgnoreCase) &&
                        Version.TryParse(file.Version, out version) &&
                        (oldest == null || version < oldest))
                    {
                        oldest = version;
                    }
                }

                if (oldest != null)
                {
                    return GetTypeLabel(fileName) + " " + oldest + " · " + game.Folder;
                }
            }

            return game.Folder;
        }

        private static string GetTypeLabel(string fileName)
        {
            switch (fileName.ToLowerInvariant())
            {
                case "nvngx_dlssd.dll":
                    return "DLSS-D";
                case "nvngx_dlssg.dll":
                    return "DLSS-G";
                default:
                    return "DLSS";
            }
        }

        private async Task UpdateSavedFolderAsync(string folder)
        {
            if (_updateInProgress || !Directory.Exists(folder))
            {
                return;
            }

            if (DlssDetailsPage.Visibility == Visibility.Visible)
            {
                DlssDetailsPage.Visibility = Visibility.Collapsed;
                MainPage.Visibility = Visibility.Visible;
            }

            CancelPendingFolderPathScan();
            await RunSingleUpdateAsync(folder);
        }

        private async void UpdateAllButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateInProgress)
            {
                return;
            }

            List<SavedGame> games = SavedGamesStore.Games
                .Where(game =>
                {
                    Version target;
                    return GetSavedGameStatus(game, out target) == SavedGameStatus.UpdateAvailable;
                })
                .ToList();

            if (games.Count == 0)
            {
                return;
            }

            StartupBanner.Visibility = Visibility.Collapsed;
            MarkVersionsSeen();

            SetUpdateInProgress(true);
            UpdateButton.IsEnabled = false;
            SetButtonText(UpdateButton, "Updating");
            ShowUpdateProgressBar();

            int updatedFiles = 0;
            int skippedFiles = 0;
            List<string> errors = new List<string>();
            List<string> skipped = new List<string>();

            try
            {
                for (int i = 0; i < games.Count; i++)
                {
                    SavedGame game = games[i];
                    int gameIndex = i;
                    IProgress<double> fileProgress = new Progress<double>(fraction =>
                        SetUpdateProgress((gameIndex + fraction) / games.Count));

                    try
                    {
                        DlssUpdateResult result = await UpdateFolderAsync(game.Folder, fileProgress);
                        updatedFiles += result.UpdatedFiles;
                        skippedFiles += result.SkippedFiles.Count;
                        errors.AddRange(result.Errors.Select(error => game.Name + ": " + error));
                        skipped.AddRange(result.SkippedFiles.Select(detail => game.Name + ": " + detail));
                    }
                    catch (Exception ex)
                    {
                        errors.Add(game.Name + ": " + Translator.T("UpdateFailedMessage") + " " + ex.Message);
                    }
                }
            }
            finally
            {
                HideUpdateProgressBar();
                SetUpdateInProgress(false);
                ApplyUpdateButtonState();
                RefreshSavedGames();
            }

            string summary = Translator.T("UpdateAllSummary", updatedFiles, games.Count);
            if (skippedFiles > 0)
            {
                summary += Translator.T("UpdateSkippedSummary", skippedFiles);
            }

            ShowOperationResult(summary, errors, "DLSS Updater", skipped);
        }

        // ---- Startup banner ----

        private void ShowStartupBannerIfNeeded(Dictionary<string, string> versions)
        {
            IDictionary<string, string> seen = SavedGamesStore.SeenVersions;

            if (seen.Count == 0)
            {
                // First run: remember what's current, nothing to announce.
                SavedGamesStore.SetSeenVersions(versions);
                return;
            }

            List<KeyValuePair<string, string>> newer = new List<KeyValuePair<string, string>>();

            foreach (string fileName in DlssFileOrder)
            {
                string latest;
                Version latestVersion;
                if (!versions.TryGetValue(fileName, out latest) ||
                    !Version.TryParse(latest, out latestVersion))
                {
                    continue;
                }

                string seenText;
                Version seenVersion;
                if (!seen.TryGetValue(fileName, out seenText) ||
                    !Version.TryParse(seenText, out seenVersion) ||
                    latestVersion > seenVersion)
                {
                    newer.Add(new KeyValuePair<string, string>(fileName, latest));
                }
            }

            if (newer.Count == 0)
            {
                return;
            }

            _bannerNewVersions = newer;
            StartupBanner.Visibility = Visibility.Visible;
            UpdateBannerContent();
        }

        private void UpdateBannerContent()
        {
            if (StartupBanner.Visibility != Visibility.Visible || _bannerNewVersions.Count == 0)
            {
                return;
            }

            // Just the new files, e.g. "DLSS, DLSS-D, DLSS-G 310.9.16".
            StartupBannerText.Text = FormatNewVersions(_bannerNewVersions);
        }

        // "DLSS, DLSS-D, DLSS-G 310.9.16" when they share a version,
        // otherwise "DLSS 310.9.16 · DLSS-G 310.5.0".
        private static string FormatNewVersions(List<KeyValuePair<string, string>> versions)
        {
            bool sameVersion = versions
                .Select(item => item.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() == 1;

            if (sameVersion)
            {
                return string.Join(", ", versions.Select(item => GetTypeLabel(item.Key))) +
                    " " + versions[0].Value;
            }

            return string.Join(
                " · ",
                versions.Select(item => GetTypeLabel(item.Key) + " " + item.Value));
        }

        private void DismissBannerButton_Click(object sender, RoutedEventArgs e)
        {
            StartupBanner.Visibility = Visibility.Collapsed;
            MarkVersionsSeen();
        }

        private void MarkVersionsSeen()
        {
            if (_latestVersions != null)
            {
                SavedGamesStore.SetSeenVersions(_latestVersions);
            }
        }
    }
}
