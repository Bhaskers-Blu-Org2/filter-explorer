﻿/*
 * Copyright (c) 2014 Nokia Corporation. All rights reserved.
 *
 * Nokia and Nokia Connecting People are registered trademarks of Nokia Corporation.
 * Other product and company names mentioned herein may be trademarks
 * or trade names of their respective owners.
 *
 * See the license text file for license information.
 */

using FilterExplorer.Commands;
using FilterExplorer.Filters;
using FilterExplorer.Models;
using FilterExplorer.Utilities;
using FilterExplorer.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace FilterExplorer.ViewModels
{
    public class StreamPageViewModel : ViewModelBase
    {
        private string _folderName = null;
        private Random _random = new Random(DateTime.Now.Millisecond + 1);
        private HighlightStrategy _highlightStrategy = null;

        public IDelegateCommand GoBackCommand { get; private set; }
        public IDelegateCommand SelectPhotoCommand { get; private set; }
        public IDelegateCommand OpenPhotoCommand { get; private set; }
        public IDelegateCommand OpenFolderCommand { get; private set; }
#if !WINDOWS_PHONE_APP
        public IDelegateCommand CapturePhotoCommand { get; private set; }
#endif
        public IDelegateCommand RefreshPhotosCommand { get; private set; }
        public IDelegateCommand RefreshSomePhotosCommand { get; private set; }
        public IDelegateCommand ShowAboutCommand { get; private set; }
        public IDelegateCommand ChangeHighlightStrategyCommand { get; private set; }

        public ObservableCollection<ThumbnailViewModel> Thumbnails { get; private set; }

        public string FolderName
        {
            get
            {
                return _folderName;
            }

            private set
            {
                if (_folderName != value)
                {
                    _folderName = value;

                    Notify("FolderName");
                }
            }
        }

        public HighlightStrategy HighlightStrategy
        {
            get
            {
                return _highlightStrategy;
            }

            private set
            {
                if (_highlightStrategy != value)
                {
                    _highlightStrategy = value;

                    Notify("HighlightStrategy");
                }
            }
        }

        public StreamPageViewModel(HighlightStrategy highlightStrategy)
        {
            HighlightStrategy = highlightStrategy;

            Thumbnails = new ObservableCollection<ThumbnailViewModel>();

            GoBackCommand = CommandFactory.CreateGoBackCommand();

            SelectPhotoCommand = new DelegateCommand((parameter) =>
                {
                    var viewModel = (ThumbnailViewModel)parameter;

                    try
                    {
                        var copy = new FilteredPhotoModel(viewModel.Model);

                        SessionModel.Instance.Photo = copy;

                        var frame = (Frame)Window.Current.Content;
                        frame.Navigate(typeof(PhotoPage));
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("SelectPhotoCommand failed: " + ex.Message + '\n' + ex.StackTrace);
                    }
                });

#if WINDOWS_PHONE_APP
            OpenPhotoCommand = new DelegateCommand(
                (parameter) =>
                {
                    StartOpenPhotoFile();
                });
#else
            OpenPhotoCommand = new DelegateCommand(
                async (parameter) =>
                {
                    var file = await PickPhotoFileAsync();

                    if (file != null)
                    {
                        SessionModel.Instance.Photo = new FilteredPhotoModel(file);

                        var frame = (Frame)Window.Current.Content;
                        frame.Navigate(typeof(PhotoPage));
                    }
                });
#endif

#if WINDOWS_PHONE_APP
            OpenFolderCommand = new DelegateCommand(
                (parameter) =>
                {
                    StartOpenPhotoFolder();
                });
#else
            OpenFolderCommand = new DelegateCommand(
                async (parameter) =>
                {
                    var folder = await PickPhotoFolderAsync();

                    if (folder != null && (SessionModel.Instance.Folder == null || folder.Path != SessionModel.Instance.Folder.Path))
                    {
                        SessionModel.Instance.Folder = folder;

                        await Refresh();
                    }
                });
#endif

#if !WINDOWS_PHONE_APP
            CapturePhotoCommand = new DelegateCommand(
                async (parameter) =>
                {
                    var file = await CapturePhotoFileAsync();

                    if (file != null)
                    {
                        SessionModel.Instance.Photo = new FilteredPhotoModel(file);

                        var frame = (Frame)Window.Current.Content;
                        frame.Navigate(typeof(PhotoPage));
                    }
                });
#endif

            ShowAboutCommand = new DelegateCommand((parameter) =>
                {
                    var frame = (Frame)Window.Current.Content;
                    frame.Navigate(typeof(AboutPage));
                });

            RefreshPhotosCommand = new DelegateCommand(
                async (parameter) =>
                    {
                        await Refresh();
                    },
                () =>
                    {
                        return !Processing;
                    });

            RefreshSomePhotosCommand = new DelegateCommand(
                (parameter) =>
                {
                    RefreshSome();
                },
                () =>
                {
                    return !Processing;
                });

            ChangeHighlightStrategyCommand = new DelegateCommand(
                async (parameter) =>
                {
                    HighlightStrategy = parameter as HighlightStrategy;

                    await Refresh();
                },
                () =>
                {
                    return !Processing;
                });
        }

        public override async Task<bool> InitializeAsync()
        {
            if (!IsInitialized)
            {
                await Refresh();

                IsInitialized = true;
            }

            return IsInitialized;
        }

        private async Task Refresh()
        {
            Processing = true;
            FolderName = null;

            RefreshPhotosCommand.RaiseCanExecuteChanged();

            Thumbnails.Clear();

            var filters = FilterFactory.CreateStreamFilters();
            var maxItems = (uint)_highlightStrategy.BatchSize * 5;

            if (SessionModel.Instance.Folder != null)
            {
                FolderName = SessionModel.Instance.Folder.DisplayName;

                var models = await GetPhotosFromFolderAsync(SessionModel.Instance.Folder, maxItems);

                for (var i = 0; i < models.Count; i++)
                {
                    var k = i % (_highlightStrategy.BatchSize);
                    var highlight = _highlightStrategy.HighlightedIndexes.Contains(k);

                    Thumbnails.Add(new ThumbnailViewModel(models[i], TakeRandomFilter(filters), highlight));
                }
            }
            else
            {
                var models = await GetPhotosFromFolderAsync(Windows.Storage.KnownFolders.CameraRoll, maxItems);

                if (models.Count > 0)
                {
                    FolderName = Windows.Storage.KnownFolders.CameraRoll.DisplayName;
                }
                else
                {
                    models = await GetPhotosFromFolderAsync(Windows.Storage.KnownFolders.PicturesLibrary, maxItems);

                    FolderName = Windows.Storage.KnownFolders.PicturesLibrary.DisplayName;
                }

                for (var i = 0; i < models.Count; i++)
                {
                    var k = i % (_highlightStrategy.BatchSize);
                    var highlight = _highlightStrategy.HighlightedIndexes.Contains(k);

                    Thumbnails.Add(new ThumbnailViewModel(models[i], TakeRandomFilter(filters), highlight));
                }
            }

            Processing = false;

            RefreshPhotosCommand.RaiseCanExecuteChanged();
        }

        private void RefreshSome()
        {
            RefreshSomePhotosCommand.RaiseCanExecuteChanged();

            var filters = FilterFactory.CreateStreamFilters();

            for (var i = 0; i < Thumbnails.Count; i += _highlightStrategy.BatchSize)
            {
                var index = _random.Next(i, Math.Min(i + _highlightStrategy.BatchSize, Thumbnails.Count - 1));

                Thumbnails[index].Filter = TakeRandomFilter(filters);
            }

            RefreshSomePhotosCommand.RaiseCanExecuteChanged();
        }

        private Filter TakeRandomFilter(ObservableList<Filter> filters)
        {
            var index = _random.Next(0, filters.Count - 1);
            var filter = filters[index];

            return filter;
        }

#if WINDOWS_PHONE_APP
        private void StartOpenPhotoFile()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.ContinuationData["Operation"] = "OpenPhotoFile";
            picker.PickSingleFileAndContinue();

            App.ContinuationEventArgsChanged += App_ContinuationEventArgsChanged;
        }

        private void StartOpenPhotoFolder()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.ViewMode = PickerViewMode.Thumbnail;
            picker.ContinuationData["Operation"] = "OpenPhotoFolder";
            picker.PickFolderAndContinue();

            App.ContinuationEventArgsChanged += App_ContinuationEventArgsChanged;
        }

        private async void App_ContinuationEventArgsChanged(object sender, IContinuationActivatedEventArgs e)
        {
            App.ContinuationEventArgsChanged -= App_ContinuationEventArgsChanged;

            var fileArgs = e as FileOpenPickerContinuationEventArgs;
            var folderArgs = e as FolderPickerContinuationEventArgs;

            if (fileArgs != null && (fileArgs.ContinuationData["Operation"] as string) == "OpenPhotoFile" && fileArgs.Files != null && fileArgs.Files.Count > 0)
            {
                var file = fileArgs.Files[0];

                SessionModel.Instance.Photo = new FilteredPhotoModel(file);

                var frame = (Frame)Window.Current.Content;
                frame.Navigate(typeof(PhotoPage));
            }
            else if (folderArgs != null && (folderArgs.ContinuationData["Operation"] as string) == "OpenPhotoFolder" && folderArgs.Folder != null)
            {
                var folder = folderArgs.Folder;

                if (folder != null && (SessionModel.Instance.Folder == null || folder.Path != SessionModel.Instance.Folder.Path))
                {
                    SessionModel.Instance.Folder = folder;

                    await Refresh();
                }
            }
        }
