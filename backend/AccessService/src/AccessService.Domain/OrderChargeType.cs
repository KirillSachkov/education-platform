namespace AccessService.Domain;

/// <summary>
/// Тип списания по <see cref="Order"/> (#614). <see cref="INITIAL"/> — первый платёж
/// через checkout с редиректом; <see cref="RENEWAL"/> — серверное автосписание по подписке
/// (без редиректа, по сохранённому RebillId). Дефолт — INITIAL.
/// </summary>
public enum OrderChargeType
{
    /// <summary>Первый платёж: пользователь проходит checkout-redirect провайдера.</summary>
    INITIAL,

    /// <summary>Автопродление подписки: безредиректное server-side списание по RebillId.</summary>
    RENEWAL,
}
