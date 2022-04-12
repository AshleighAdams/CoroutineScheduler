#if DEBUG
#define DEBUG_ASYNC
#endif

#if !DEBUG_ASYNC
using System.Diagnostics;
#endif

using System.Runtime.CompilerServices;

[assembly: CLSCompliant(true)]

namespace CoroutineScheduler;

/// <summary>
/// A manually driven scheduler suitible for tight update loops such as game frames. <br />
/// Will use a <see cref="SynchronizationContext"/> to marshal continuations back onto the expected execution environment.
/// </summary>
public sealed class Scheduler : IScheduler
{
	private AsyncManualResetEvent Awaiter { get; } = new AsyncManualResetEvent();
	private SchedulerSynchronizationContext SyncContext { get; }

	/// <summary>
	/// Create a new scheduler
	/// </summary>
	public Scheduler()
	{
		SyncContext = new(SyncronizationContextPost);
	}

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
		{
			Awaiter.Release();
		}
		SynchronizationContext.SetSynchronizationContext(original);
	}

	/// <summary>
	/// Wait until <see cref="Resume()"/> is next invoked.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public YieldTask Yield()
	{
		return new(Awaiter);
	}

	/// <inheritdoc/>
#if !DEBUG_ASYNC
	[DebuggerHidden]
#endif
	public Task SpawnTask(Func<Task> func)
	{
		TaskCompletionSource<object?> proxy = new();
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
				proxy.SetException(ex);
				throw;
			}
		}

	}

#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	private void SyncronizationContextPost(SendOrPostCallback cb, object? state)
	{
		Awaiter.Post(cb, state);
	}
}
