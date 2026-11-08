using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Small dependency-free JSON reader used by the level loader.
/// Returns Dictionary&lt;string, object&gt;, List&lt;object&gt;, string,
/// double, bool or null for the parsed document.
/// </summary>
public static class MiniJson
{
    public static object Parse(string text)
    {
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("JSON text is empty.");

        Parser parser = new Parser(text);
        object value = parser.ParseValue();
        parser.SkipWhitespace();

        if (!parser.IsAtEnd)
            throw new FormatException("Unexpected trailing JSON content at " + parser.Position);

        return value;
    }

    private sealed class Parser
    {
        private readonly string text;
        private int index;

        public Parser(string text)
        {
            this.text = text;
        }

        public int Position => index;
        public bool IsAtEnd => index >= text.Length;

        public void SkipWhitespace()
        {
            while (!IsAtEnd)
            {
                char c = text[index];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    index++;
                else
                    break;
            }
        }

        public object ParseValue()
        {
            SkipWhitespace();

            if (IsAtEnd)
                throw new FormatException("Unexpected end of JSON.");

            char c = text[index];

            switch (c)
            {
                case '{':
                    return ParseObject();
                case '[':
                    return ParseArray();
                case '"':
                    return ParseString();
                case 't':
                    Expect("true");
                    return true;
                case 'f':
                    Expect("false");
                    return false;
                case 'n':
                    Expect("null");
                    return null;
                default:
                    if (c == '-' || (c >= '0' && c <= '9'))
                        return ParseNumber();

                    throw new FormatException(
                        "Unexpected character '" + c + "' at " + index);
            }
        }

        private Dictionary<string, object> ParseObject()
        {
            Dictionary<string, object> obj =
                new Dictionary<string, object>();

            Expect('{');
            SkipWhitespace();

            if (Peek('}'))
            {
                index++;
                return obj;
            }

            while (true)
            {
                SkipWhitespace();
                string key = ParseString();

                SkipWhitespace();
                Expect(':');

                obj[key] = ParseValue();

                SkipWhitespace();
                if (Peek(','))
                {
                    index++;
                    continue;
                }

                Expect('}');
                return obj;
            }
        }

        private List<object> ParseArray()
        {
            List<object> list = new List<object>();

            Expect('[');
            SkipWhitespace();

            if (Peek(']'))
            {
                index++;
                return list;
            }

            while (true)
            {
                list.Add(ParseValue());

                SkipWhitespace();
                if (Peek(','))
                {
                    index++;
                    continue;
                }

                Expect(']');
                return list;
            }
        }

        private string ParseString()
        {
            Expect('"');
            StringBuilder sb = new StringBuilder();

            while (!IsAtEnd)
            {
                char c = text[index++];

                if (c == '"')
                    return sb.ToString();

                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }

                if (IsAtEnd)
                    break;

                char escaped = text[index++];
                switch (escaped)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > text.Length)
                            throw new FormatException("Bad unicode escape.");

                        string hex = text.Substring(index, 4);
                        index += 4;
                        sb.Append((char)int.Parse(
                            hex, NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture));
                        break;
                    default:
                        throw new FormatException(
                            "Bad escape '\\" + escaped + "' at " + index);
                }
            }

            throw new FormatException("Unterminated JSON string.");
        }

        private double ParseNumber()
        {
            int start = index;

            if (Peek('-'))
                index++;

            while (!IsAtEnd)
            {
                char c = text[index];
                if ((c >= '0' && c <= '9') ||
                    c == '.' || c == 'e' || c == 'E' ||
                    c == '+' || c == '-')
                {
                    index++;
                }
                else
                {
                    break;
                }
            }

            string number = text.Substring(start, index - start);
            if (!double.TryParse(
                    number, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double value))
            {
                throw new FormatException(
                    "Invalid number '" + number + "' at " + start);
            }

            return value;
        }

        private void Expect(string literal)
        {
            if (index + literal.Length > text.Length ||
                string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException(
                    "Expected '" + literal + "' at " + index);
            }

            index += literal.Length;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();

            if (IsAtEnd || text[index] != expected)
            {
                throw new FormatException(
                    "Expected '" + expected + "' at " + index);
            }

            index++;
        }

        private bool Peek(char expected)
        {
            return !IsAtEnd && text[index] == expected;
        }
    }
}
