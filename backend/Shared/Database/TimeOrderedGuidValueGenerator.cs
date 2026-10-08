using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace PlatformDatabase;

/// <summary>
///     EF Core ValueGenerator выдающий time-ordered <see cref="Guid"/> v7.
///
///     <para>Зачем нужен:</para>
///     Если установить PK через <c>Guid.CreateVersion7()</c> в конструкторе entity ДО Add'а
///     в DbSet (или до добавления в navigation collection trackable aggregate root'а), то
///     EF Change Tracker считает entity "detached existing" (PK уже выставлен → assume previously
///     persisted, нужен Reattach) и помечает state как <c>Modified</c>. На SaveChanges идёт
///     UPDATE WHERE id=&lt;новый guid&gt; → 0 rows affected → <c>DbUpdateConcurrencyException</c>.
///
///     <para>Решение:</para>
///     В конфигурации использовать <c>HasValueGenerator&lt;TimeOrderedGuidValueGenerator&gt;()</c>
///     + <c>ValueGeneratedOnAdd()</c>. Domain entity ctor оставляет <c>Id = Guid.Empty</c>;
///     EF при детектировании новой сущности (в DbSet или navigation collection) вызывает этот
///     generator и сам заполняет PK значением v7. Entity tracking корректно помечает её Added.
///     Свойство Id будет валидным сразу после Add (до SaveChanges) — так что caller может
///     спокойно делать <c>return step.Id;</c>.
/// </summary>
public sealed class TimeOrderedGuidValueGenerator : ValueGenerator<Guid>
{
    public override bool GeneratesTemporaryValues => false;

    public override Guid Next(EntityEntry entry) => Guid.CreateVersion7();
}
