using System.Text;

namespace AssignmentReviewService.Core.Features.Reviews.Services;

/// <summary>
///     Repairs the common UTF-8 mojibake shape returned by some AI/OpenAI-compatible
///     providers for Cyrillic text: UTF-8 bytes decoded as Latin-1/Windows-1252
///     characters (e.g. <c>ÐžÑˆÐ¸Ð±ÐºÐ°</c> instead of <c>Ошибка</c>).
/// </summary>
public static class AiReviewTextEncodingRepair
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string RepairUtf8Mojibake(string text)
    {
        if (string.IsNullOrEmpty(text) || !ContainsMojibakeMarker(text))
            return text;

        StringBuilder result = new(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            if (!CanRepresentMojibakeByte(text[i]))
            {
                result.Append(text[i]);
                i++;
                continue;
            }

            int start = i;
            while (i < text.Length && CanRepresentMojibakeByte(text[i]))
                i++;

            string run = text[start..i];
            result.Append(RepairRunIfBetter(run));
        }

        return result.ToString();
    }

    private static string RepairRunIfBetter(string run)
    {
        if (!ContainsMojibakeMarker(run))
            return run;

        byte[] bytes = new byte[run.Length];
        for (int i = 0; i < run.Length; i++)
        {
            if (!TryGetOriginalByte(run[i], out byte value))
                return run;
            bytes[i] = value;
        }

        string candidate;
        try
        {
            candidate = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Провайдер съедает отдельные байты внутри mojibake (наблюдали потерю
            // 0x85/NEL — видимо, line-split в их пайплайне), поэтому byte-стрим
            // частично невалиден как UTF-8. Лояльный декод восстанавливает всё
            // остальное, дыры становятся U+FFFD; score-gate ниже решает, лучше ли
            // кандидат оригинала (U+FFFD штрафуется сильнее mojibake-маркеров).
            candidate = Encoding.UTF8.GetString(bytes);
        }

        if (CountCyrillic(candidate) == 0)
            return run;

        int originalScore = Score(run);
        int candidateScore = Score(candidate);
        return candidateScore > originalScore + 2 ? candidate : run;
    }

    /// <summary>
    ///     Детектор для retry-решения в <c>AiReviewer</c>: текст ПОСЛЕ репарации всё ещё
    ///     несёт следы битой кодировки — U+FFFD (репарация была lossy: провайдер съел
    ///     байты, слова не восстановить) либо плотный mojibake (репарация не взялась).
    ///     Порог ≥3 для Ð/Ñ/C1 исключает false-positive на легитимной пунктуации
    ///     (em-dash и т.п. живут в Windows1252-карте и маркерами здесь не считаются).
    /// </summary>
    public static bool ContainsMojibakeArtifacts(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        if (text.Contains('�', StringComparison.Ordinal))
            return true;

        int markers = 0;
        foreach (char c in text)
        {
            if ((c is 'Ð' or 'Ñ' || c is >= '\u0080' and <= '\u009F') && ++markers >= 3)
                return true;
        }

        return false;
    }

    private static bool ContainsMojibakeMarker(string text) =>
        text.Any(IsMojibakeMarker);

    private static bool IsMojibakeMarker(char c) =>
        c is 'Ð' or 'Ñ' or 'Â'
        || c is >= '\u0080' and <= '\u009F'
        || Windows1252ReverseBytes.ContainsKey(c);

    private static bool CanRepresentMojibakeByte(char c) =>
        c <= '\u00FF' || Windows1252ReverseBytes.ContainsKey(c);

    private static bool TryGetOriginalByte(char c, out byte value)
    {
        if (c <= '\u00FF')
        {
            value = (byte)c;
            return true;
        }

        return Windows1252ReverseBytes.TryGetValue(c, out value);
    }

    private static int Score(string text) =>
        CountCyrillic(text) * 3
        - text.Count(IsMojibakeMarker) * 2
        - text.Count(c => c == '�') * 4;

    private static int CountCyrillic(string text) =>
        text.Count(c => c is >= '\u0400' and <= '\u04FF');

    private static readonly IReadOnlyDictionary<char, byte> Windows1252ReverseBytes =
        new Dictionary<char, byte>
        {
            ['€'] = 0x80,
            ['‚'] = 0x82,
            ['ƒ'] = 0x83,
            ['„'] = 0x84,
            ['…'] = 0x85,
            ['†'] = 0x86,
            ['‡'] = 0x87,
            ['ˆ'] = 0x88,
            ['‰'] = 0x89,
            ['Š'] = 0x8A,
            ['‹'] = 0x8B,
            ['Œ'] = 0x8C,
            ['Ž'] = 0x8E,
            ['‘'] = 0x91,
            ['’'] = 0x92,
            ['“'] = 0x93,
            ['”'] = 0x94,
            ['•'] = 0x95,
            ['–'] = 0x96,
            ['—'] = 0x97,
            ['˜'] = 0x98,
            ['™'] = 0x99,
            ['š'] = 0x9A,
            ['›'] = 0x9B,
            ['œ'] = 0x9C,
            ['ž'] = 0x9E,
            ['Ÿ'] = 0x9F,
        };
}
