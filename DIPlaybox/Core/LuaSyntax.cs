using System.Text;

namespace DIPlaybox.Core;

public enum LuaTokenKind { Normal, Keyword, String, Comment, Number }
public readonly record struct LuaToken(int Start, int Length, LuaTokenKind Kind);

public static class LuaSyntax
{
    public static readonly HashSet<string> Keywords = new("and break do else elseif end false for function if in local nil not or repeat return then true until while".Split(' '), StringComparer.Ordinal);
    public static List<LuaToken> Tokenize(string text)
    {
        var tokens = new List<LuaToken>(); int i = 0;
        bool LongBracket(int at, out int equals, out int openLength)
        {
            equals = 0; openLength = 0;
            if (at >= text.Length || text[at] != '[') return false;
            int j = at + 1; while (j < text.Length && text[j] == '=') { equals++; j++; }
            if (j >= text.Length || text[j] != '[') return false;
            openLength = j - at + 1; return true;
        }
        int LongEnd(int at, int equals, int length)
        {
            int end = text.IndexOf("]" + new string('=', equals) + "]", at + length, StringComparison.Ordinal);
            return end < 0 ? text.Length : end + equals + 2;
        }
        while (i < text.Length)
        {
            int start = i; LuaTokenKind kind;
            if (text[i] == '-' && i + 1 < text.Length && text[i + 1] == '-')
            {
                kind = LuaTokenKind.Comment; i += 2;
                if (LongBracket(i, out int eq, out int len)) i = LongEnd(i, eq, len);
                else { while (i < text.Length && text[i] != '\n') i++; }
            }
            else if (text[i] == '"' || text[i] == '\'')
            {
                kind = LuaTokenKind.String; char quote = text[i++];
                while (i < text.Length) { char c = text[i++]; if (c == '\\' && i < text.Length) i++; else if (c == quote) break; }
            }
            else if (LongBracket(i, out int eq, out int len)) { kind = LuaTokenKind.String; i = LongEnd(i, eq, len); }
            else if (char.IsLetter(text[i]) || text[i] == '_')
            {
                i++; while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                kind = Keywords.Contains(text[start..i]) ? LuaTokenKind.Keyword : LuaTokenKind.Normal;
            }
            else if (char.IsDigit(text[i]) || text[i] == '.' && i + 1 < text.Length && char.IsDigit(text[i + 1]) && (i == 0 || text[i - 1] != '.'))
            {
                kind = LuaTokenKind.Number; i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '.' && (i + 1 >= text.Length || text[i + 1] != '.')))
                {
                    char previous = text[i++];
                    if (previous is 'e' or 'E' or 'p' or 'P' && i < text.Length && text[i] is '+' or '-') i++;
                }
            }
            else { kind = LuaTokenKind.Normal; i++; }
            tokens.Add(new LuaToken(start, i - start, kind));
        }
        return tokens;
    }

    public static string ToRtf(string text)
    {
        var result = new StringBuilder(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Consolas;}}{\colortbl;\red215\green220\blue230;\red100\green170\blue255;\red225\green165\blue105;\red105\green170\blue110;\red95\green205\blue190;}\f0\fs20 ");
        int current = -1;
        foreach (var token in Tokenize(text))
        {
            int color = (int)token.Kind + 1;
            if (color != current) { result.Append("\\cf").Append(color).Append(' '); current = color; }
            foreach (char c in text.AsSpan(token.Start, token.Length))
            {
                if (c is '\\' or '{' or '}') result.Append('\\').Append(c);
                else if (c == '\n') result.Append("\\par\n");
                else if (c == '\t') result.Append("\\tab ");
                else if (c == '\r') { }
                else if (c <= 127) result.Append(c);
                else result.Append("\\u").Append(unchecked((short)c)).Append('?');
            }
        }
        return result.Append('}').ToString();
    }
}
