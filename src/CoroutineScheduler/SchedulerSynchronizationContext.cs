//#define DEBUG_ASYNC

#if !DEBUG_ASYNC
using System.Diagnostics;
#endif

using TPoster = System.Action<System.Threading.SendOrPostCallback, object?>;

namespace CoroutineScheduler;

internal sealed class SchedulerSynchronizationContext : SynchronizationContext
{
	private TPoster Poster { get; }
	public SchedulerSynchronizationContext(TPoster poster)
	{
		Poster = poster;
	}

	/// <inheritdoc/>
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	public override void Send(SendOrPostCallback d, object? state)
	{
		d(state);
	}

	/// <inheritdoc/>
#if !DEBUG_ASYNC
	[DebuggerNonUserCode]
#endif
	public override void Post(SendOrPostCallback d, object? state)
	{
		Poster(d, state);
	}
}