#else
        public static async Task<StorageFile> PickPhotoFileAsync()
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add("*");
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.ViewMode = PickerViewMode.Thumbnail;

            return await picker.PickSingleFileAsync();
        }

        public static async Task<StorageFolder> PickPhotoFolderAsync()
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.ViewMode = PickerViewMode.Thumbnail;

            return await picker.PickSingleFolderAsync();
        }

        public static async Task<StorageFile> CapturePhotoFileAsync()
        {
            var captureUi = new Windows.Media.Capture.CameraCaptureUI();
            captureUi.PhotoSettings.Format = Windows.Media.Capture.CameraCaptureUIPhotoFormat.Jpeg;

            var file = await captureUi.CaptureFileAsync(Windows.Media.Capture.CameraCaptureUIMode.Photo);

            if (file != null)
            {
                var filename = Application.Current.Resources["PhotoCaptureTemporaryFilename"] as string;
                var folder = ApplicationData.Current.TemporaryFolder;
                var temporaryFile = await folder.CreateFileAsync(filename, Windows.Storage.CreationCollisionOption.ReplaceExisting);

                CachedFileManager.DeferUpdates(temporaryFile);
                await file.CopyAndReplaceAsync(temporaryFile);
                await CachedFileManager.CompleteUpdatesAsync(temporaryFile);

                file = temporaryFile;
            }

            return file;
        }
#endif

        private static async Task<List<FilteredPhotoModel>> GetPhotosFromFolderAsync(StorageFolder folder, uint amount)
        {
            var list = new List<FilteredPhotoModel>();
            var files = await folder.GetFilesAsync();

            foreach (var file in files)
            {
                if (list.Count == amount)
                {
                    break;
                }

                var properties = await file.GetBasicPropertiesAsync();

                if (properties.Size > 0 && file.ContentType == "image/jpeg" && !file.Name.Contains("highres"))
                {
                    list.Add(new FilteredPhotoModel(file));
                }
            }

            return list;
        }
    }
}
