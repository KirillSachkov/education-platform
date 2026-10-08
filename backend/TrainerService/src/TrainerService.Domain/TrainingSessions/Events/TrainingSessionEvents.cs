using SharedKernel.DomainEvents;

namespace TrainerService.Domain.TrainingSessions.Events;

public sealed record TrainingSessionStartedEvent(Guid SessionId, Guid UserId, TrainingMode Mode) : IDomainEvent;

public sealed record TrainingSessionCompletedEvent(Guid SessionId, Guid UserId, int ScorePercent) : IDomainEvent;
