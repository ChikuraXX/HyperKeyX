namespace HyperKeyX.Domain.Automation.Paths;

public enum PathIssueCode
{
    MissingPayload, UnexpectedPayload, InvalidStructure, InvalidEnum,
    EmptySource, InvalidPoint, InvalidTimeOrder, DuplicatePoint, InvalidAnchor,
    InvalidSettings, MissingGroup, InvalidGroup, DuplicateGroup, MissingTarget,
    InvalidTarget, DuplicateTarget, InvalidCoordinate, ConflictingAnchor,
    DiscontinuousGroup, CrossedGroups, TargetSwitchWhileHeld, InvalidEdit,
    TooManyPoints, UnresolvedOutlier, MotionLimitExceeded, UnreachablePosition,
    AmbiguousTarget, TargetUnavailable, TargetChanged, SchedulingOverrun,
    RandomizationExhausted, InputBusy, InputSendFailed, InputReleaseFailed,
    CaptureStepLimitExceeded, CaptureDurationLimitExceeded, CaptureQueueOverflow, CaptureInterrupted
}

public sealed record PathIssue(
    PathIssueCode Code, int StepIndex = -1, Guid? PointId = null, Guid? TargetId = null);
