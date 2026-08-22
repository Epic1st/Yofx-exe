namespace YO4X.Policy;

public enum WorkerActionPlanDisposition
{
    Ready = 0,
    ReconciliationRequired = 1,
    ReviewRequired = 2
}

public sealed record WorkerActionPlanningContext(
    bool AccountConfirmedFlat,
    bool ProtectedReductionPathAvailable);

public sealed record WorkerActionPlanIssue(string Code, string Message);

public sealed record WorkerActionPlan(
    WorkerActionPlanDisposition Disposition,
    IReadOnlyList<WorkerAction> Steps,
    IReadOnlyList<WorkerActionPlanIssue> Issues)
{
    public bool CanExecuteAutomatically => Disposition == WorkerActionPlanDisposition.Ready;
}

public static class WorkerActionPlanner
{
    public static WorkerActionPlan Plan(
        ExecutionSafetyPolicyVector vector,
        WorkerActionPlanningContext context)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<WorkerActionPlanIssue>();
        WorkerActionPlanDisposition disposition = WorkerActionPlanDisposition.Ready;
        WorkerAction actions = vector.WorkerActions;

        if (actions.HasFlag(WorkerAction.Replace) && !actions.HasFlag(WorkerAction.Fence))
        {
            AddIssue(
                WorkerActionPlanDisposition.ReviewRequired,
                "WORKER_REPLACE_REQUIRES_FENCE",
                "A replacement cannot be planned without explicitly fencing the previous worker.");
        }

        bool permitsRiskReducingWork = vector.AllowExposureReduction
            || vector.AllowProtection
            || vector.AllowPendingOrderCancellation
            || vector.AllowEmergencyClose;

        if (actions.HasFlag(WorkerAction.StopAfterFlat)
            && !context.AccountConfirmedFlat
            && !permitsRiskReducingWork)
        {
            AddIssue(
                WorkerActionPlanDisposition.ReviewRequired,
                "STOP_AFTER_FLAT_HAS_NO_REDUCTION_AUTHORITY",
                "Stop-after-flat cannot complete while all risk-reducing actions are denied.");
        }

        if (actions.HasFlag(WorkerAction.Fence)
            && !context.AccountConfirmedFlat
            && permitsRiskReducingWork
            && !context.ProtectedReductionPathAvailable)
        {
            WorkerActionPlanDisposition requiredDisposition =
                actions.HasFlag(WorkerAction.StopAfterFlat)
                    ? WorkerActionPlanDisposition.ReviewRequired
                    : WorkerActionPlanDisposition.ReconciliationRequired;

            AddIssue(
                requiredDisposition,
                actions.HasFlag(WorkerAction.StopAfterFlat)
                    ? "FENCE_CONFLICTS_WITH_STOP_AFTER_FLAT"
                    : "FENCE_REQUIRES_PROTECTED_REDUCTION_PATH",
                "Fencing cannot implicitly remove the only authorized risk-reducing execution path.");
        }

        IReadOnlyList<WorkerAction> steps = vector.EnumerateWorkerActions();
        return new WorkerActionPlan(
            disposition,
            steps,
            Array.AsReadOnly(issues.ToArray()));

        void AddIssue(
            WorkerActionPlanDisposition issueDisposition,
            string code,
            string message)
        {
            if (issueDisposition > disposition)
            {
                disposition = issueDisposition;
            }

            issues.Add(new WorkerActionPlanIssue(code, message));
        }
    }
}
