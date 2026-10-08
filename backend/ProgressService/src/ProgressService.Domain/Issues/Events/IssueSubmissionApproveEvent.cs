using ProgressService.Domain.IssueSubmissions;
using SharedKernel.DomainEvents;

namespace ProgressService.Domain.Issues.Events;

public sealed record IssueSubmissionApproveEvent(IssueSubmission Submission) : IDomainEvent;
    
