using System.Diagnostics;
using System.Runtime.CompilerServices;

[assembly: CLSCompliant(true)]

namespace CoroutineScheduler;

/// <summary>
/// Interface to handle concurrency cooperatively.
/// </summary>
public interface IScheduler
{
	/// <summary>
	/// Spawn a task to be scheduled by this scheduler. <br />
	/// Any exception thrown by <paramref name="func"/> is to be regarded as if it were unhandled.
	/// </summary>
	/// <param name="func"></param>
	/// <returns>A <c>Task</c> that completes when <paramref name="func"/> completes. Propagates exceptions.</returns>
	Task SpawnTask(Func<Task> func);

	/// <summary>
	/// Yield to this scheduler.<br/>
	/// </summary>
	/// <returns>An awaitable whose awaiter completes at the mercy of this scheduler.</returns>
	YieldTask Yield();

	/// <summary>
	/// Marshal to this scheduler.<br/>
	/// </summary>
	/// <returns>An awaitable whose awaiter completes at the mercy of this scheduler for the purposes of marshalling in some way, such as between threads.</returns>
	YieldTask Marshal();
}

/// <summary>
/// A task to be awaited, upon which the scheduler will yield control back at some
/// point defined by the implementation.
/// </summary>
public record struct YieldTask
{
	private IYieldAwaiter Awaiter { get; }

	/// <summary>
	/// A struct container to provide the compiler duck-typeing so an <see cref="IYieldAwaiter"/> can be <c>await</c>ed.
	/// </summary>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[DebuggerNonUserCode]
	public YieldTask(IYieldAwaiter awaiter)
	{
		Awaiter = awaiter;
	}

	/// <summary>
	/// Used by compiler for awaiting.
	/// </summary>
	/// <returns>An interface for whom to notify we're awaiting, with further compiler invoked.</returns>
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	[DebuggerNonUserCode]
	public IYieldAwaiter GetAwaiter()
	{
		return Awaiter;
	}
}

/// <summary>
/// Virtual dispatch for the awaiter, likely to be reused/nonspecific to reduce allocations.
/// </summary>
public interface IYieldAwaiter : ICriticalNotifyCompletion
{
	/// <summary>
	/// Implementing pattern required by the compiler.
	/// </summary>
	public bool IsCompleted { get; }
	/// <summary>
	/// Implementing pattern required by the compiler.
	/// </summary>
	[DebuggerNonUserCode]
	public void GetResult();
}
