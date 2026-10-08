namespace AuthService.Core.Services;

/// <summary>
///     Текущие версии юридических документов на Платформе.
///     При обновлении документа — bump версию здесь + создать новый
///     <c>{slug}-v{N+1}.md</c> в <c>frontend/content/legal/</c>.
///     Старые версии остаются в архиве.
///
///     Должно быть синхронизировано с
///     <c>frontend/src/shared/legal/versions.ts#CURRENT_LEGAL_VERSIONS</c>.
/// </summary>
public static class LegalDocumentVersions
{
    public const string Offer = "v2";
    public const string PrivacyPolicy = "v3";
    public const string PersonalDataConsent = "v2";
    public const string CookiesPolicy = "v1";
    public const string MarketingConsent = "v1";
}
