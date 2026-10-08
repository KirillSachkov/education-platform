using System.Globalization;
using System.Text;
using System.Web;

namespace Core.HttpCommunication;

public sealed class QueryStringBuilder
{
    private readonly List<KeyValuePair<string, string>> _params = [];

    public QueryStringBuilder Add(string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
            _params.Add(new KeyValuePair<string, string>(key, value));

        return this;
    }

    public QueryStringBuilder Add(string key, int value)
    {
        _params.Add(new KeyValuePair<string, string>(key, value.ToString(CultureInfo.InvariantCulture)));
        return this;
    }

    public QueryStringBuilder Add(string key, int? value)
    {
        if (value.HasValue)
            _params.Add(new KeyValuePair<string, string>(key, value.Value.ToString(CultureInfo.InvariantCulture)));

        return this;
    }

    public QueryStringBuilder Add(string key, bool? value)
    {
        if (value.HasValue)
            _params.Add(new KeyValuePair<string, string>(key, value.Value.ToString().ToLowerInvariant()));

        return this;
    }

    public QueryStringBuilder Add(string key, Guid? value)
    {
        if (value.HasValue)
            _params.Add(new KeyValuePair<string, string>(key, value.Value.ToString()));

        return this;
    }

    public string Build()
    {
        if (_params.Count == 0)
            return string.Empty;

        var sb = new StringBuilder("?");

        for (int i = 0; i < _params.Count; i++)
        {
            if (i > 0)
                sb.Append('&');

            sb.Append(HttpUtility.UrlEncode(_params[i].Key));
            sb.Append('=');
            sb.Append(HttpUtility.UrlEncode(_params[i].Value));
        }

        return sb.ToString();
    }

    public override string ToString() => Build();
}
