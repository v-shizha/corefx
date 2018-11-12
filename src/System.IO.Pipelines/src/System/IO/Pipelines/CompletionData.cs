// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Sources;

namespace System.IO.Pipelines
{
    internal class CompletionData : IThreadPoolWorkItem
    {
        // These callbacks all point to the same methods but are different delegate types
        private static readonly ContextCallback s_executionContextCallback = ExecuteWithExecutionContext;
        private static readonly ContextCallback s_executionContextRawCallback = ExecuteWithoutExecutionContext;
        private static readonly SendOrPostCallback s_syncContextExecutionContextCallback = ExecuteWithExecutionContext;
        private static readonly SendOrPostCallback s_syncContextExecuteWithoutExecutionContextCallback = ExecuteWithoutExecutionContext;
        private static readonly Action<object> s_scheduleWithExecutionContextCallback = ExecuteWithExecutionContext;

        private Action<object> _completion;
        private object _completionState;
        private ExecutionContext _executionContext;
        private SynchronizationContext _synchronizationContext;

        public bool HasCompletion => _completion != null;

        public void Set(Action<object> completion, object completionState, ExecutionContext executionContext, SynchronizationContext synchronizationContext)
        {
            _completion = completion;
            _completionState = completionState;
            _executionContext = executionContext;
            _synchronizationContext = synchronizationContext;
        }

        public void Reset()
        {
            // REVIEW: Make sure this isn't currently queued

            _completion = null;
            _completionState = null;
            _executionContext = null;
            _synchronizationContext = null;
        }

        public void Execute()
        {
            // TODO: Check SyncContext and ExecutionContext
            _completion(_completionState);
        }

        public void TrySchedule(PipeScheduler scheduler)
        {
            // Nothing to do
            if (_completion == null)
            {
                return;
            }

            //if (scheduler.IsDefaultThreadPoolScheduler)
            //{
            //    ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
            //    return;
            //}

            ExecuteCustomScheduler(scheduler);
        }

        private void ExecuteCustomScheduler(PipeScheduler scheduler)
        {
            // Ultimately, we need to call either
            // 1. The sync context with a delegate
            // 2. The scheduler with a delegate
            // That delegate and state will either be the action passed in directly
            // or it will be that specified delegate wrapped in ExecutionContext.Run

            if (_synchronizationContext == null)
            {
                // We don't have a SynchronizationContext so execute on the specified scheduler
                if (_executionContext == null)
                {
                    // We can run directly, this should be the default fast path
                    scheduler.Schedule(_completion, _completionState);
                    return;
                }

                // We also have to run on the specified execution context so run the scheduler and execute the
                // delegate on the execution context
                scheduler.Schedule(s_scheduleWithExecutionContextCallback, this);
            }
            else
            {
                if (_executionContext == null)
                {
                    // We need to box the struct here since there's no generic overload for state
                    _synchronizationContext.Post(s_syncContextExecuteWithoutExecutionContextCallback, this);
                }
                else
                {
                    // We need to execute the callback with the execution context
                    _synchronizationContext.Post(s_syncContextExecutionContextCallback, this);
                }
            }
        }

        private static void ExecuteWithoutExecutionContext(object state)
        {
            CompletionData completionData = (CompletionData)state;
            completionData._completion(completionData._completionState);
        }

        private static void ExecuteWithExecutionContext(object state)
        {
            CompletionData completionData = (CompletionData)state;
            Debug.Assert(completionData._executionContext != null);
            ExecutionContext.Run(completionData._executionContext, s_executionContextRawCallback, state);
        }
    }
}
