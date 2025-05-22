#if DEBUG
#define DEBUG_ASYNC
#endif
#if !DEBUG_ASYNC
using System.Diagnostics;
#endif

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: CLSCompliant(true)]

namespace CoroutineScheduler;

/// <summary>
/// Represents the data and response for when an unhandled exception occurs.
/// </summary>
public class UnhandledExceptionEventArgs : EventArgs
{
	/// <summary>
	/// Create an event from the exception <paramref name="ex"/>.
	/// </summary>
	internal UnhandledExceptionEventArgs(Exception ex)
	{
		Exception = ex;
	}

	/// <summary>
	/// The exception that was caught.
	/// </summary>
	public Exception Exception { get; }
	/// <summary>
	/// Whether or not the <see cref="Task"/> returned by <see cref="Scheduler.SpawnTask(Func{Task})"/> completes the promise with an exception.
	/// </summary>
	public bool RethrowAsync { get; set; } = true;
	/// <summary>
	/// Whether or not the exception should be rethrown, resulting in an instant runtime crash.
	/// </summary>
	public bool Rethrow { get; set; } = true;
}

/// <summary>
/// A manually driven scheduler suitible for tight update loops such as game frames. <br />
/// Will use a <see cref="SynchronizationContext"/> to marshal continuations back onto the expected execution environment.
/// </summary>
public sealed class Scheduler : IScheduler
{
	private SchedulerSynchronizationContext SyncContext { get; }
	private YieldAwaiter NormalAwaiter { get; }
	private YieldAwaiter MarshalAwaiter { get; }

	/// <summary>
	/// Create a new scheduler
	/// </summary>
	public Scheduler()
	{
		SyncContext = new(PostContinuation);
		NormalAwaiter = new(this, true);
		MarshalAwaiter = new(this, false);
	}

	private ContextCallback? Runner { get; set; }

	private int? ResumingOnThread { get; set; }
	private bool RequiresMarshalling => !ResumingOnThread.HasValue || ResumingOnThread.Value != Thread.CurrentThread.ManagedThreadId;

	/// <summary>
	/// Resume any tasks that are at the point of invocation are currently awaiting `Yield()`, and <br />
	/// accept any posted continuations from the syncronization context for the duration of the call.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	public void Resume()
	{
		var original = SynchronizationContext.Current;
		SynchronizationContext.SetSynchronizationContext(SyncContext);
		ResumingOnThread = Thread.CurrentThread.ManagedThreadId;
		{
			// cache this allocation
			[DebuggerNonUserCode]
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			static void executionContextRunner(object obj) => (obj as Action)!();
			Runner ??= executionContextRunner!;

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
		ResumingOnThread = null;
		SynchronizationContext.SetSynchronizationContext(original);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private void ReleaseImplicit()
	{
		while (implicitQueue.TryDequeue(out var workItem))
			workItem.Continuation(workItem.Context);
	}

	/// <summary>
	/// Wait until <see cref="Resume()"/> is next invoked.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public YieldTask Yield()
	{
		return new(NormalAwaiter);
	}

	/// <summary>
	/// Wait until <see cref="Resume()"/> is next invoked.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public YieldTask Marshal()
	{
		return new(MarshalAwaiter);
	}

	/// <inheritdoc/>
#if !DEBUG_ASYNC
	[DebuggerHidden]
#endif
	public Task SpawnTask(Func<Task> func)
	{
		var proxy = new TaskCompletionSource<object?>();
		SpawnTaskInternal();
		return proxy.Task;

#if !DEBUG_ASYNC
		[DebuggerHidden]
#endif
		async void SpawnTaskInternal()
		{
			try
			{
				SynchronizationContext.SetSynchronizationContext(SyncContext);
				await func();
				proxy.SetResult(null);
			}
			catch (Exception ex)
			{
				var e = new UnhandledExceptionEventArgs(ex);
				UnhandledException?.Invoke(this, e);

				if (e.RethrowAsync)
					proxy.SetException(ex);
				else
					proxy.SetResult(null);
				if (e.Rethrow)
					throw;
			}
		}

	}

	/// <summary>
	/// An event fired when an unhandled exception occurs within a spawned task.
	/// </summary>
	public event EventHandler<UnhandledExceptionEventArgs>? UnhandledException;

#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	private void PostContinuation(SendOrPostCallback cb, object? state)
	{
		if (RequiresMarshalling)
			implicitQueue.Enqueue(new() { Continuation = cb, Context = state });
		else
			cb(state);
	}

#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
	private void Yield(Action action, ExecutionContext? ctx)
	{
		explicitQueue.Enqueue(new() { Continuation = action, Context = ctx });
	}

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

	private class YieldAwaiter : IYieldAwaiter
	{
		public Scheduler Self { get; }
		public bool ForceYield { get; }

		public YieldAwaiter(Scheduler self, bool forceYield)
		{
			Self = self;
			ForceYield = forceYield;
		}

		bool IYieldAwaiter.IsCompleted => ForceYield switch
		{
			true => false,
			false => !Self.RequiresMarshalling,
		};
#if !DEBUG_ASYNC
		[DebuggerNonUserCode]
#endif
		void INotifyCompletion.OnCompleted(Action continuation)
		{
			Self.Yield(continuation, ExecutionContext.Capture());
		}
#if !DEBUG_ASYNC
		[DebuggerNonUserCode]
#endif
		void ICriticalNotifyCompletion.UnsafeOnCompleted(Action continuation)
		{
			Self.Yield(continuation, null);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
#if !DEBUG_ASYNC
		[DebuggerNonUserCode]
#endif
		void IYieldAwaiter.GetResult()
		{
		}
	}
}
