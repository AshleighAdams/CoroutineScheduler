#if DEBUG
#define DEBUG_ASYNC
#endif

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CoroutineScheduler;

internal class AsyncManualResetEvent : IYieldAwaiter
{
	private struct PostedWorkItem
	{
		public SendOrPostCallback Continuation { get; set; }
		public object? Context { get; set; }
	}

	private struct YieldedWorkItem
	{
		public Action Continuation { get; set; }
		public ExecutionContext? Context { get; set; }
	}

	private readonly ConcurrentQueue<YieldedWorkItem> explicitQueue = new();
	private readonly ConcurrentQueue<PostedWorkItem> implicitQueue = new();

	bool IYieldAwaiter.IsCompleted => false;
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	void INotifyCompletion.OnCompleted(Action continuation)
	{
		explicitQueue.Enqueue(new()
		{
			Continuation = continuation,
			Context = ExecutionContext.Capture(),
		});
	}
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	void ICriticalNotifyCompletion.UnsafeOnCompleted(Action continuation)
	{
		explicitQueue.Enqueue(new()
		{
			Continuation = continuation,
			Context = null,
		});
	}

#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	// Reduce Action delegate memory allocations by giving post a direct call
	// 
	internal void Post(SendOrPostCallback cb, object? state)
	{
		implicitQueue.Enqueue(new()
		{
			Continuation = cb,
			Context = state,
		});
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	void IYieldAwaiter.GetResult()
	{
	}

	private ContextCallback? Runner { get; set; }
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
	public void Release()
	{
		// cache this allocation
		[DebuggerNonUserCode]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void executionContextRunner(object obj) => (obj as Action)!();
		if (Runner is null)
			Runner = executionContextRunner!;

		ReleaseImplicit();

		int alreadyQueuedCount = explicitQueue.Count;
		while (alreadyQueuedCount > 0)
		{
			if (!explicitQueue.TryDequeue(out var workItem))
				throw new InternalReleaseException("Failed to dequeue the next continuation.");

			alreadyQueuedCount--;

			if (workItem.Context is null)
				workItem.Continuation();
			else
				ExecutionContext.Run(workItem.Context, Runner, workItem.Continuation);

			ReleaseImplicit();
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ReleaseImplicit()
	{
		while (implicitQueue.TryDequeue(out var workItem))
			workItem.Continuation(workItem.Context);
	}
}
