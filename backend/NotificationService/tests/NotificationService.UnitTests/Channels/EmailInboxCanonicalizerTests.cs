using NotificationService.Core.Channels.Email;

namespace NotificationService.UnitTests.Channels;

/// <summary>
/// Канонизация физического инбокса для дедупа доставки дайджеста: несколько Identity-аккаунтов
/// на один Gmail-инбокс (точки/<c>+tag</c>/регистр <c>RequireUniqueEmail</c> не сворачивает) должны
/// дать один канонический адрес. Не-Gmail домены трогаем консервативно (только lower-case),
/// чтобы не слить разные ящики у провайдеров без dot-/plus-семантики Gmail.
/// </summary>
public sealed class EmailInboxCanonicalizerTests
{
    [Theory]
    [InlineData("kir@gmail.com", "kir@gmail.com")]
    [InlineData("KIR@Gmail.Com", "kir@gmail.com")]                // регистр
    [InlineData("k.i.r@gmail.com", "kir@gmail.com")]              // gmail игнорирует точки
    [InlineData("kir+promo@gmail.com", "kir@gmail.com")]          // gmail игнорирует +tag
    [InlineData("k.ir+x@googlemail.com", "kir@gmail.com")]        // googlemail → gmail, точки + tag
    [InlineData("  kir@gmail.com  ", "kir@gmail.com")]            // trim
    [InlineData("user.name@yandex.ru", "user.name@yandex.ru")]   // не-gmail: точки сохраняем
    [InlineData("User+tag@outlook.com", "user+tag@outlook.com")] // не-gmail: +tag сохраняем, lower
    public void Canonicalize_NormalizesInboxes(string input, string expected) =>
        Assert.Equal(expected, EmailInboxCanonicalizer.Canonicalize(input));

    [Fact]
    public void InboxHash_EqualForGmailAliases_DifferentForDistinctInboxes()
    {
        Assert.Equal(
            EmailInboxCanonicalizer.InboxHash("k.ir+promo@gmail.com"),
            EmailInboxCanonicalizer.InboxHash("kir@gmail.com"));

        Assert.NotEqual(
            EmailInboxCanonicalizer.InboxHash("kir@gmail.com"),
            EmailInboxCanonicalizer.InboxHash("other@gmail.com"));
    }
}
