using System.Text;

namespace QA.PerfKit
{
    /// <summary>Game-set key/value pairs shown as `ctx=k:v;k:v` on every kit line. Fixed arrays, so reading it on a
    /// dropped frame allocates nothing. Pure C#; main thread only.</summary>
    internal sealed class PerfContext
    {
        public const int MAX_KEYS = 8;
        public const int MAX_VALUE = 32;

        private readonly string[] _keys = new string[MAX_KEYS];
        private readonly string[] _values = new string[MAX_KEYS];
        private int _count;

        public int Count => _count;

        /// <summary>Adds or replaces <paramref name="key"/>; a ninth distinct key is ignored. Separators and
        /// whitespace are stripped (they would break the line), the value is cut to <see cref="MAX_VALUE"/>.</summary>
        public void Set(string key, string value)
        {
            key = Sanitize(key, MAX_VALUE, stripColon: true);
            if (key.Length == 0) return;
            value = Sanitize(value, MAX_VALUE, stripColon: false);

            int index = IndexOf(key);
            if (index < 0)
            {
                if (_count >= MAX_KEYS) return;
                index = _count++;
                _keys[index] = key;
            }
            _values[index] = value;
        }

        public void Clear(string key)
        {
            int index = IndexOf(Sanitize(key, MAX_VALUE, stripColon: true));
            if (index < 0) return;

            for (int i = index; i < _count - 1; i++)
            {
                _keys[i] = _keys[i + 1];
                _values[i] = _values[i + 1];
            }
            _count--;
            _keys[_count] = null;
            _values[_count] = null;
        }

        public void ClearAll()
        {
            for (int i = 0; i < _count; i++)
            {
                _keys[i] = null;
                _values[i] = null;
            }
            _count = 0;
        }

        /// <summary>Appends `k:v;k:v` in insertion order; appends nothing when empty.</summary>
        public void AppendTo(StringBuilder sb)
        {
            for (int i = 0; i < _count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(_keys[i]).Append(':').Append(_values[i]);
            }
        }

        /// <summary>Removes whitespace, `;` and `=` (and `:` when asked) and cuts to <paramref name="max"/> chars.
        /// Returns the input itself when nothing changes, so a clean value costs no allocation.</summary>
        internal static string Sanitize(string text, int max, bool stripColon)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            bool clean = text.Length <= max;
            for (int i = 0; clean && i < text.Length; i++)
                if (IsSeparator(text[i], stripColon)) clean = false;
            if (clean) return text;

            var sb = new StringBuilder(text.Length < max ? text.Length : max);
            for (int i = 0; i < text.Length && sb.Length < max; i++)
                if (!IsSeparator(text[i], stripColon)) sb.Append(text[i]);
            return sb.ToString();
        }

        private static bool IsSeparator(char c, bool stripColon) =>
            c == ';' || c == '=' || char.IsWhiteSpace(c) || (stripColon && c == ':');

        private int IndexOf(string key)
        {
            for (int i = 0; i < _count; i++)
                if (_keys[i] == key) return i;
            return -1;
        }
    }
}
