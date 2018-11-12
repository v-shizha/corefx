// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;

namespace System.IO.Pipelines
{
    internal class CompletionData
        : IThreadPoolWorkItem
    {
        // These callbacks all point to the same methods but are different delegate types
        private static readonly ContextCallback s_executionContextCallback = ExecuteWithExecutionContext;
        private static readonly ContextCallback s_executionContextRawCallback = ExecuteWithoutExecutionContext;
        private static readonly SendOrPostCallback s_syncContextExecutionContextCallback = ExecuteWithExecutionContext;
        private static readonly SendOrPostCallback s_syncContextExecuteWithoutExecutionContextCallback = ExecuteWithoutExecutionContext;
        private static readonly Action<object> s_scheduleWithExecutionContextCallback = ExecuteWithExecutionContext;

        public Action<object> Completion { get; }
        public object CompletionState { get; }
        public ExecutionContext ExecutionContext { get; }
        public SynchronizationContext SynchronizationContext { get; }

        public void Set(Action<object> completion, object completionState, ExecutionContext executionContext, SynchronizationContext synchronizationContext)
        {
            Completion = completion;
            CompletionState = completionState;
            ExecutionContext = executionContext;
            SynchronizationContext = synchronizationContext;
        }

        public void ExecuteWorkItem()
        {
            Completion(CompletionState);
        }

        public void TrySchedule(PipeScheduler scheduler)
        {
            // Nothing to do
            if (Completion == null)
            {
                return;
            }

            if (scheduler.IsDefaultThreadPoolScheduler)
            {
                ThreadPool.QueueCustomWorkItem(this);
                return;
            }

            // Ultimately, we need to call either
            // 1. The sync context with a delegate
            // 2. The scheduler with a delegate
            // That delegate and state will either be the action passed in directly
            // or it will be that specified delegate wrapped in ExecutionContext.Run

            if (SynchronizationContext == null)
            {
                // We don't have a SynchronizationContext so execute on the specified scheduler
                if (ExecutionContext == null)
                {
                    // We can run directly, this should be the default fast path
                    scheduler.Schedule(Completion, CompletionState);
                    return;
                }

                // We also have to run on the specified execution context so run the scheduler and execute the
                // delegate on the execution context
                scheduler.Schedule(s_scheduleWithExecutionContextCallback, this);
            }
            else
            {
                if (ExecutionContext == null)
                {
                    // We need to box the struct here since there's no generic overload for state
                    SynchronizationContext.Post(s_syncContextExecuteWithoutExecutionContextCallback, this);
                }
                else
                {
                    // We need to execute the callback with the execution context
                    SynchronizationContext.Post(s_syncContextExecutionContextCallback, this);
                }
            }
        }

        private static void ExecuteWithoutExecutionContext(object state)
        {
            CompletionData completionData = (CompletionData)state;
            completionData.Completion(completionData.CompletionState);
        }

        private static void ExecuteWithExecutionContext(object state)
        {
            CompletionData completionData = (CompletionData)state;
            Debug.Assert(completionData.ExecutionContext != null);
            ExecutionContext.Run(completionData.ExecutionContext, s_executionContextRawCallback, state);
        }
    }
}
