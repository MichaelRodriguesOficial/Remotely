using Remotely.Shared.Utilities;

namespace Remotely.Shared.Tests;

[TestClass]
public class CommandLineQuotingTests
{
    [TestMethod]
    [DataRow("abc", "abc")]
    [DataRow("", "\"\"")]
    [DataRow("Michael Rodrigues", "\"Michael Rodrigues\"")]
    [DataRow("x\" --host https://evil", "\"x\\\" --host https://evil\"")]
    [DataRow(@"C:\Path\", @"C:\Path\")]
    [DataRow(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
    [DataRow("a\\\"b", "\"a\\\\\\\"b\"")]
    public void Quote_FollowsWindowsRules(string value, string expected)
    {
        Assert.AreEqual(expected, CommandLineQuoting.Quote(value));
    }

    [TestMethod]
    public void Join_KeepsEachArgumentSeparate()
    {
        var line = CommandLineQuoting.Join(["--requester-name", "Ana \"TI\" Souza", "--org-id", "abc"]);
        Assert.AreEqual("--requester-name \"Ana \\\"TI\\\" Souza\" --org-id abc", line);
    }

    [TestMethod]
    public void Quote_RoundTripsThroughArgvParsing()
    {
        string[] values = ["simples", "com espaço", "aspa\"no meio", @"barra\final\", @"barra e espaço\ ", "\" --host x"];
        foreach (var value in values)
        {
            Assert.AreEqual(value, ParseSingle(CommandLineQuoting.Quote(value)));
        }
    }

    // Implementação das regras do CommandLineToArgvW para um único argumento.
    private static string ParseSingle(string text)
    {
        var result = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var backslashes = 0;
            while (i < text.Length && text[i] == '\\')
            {
                backslashes++;
                i++;
            }

            if (i < text.Length && text[i] == '"')
            {
                result.Append('\\', backslashes / 2);
                if (backslashes % 2 == 1)
                {
                    result.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            result.Append('\\', backslashes);
            if (i < text.Length)
            {
                if (!inQuotes && char.IsWhiteSpace(text[i]))
                {
                    throw new InvalidOperationException($"Argument split at {i}: {text}");
                }
                result.Append(text[i]);
            }
        }
        return result.ToString();
    }
}
