namespace TrainerService.Core.Features.Sessions.Grading;

/// <summary>
///     In-process queue of session ids awaiting AI grading (#585). TrainerService has no Wolverine/
///     Redis, so deferred work is an unbounded <c>Channel</c> drained by a hosted background service.
///     <see cref="Enqueue"/> is fire-and-forget (the HTTP request returns immediately after Complete).
/// </summary>
public interface IMockGradingQueue
{
    /// <summary>Schedules a completed mock session for AI grading. Returns immediately.</summary>
    void Enqueue(Guid sessionId);
}
