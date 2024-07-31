using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;

using CoroutineScheduler;

using Xunit;

namespace UnitTests;

public class AsyncSignalTests
{
	[Fact]
	public void SimpleWaitingFunctional()
	{
		var signal = new AsyncSignal();

		int x = 0;
		async Task taskFunc()
		{
			x = 10;
			await signal.Wait();
			x = 20;
			await signal.Wait();
			x = 30;
		};

		var t = taskFunc();
		x.Should().Be(10);
		signal.NotifyAll();
		x.Should().Be(20);
		signal.NotifyAll();
		x.Should().Be(30);
	}

	[Fact]
	public void OldWaitsDontWait()
	{
		var signal = new AsyncSignal();

		int x = 0;
		async Task taskFunc()
		{
			x = 10;
			var oldWait = signal.Wait();
			await signal.Wait();
			x = 20;
			await oldWait;
			x = 30;
		};

		var t = taskFunc();
		x.Should().Be(10);
		signal.NotifyAll();
		x.Should().Be(30);
	}
}
