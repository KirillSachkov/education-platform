using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues.Events;

public sealed record IssueProgressApprovedEvent(IssueProgress Progress) : IDomainEvent;
    
