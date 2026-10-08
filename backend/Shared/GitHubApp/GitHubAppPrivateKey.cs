using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Shared.GitHubApp;

/// <summary>
///     Singleton holder для RSA private key GitHub App'а. Lazy-init —
///     <see cref="RSA"/> создаётся при первом обращении к <see cref="Rsa"/>
///     и живёт до конца процесса (GC соберёт при shutdown).
///
///     <para>
///     <b>Non-disposable by design.</b> Раньше тип реализовывал <see cref="IDisposable"/>,
///     но в DI-конфигурациях, где сервис-обёртка регистрировалась scoped/transient
///     (как было в ARS), RSA уничтожался при dispose scope'а — а
///     <c>Microsoft.IdentityModel.Tokens.CryptoProviderFactory.Default</c>
///     кэширует <c>SignatureProvider</c> по <c>SecurityKey.InternalId</c>
///     (детерминирован на данных RSA). Два <c>RsaSecurityKey</c> вокруг разных
///     RSA-объектов с одинаковым PEM имеют одинаковый InternalId → cache hit
///     возвращает provider с reference на disposed RSA из прошлого scope'а →
///     <see cref="ObjectDisposedException"/> на <c>handler.WriteToken</c>. Issue #203.
///     </para>
///
///     <para>
///     Правильный фикс — singleton lifetime + не звать Dispose. RSA — managed
///     класс, GC соберёт его. Этот тип регистрируется как <c>AddSingleton</c>.
///     </para>
/// </summary>
public sealed class GitHubAppPrivateKey
{
    private readonly Lazy<RSA> _rsa;

    public GitHubAppPrivateKey(IOptions<GitHubAppOptions> options)
    {
        string base64 = options.Value.PrivateKeyPemBase64;
        _rsa = new Lazy<RSA>(() => Load(base64));
    }

    public RSA Rsa => _rsa.Value;

    private static RSA Load(string base64)
    {
        byte[] decoded = Convert.FromBase64String(base64);
        string pem = Encoding.UTF8.GetString(decoded);
        RSA rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return rsa;
    }
}
