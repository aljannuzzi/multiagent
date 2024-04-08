namespace TBAStatReader;

internal class CircularCharArray(params char[] content)
{
    private int _pos = 0;

    public char Next()
    {
        if (++_pos >= content.Length)
        {
            _pos = 0;
        }

        return content[_pos];
    }
}
