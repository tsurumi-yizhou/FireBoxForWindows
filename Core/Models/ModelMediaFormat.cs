namespace Core.Models;

public enum MediaFormat
{
    Image = 0,
    Video = 1,
    Audio = 2,
}

public static class MediaFormatMask
{
    public const int ImageBit = 1;
    public const int VideoBit = 2;
    public const int AudioBit = 4;

    public static List<MediaFormat> FromMask(int mask)
    {
        var list = new List<MediaFormat>();
        if ((mask & ImageBit) != 0) list.Add(MediaFormat.Image);
        if ((mask & VideoBit) != 0) list.Add(MediaFormat.Video);
        if ((mask & AudioBit) != 0) list.Add(MediaFormat.Audio);
        return list;
    }

    public static int ToMask(IEnumerable<MediaFormat>? formats)
    {
        if (formats is null) return 0;
        var mask = 0;
        foreach (var f in formats)
            mask |= f switch
            {
                MediaFormat.Image => ImageBit,
                MediaFormat.Video => VideoBit,
                MediaFormat.Audio => AudioBit,
                _ => 0,
            };
        return mask;
    }
}
