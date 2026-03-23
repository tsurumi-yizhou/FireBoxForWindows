namespace Core.Models;

public enum ModelMediaFormat
{
    Image = 0,
    Video = 1,
    Audio = 2,
}

public static class ModelMediaFormatMask
{
    public const int ImageBit = 1;
    public const int VideoBit = 2;
    public const int AudioBit = 4;

    public static List<ModelMediaFormat> FromMask(int mask)
    {
        var list = new List<ModelMediaFormat>();
        if ((mask & ImageBit) != 0) list.Add(ModelMediaFormat.Image);
        if ((mask & VideoBit) != 0) list.Add(ModelMediaFormat.Video);
        if ((mask & AudioBit) != 0) list.Add(ModelMediaFormat.Audio);
        return list;
    }

    public static int ToMask(IEnumerable<ModelMediaFormat>? formats)
    {
        if (formats is null) return 0;
        var mask = 0;
        foreach (var f in formats)
            mask |= f switch
            {
                ModelMediaFormat.Image => ImageBit,
                ModelMediaFormat.Video => VideoBit,
                ModelMediaFormat.Audio => AudioBit,
                _ => 0,
            };
        return mask;
    }
}
