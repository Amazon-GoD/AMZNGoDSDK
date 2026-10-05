using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AMZNGoDSDK.Editor
{
    /// <summary>Validates manifest JSON and locates root fields without rewriting consumer data.</summary>
    internal sealed class ManifestJson
    {
        private static readonly Regex Number = new Regex(@"\G-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?");
        private readonly string _text;
        private int _position;
        private readonly Dictionary<string, int> _rootValues = new Dictionary<string, int>(StringComparer.Ordinal);

        private ManifestJson(string text) => _text = text;

        internal static ManifestJson Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) throw new FormatException("Манифест пуст.");
            var json = new ManifestJson(text);
            json.SkipWhitespace();
            if (json.Current != '{') throw new FormatException("Корень манифеста должен быть JSON-объектом.");
            json.ReadObject(0);
            json.SkipWhitespace();
            if (json._position != text.Length) throw json.Invalid();
            return json;
        }

        internal int FindRootValue(string key) => _rootValues.TryGetValue(key, out int index) ? index : -1;
        private char Current => _position < _text.Length ? _text[_position] : '\0';
        private FormatException Invalid() => new FormatException("Некорректный JSON манифеста, позиция " + _position + ".");

        private void SkipWhitespace()
        {
            while (Current == ' ' || Current == '\t' || Current == '\r' || Current == '\n') _position++;
        }

        private bool Take(char token)
        {
            SkipWhitespace();
            if (Current != token) return false;
            _position++;
            return true;
        }

        private void Require(char token)
        {
            if (!Take(token)) throw Invalid();
        }

        private void ReadValue(int depth)
        {
            if (depth > 128) throw Invalid();
            SkipWhitespace();
            switch (Current)
            {
                case '{': ReadObject(depth); return;
                case '[':
                    _position++;
                    if (Take(']')) return;
                    do { ReadValue(depth + 1); } while (Take(','));
                    Require(']');
                    return;
                case '"': ReadString(); return;
            }

            foreach (string literal in new[] { "true", "false", "null" })
            {
                if (_position + literal.Length <= _text.Length &&
                    string.CompareOrdinal(_text, _position, literal, 0, literal.Length) == 0)
                {
                    _position += literal.Length;
                    return;
                }
            }
            var number = Number.Match(_text, _position);
            if (!number.Success) throw Invalid();
            _position += number.Length;
        }

        private void ReadObject(int depth)
        {
            Require('{');
            if (Take('}')) return;
            do
            {
                SkipWhitespace();
                string key = ReadString();
                Require(':');
                SkipWhitespace();
                if (depth == 0)
                {
                    if (_rootValues.ContainsKey(key)) throw new FormatException("Повторяющееся поле манифеста: " + key);
                    _rootValues.Add(key, _position);
                }
                ReadValue(depth + 1);
            } while (Take(','));
            Require('}');
        }

        private string ReadString()
        {
            Require('"');
            var value = new StringBuilder();
            while (_position < _text.Length)
            {
                char character = _text[_position++];
                if (character == '"') return value.ToString();
                if (character < 32) throw Invalid();
                if (character != '\\')
                {
                    value.Append(character);
                    continue;
                }
                if (_position >= _text.Length) throw Invalid();
                char escape = _text[_position++];
                switch (escape)
                {
                    case '"': case '\\': case '/': value.Append(escape); break;
                    case 'b': value.Append('\b'); break;
                    case 'f': value.Append('\f'); break;
                    case 'n': value.Append('\n'); break;
                    case 'r': value.Append('\r'); break;
                    case 't': value.Append('\t'); break;
                    case 'u':
                        if (_position + 4 > _text.Length || !ushort.TryParse(_text.Substring(_position, 4),
                                NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code)) throw Invalid();
                        value.Append((char)code);
                        _position += 4;
                        break;
                    default: throw Invalid();
                }
            }
            throw Invalid();
        }
    }
}
