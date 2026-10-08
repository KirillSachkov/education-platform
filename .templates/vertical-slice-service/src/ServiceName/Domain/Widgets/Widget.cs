using ServiceName.Domain.Widgets.Events;
using SharedKernel.DomainEvents;

namespace ServiceName.Domain.Widgets;

/// <summary>
/// Example aggregate root. Private ctor + static `Create` factory.
/// All mutations go through methods that raise a domain event.
/// </summary>
public sealed class Widget : AggregateRoot
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public WidgetName Name { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    // EF Core constructor — do not remove.
    private Widget() { }

    private Widget(Guid id, Guid ownerId, WidgetName name)
    {
        Id = id;
        OwnerId = ownerId;
        Name = name;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public static Result<Widget, Error> Create(Guid ownerId, WidgetName name)
    {
        var widget = new Widget(Guid.CreateVersion7(), ownerId, name);
        widget.RaiseDomainEvent(new WidgetCreatedEvent(widget.Id, ownerId, name.Value));
        return widget;
    }

    public void Rename(WidgetName newName)
    {
        if (Name == newName)
            return;

        Name = newName;
        UpdatedAt = DateTime.UtcNow;
        RaiseDomainEvent(new WidgetRenamedEvent(Id, newName.Value));
    }
}
