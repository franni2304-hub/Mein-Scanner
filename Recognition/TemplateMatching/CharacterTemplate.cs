using OpenCvSharp;

public sealed class CharacterTemplate
{
    public char Character
    {
        get;
        init;
    }

    public string FilePath
    {
        get;
        init;
    } = string.Empty;

    public Mat Image
    {
        get;
        init;
    } = new();

    public int Width => Image.Width;

    public int Height => Image.Height;
}