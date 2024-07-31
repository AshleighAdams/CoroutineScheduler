#if DEBUG
#define DEBUG_ASYNC
#endif

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace CoroutineScheduler;

/// <summary>
/// Waits for a signal to be raised<br/>
/// <br/>
/// Note that this is a hybrid of a condition variable, a manual reset event, and an automatic reset event:<br/>
/// - Condition variable takes a locked mutex to eliminate race conditions, atomically unlocking and waiting<br/>
/// - Manual reset event requires the event be reset, and does not guarantee all current waiters have been resumed (non-deterministic)<br/>
/// - AutoResetEvent will release one waiter in a non-deterministic fashion, and if no waiters, the next waiter will not block
///   resolving the race condition present in the manual reset event <br/>
/// <br/>
/// To resolve the race condition in a manner similar to a condition variable, you may call Wait() early, then await the return value
/// after you have completed your check in the case you need to wait:
/// <code>
///		// with a signal
///		while (true)
///		{
///			var signalWaitTask = signal.Wait();
///			// warning: does not provide exclusivity/thread-safety
///			if (!queue.TryDeque(out var item))
///				await signalWaitTask;
///			Process(item);
///		}
///		// is equivalent to the following C++ with a condition var
///		while (true)
///		{
///			std::unique_lock lock(queueMutex);
///			while (queue.size() == 0)
///				conditionVar.wait(lock);
///			auto item = queue.pop_front();
///			Process(item);
///		}
///	</code>
/// </summary>
public class AsyncSignal
{
	private readonly ConcurrentQueue<WorkItem> workQueue = new();
	private volatile int currentToken;
	private readonly object tokenLock = new object();

	internal struct WorkItem
	{
		public Action Continuation { get; set; }
		public ExecutionContext? Context { get; set; }
	}

	/// <summary>
	/// Wait for the signal
	/// </summary>
	/// <returns>An awaitable that completes when <see cref="NotifyAll()"/> is called</returns>
	public AsyncSignalTask Wait()
	{
		return new(this, currentToken);
	}

	/// <summary>
	/// Complete all awaitables that are waiting for this signal
	/// </summary>
	/// <exception cref="InternalReleaseException"></exception>
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
	public void NotifyAll()
	{
		int alreadyQueuedCount;
		lock (tokenLock)
		{
			Interlocked.Increment(ref currentToken);
			alreadyQueuedCount = workQueue.Count;
		}

		while (alreadyQueuedCount > 0)
		{
			if (!workQueue.TryDequeue(out var workItem))
				throw new InternalReleaseException("Failed to dequeue the next continuation.");

			alreadyQueuedCount--;
			Execute(workItem);
		}
	}

	internal bool IsTokenCompleted(int token)
	{
		return currentToken != token;
	}

	internal void AddContinuation(int token, WorkItem item)
	{
		lock (tokenLock)
		{
			if (token == currentToken)
			{
				workQueue.Enqueue(item);
				return;
			}
		}
		Execute(item);
	}

	private static ContextCallback? Runner { get; set; }
	private static void Execute(WorkItem item)
	{
		// cache this allocation
		[DebuggerNonUserCode]
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		static void executionContextRunner(object obj) => (obj as Action)!();
		Runner ??= executionContextRunner!;

		if (item.Context is null)
			item.Continuation();
		else
			ExecutionContext.Run(item.Context, Runner, item.Continuation);
	}
}

/// <summary>
/// An awaitable that posts to the signal. Contains a token to resolve race conditions
/// </summary>
public readonly record struct AsyncSignalTask
{
	private AsyncSignal Signal { get; }
	private int Token { get; }

	internal AsyncSignalTask(
		AsyncSignal signal,
		int token)
	{
		Signal = signal;
		Token = token;
	}

	/// <summary>
	/// Get an awaiter for the current awaitable
	/// </summary>
	public AsyncSignalAwaiter GetAwaiter()
	{
		return new AsyncSignalAwaiter(Signal, Token);
	}
}

/// <summary>
/// <inheritdoc/>
/// </summary>
public readonly record struct AsyncSignalAwaiter : ICriticalNotifyCompletion
{
	private AsyncSignal Signal { get; }
	private int Token { get; }

	internal AsyncSignalAwaiter(AsyncSignal signal, int token)
	{
		Signal = signal;
		Token = token;
	}

	/// <summary>
	/// Whether or not the awaitable has completed
	/// </summary>
	public bool IsCompleted => Signal.IsTokenCompleted(Token);
	/// <summary>
	/// No op
	/// </summary>
	public void GetResult() { }

#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	void INotifyCompletion.OnCompleted(Action continuation)
	{
		Signal.AddContinuation(
			Token,
			new()
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
		Signal.AddContinuation(
			Token,
			new()
			{
				Continuation = continuation,
				Context = null,
			});
	}
}
