using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace Demo.Models;

public class ChatUiAttachmentItem : ObservableObject
{
    private readonly byte[] _data;
    private readonly string _mimeType;
    private BitmapImage? _previewImage;

    public ChatUiAttachmentItem(ChatAttachment attachment)
    {
        _data = attachment.Data ?? [];
        _mimeType = attachment.MimeType ?? string.Empty;
        MediaFormat = attachment.MediaFormat;
        DisplayName = string.IsNullOrWhiteSpace(attachment.FileName)
            ? $"{attachment.MediaFormat} attachment"
            : attachment.FileName;
        IsImage = attachment.MediaFormat == MediaFormat.Image
            || _mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }

    public MediaFormat MediaFormat { get; }
    public string DisplayName { get; }
    public bool IsImage { get; }
    public BitmapImage? PreviewImage
    {
        get => _previewImage;
        private set
        {
            if (SetProperty(ref _previewImage, value))
                OnPropertyChanged(nameof(PreviewVisibility));
        }
    }

    public Visibility PreviewVisibility => PreviewImage is null ? Visibility.Collapsed : Visibility.Visible;

    public async Task LoadPreviewAsync()
    {
        if (!IsImage || _data.Length == 0)
            return;

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(_data.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            PreviewImage = bitmap;
        }
        catch
        {
            PreviewImage = null;
        }
    }

}
