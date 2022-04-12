//#define DEBUG_ASYNC

using System.Runtime.Serialization;

namespace CoroutineScheduler;

/// <summary>
/// When an error occurred attempting to resume a scheduler
/// </summary>
public class InternalReleaseException : SystemException
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
	public InternalReleaseException(string message) : base(message) { }
	public InternalReleaseException(string message, Exception innerException) : base(message, innerException) { }
	protected InternalReleaseException(SerializationInfo info, StreamingContext context) : base(info, context) { }
#pragma warning restore CS1591
}
