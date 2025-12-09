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
	private volatile List<RegisteredWorkItem> workQueueFront = new();
	private volatile List<RegisteredWorkItem> workQueueBack = new();
	private volatile int currentToken;
	private readonly object workQueueFrontLock = new();
	private readonly ConcurrentBag<RegisteredWorkItem> workItemPool = new();

	internal struct WorkItem
	{
		public Action Continuation { get; set; }
		public ExecutionContext? Context { get; set; }

		private static ContextCallback? Runner { get; set; }
		public void Invoke()
		{
			// cache this allocation
			[DebuggerNonUserCode]
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			static void executionContextRunner(object obj) => (obj as Action)!();
			Runner ??= executionContextRunner!;

			if (Context is null)
				Continuation();
			else
				ExecutionContext.Run(Context, Runner, Continuation);
		}
	}

	internal class RegisteredWorkItem
	{
		public WorkItem Item { get; private set; }
		public CancellationTokenRegistration CancellationTokenRegistration { get; set; }
		private volatile int completed;

		public void Reset(WorkItem item)
		{
			Item = item;
			completed = 0;
			CancellationTokenRegistration = default;
		}

		public bool TryComplete()
		{
			int was = Interlocked.Exchange(ref completed, 1);
			return was == 0;
		}
	}

	/// <summary>
	/// Wait for the signal
	/// </summary>
	/// <returns>An awaitable that completes when <see cref="NotifyAll()"/> is called</returns>
	public AsyncSignalTask Wait(CancellationToken ct = default)
	{
		return new(this, currentToken, ct);
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
		lock (workQueueFrontLock)
		{
			Interlocked.Increment(ref currentToken);
			(workQueueFront, workQueueBack) = (workQueueBack, workQueueFront);
		}

		foreach (var workItem in workQueueBack)
		{
			if (!workItem.TryComplete())
				throw new InvalidOperationException("Dequeued work item somehow completed in back queue");

			workItem.CancellationTokenRegistration.Dispose();
			workItem.Item.Invoke();
			workItem.Reset(default);
			
			workItemPool.Add(workItem);
		}
		workQueueBack.Clear();
	}

	internal bool IsTokenCompleted(int token)
	{
		return currentToken != token;
	}

	internal void AddContinuation(int token, WorkItem item, CancellationToken ct)
	{
		lock (workQueueFrontLock)
		{
			if (token == currentToken)
			{
				if (!workItemPool.TryTake(out var regItem))
					regItem = new();

				regItem.Reset(item);
				workQueueFront.Add(regItem);

				if (ct.CanBeCanceled)
					regItem.CancellationTokenRegistration = ct.Register(TokenCancelled, regItem, false);
				return;
			}
		}

		item.Invoke();
	}

	internal void TokenCancelled(object objItem)
	{
		if (objItem is not RegisteredWorkItem item)
			throw new InvalidOperationException($"Unexpected item type");

		bool removedItem;
		lock (workQueueFrontLock)
		{
			removedItem = workQueueFront.Remove(item);
		}

		if (!removedItem)
			return;

		if (!item.TryComplete())
			return;
		
		var ret = item.Item;
		item.Reset(default);

		workItemPool.Add(item);

		ret.Invoke();
	}
}

/// <summary>
/// An awaitable that posts to the signal. Contains a token to resolve race conditions
/// </summary>
public class AsyncSignalTask
{
	private AsyncSignal Signal { get; }
	private int Token { get; }
	private CancellationToken Ct { get; }

	internal AsyncSignalTask(
		AsyncSignal signal,
		int token,
		CancellationToken ct)
	{
		Signal = signal;
		Token = token;
		Ct = ct;
	}

	/// <summary>
	/// Get an awaiter for the current awaitable
	/// </summary>
	public AsyncSignalAwaiter GetAwaiter()
	{
		return new AsyncSignalAwaiter(Signal, Token, Ct);
	}
}

/// <summary>
/// <inheritdoc/>
/// </summary>
public readonly record struct AsyncSignalAwaiter : ICriticalNotifyCompletion
{
	private AsyncSignal Signal { get; }
	private int Token { get; }
	private CancellationToken Ct { get; }

	internal AsyncSignalAwaiter(AsyncSignal signal, int token, CancellationToken ct)
	{
		Signal = signal;
		Token = token;
		Ct = ct;
	}

	/// <summary>
	/// Whether or not the awaitable has completed
	/// </summary>
	public bool IsCompleted => Signal.IsTokenCompleted(Token) || Ct.IsCancellationRequested;
	/// <summary>
	/// No op
	/// </summary>
	public void GetResult()
	{
		Ct.ThrowIfCancellationRequested();
	}

	private CancellationTokenRegistration Registration { get; }

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
			},
			Ct);
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
			},
			Ct);
	}
}
