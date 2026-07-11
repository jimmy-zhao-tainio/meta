using System.Text;

namespace MetaCli.Core;

public static class MetaCliStandardInput
{
    public static string ReadToEnd()
    {
        if (Console.IsInputRedirected)
        {
            Console.InputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }

        var input = Console.In.ReadToEnd();
        while (true)
        {
            if (input.StartsWith('\uFEFF'))
            {
                input = input[1..];
                continue;
            }

            if (input.StartsWith("\u00EF\u00BB\u00BF", StringComparison.Ordinal))
            {
                input = input[3..];
                continue;
            }

            if (input.StartsWith('\uFFFD'))
            {
                input = input[1..];
                continue;
            }

            return input;
        }
    }
}
