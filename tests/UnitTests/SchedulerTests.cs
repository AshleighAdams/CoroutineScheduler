using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using FluentAssertions;

using CoroutineScheduler;

using Xunit;
using System.Threading;

[assembly: CLSCompliant(true)]

namespace UnitTests;

public class SchedulerTests
{
	[Fact]
	public void SpawnTaskExecutesArgument()
	{
		var scheduler = new Scheduler();

		int x = 0;
		Task taskFunc()
		{
			x = 10;
			return Task.CompletedTask;
		};

		scheduler.SpawnTask(taskFunc);

		x.Should().Be(10);
	}

	[Fact]
	public void ResumeContinuesTaskExecution()
	{
		var scheduler = new Scheduler();

		int x = 0;
		async Task taskFunc()
		{
			x = 10;
			await scheduler.Yield();
			x = 20;
		};

		scheduler.SpawnTask(taskFunc);
		x.Should().Be(10);
		scheduler.Resume();
		x.Should().Be(20);
	}

	[Fact]
	public void MultipleAwaitsBlockMultipleTimes()
	{
		var scheduler = new Scheduler();

		int x = 0;
		async Task taskFunc()
		{
			x = 10;
			await scheduler.Yield();
			x = 20;
			await scheduler.Yield();
			x = 30;
		}

		scheduler.SpawnTask(taskFunc);
		x.Should().Be(10);
		scheduler.Resume();
		x.Should().Be(20);
		scheduler.Resume();
		x.Should().Be(30);
	}

	[Fact]
	public void ResumesContinuesOnlyAlreadyQueued()
	{
		var scheduler = new Scheduler();

		var list = new List<int>();
		async Task taskFunc()
		{
			async Task nestedTaskFunc()
			{
				list.Add(13);
				await scheduler.Yield();
				list.Add(17);
				await scheduler.Yield();
				list.Add(30);
			}

			list.Add(10);
			Task subTask = nestedTaskFunc();
			await scheduler.Yield();
			list.Add(20);
		}

		list.Should().BeEmpty();
		scheduler.SpawnTask(taskFunc);
		list.Should().BeEquivalentTo(new int[] { 10, 13 });
		scheduler.Resume();
		list.Should().BeEquivalentTo(new int[] { 10, 13, 17, 20 });
		scheduler.Resume();
		list.Should().BeEquivalentTo(new int[] { 10, 13, 17, 20, 30 });
	}

	[Fact]
	public void ResumesContinuesInCorrectOrder()
	{
		var scheduler = new Scheduler();

		var list = new List<int>();
		async Task addValue(int value)
		{
			await scheduler.Yield();
			list.Add(value);
		}

		var tasks = new List<Task>
			{
				addValue(3),
				addValue(1),
				addValue(2),
			};

		list.Should().BeEmpty();
		scheduler.Resume();
		list.Should().BeEquivalentTo(new int[] { 3, 1, 2 });

		foreach (Task t in tasks)
			t.IsCompletedSuccessfully.Should().BeTrue();
	}

	[Fact]
	public void ContinuationsAlwaysSameThread()
	{
		int threadId = System.Environment.CurrentManagedThreadId;
		void checkThreadId() => System.Environment.CurrentManagedThreadId.Should().Be(threadId);

		var scheduler = new Scheduler();
		bool done = false;

		async Task taskFunc()
		{
			checkThreadId();
			for (int i = 0; i < 10; i++)
				await scheduler.Yield();
			checkThreadId();
			for (int i = 0; i < 10; i++)
				await Task.Yield();
			checkThreadId();
			done = true;
		}

		scheduler.SpawnTask(taskFunc);

		for (int i = 0; i < 10; i++)
		{
			done.Should().BeFalse();
			scheduler.Resume();
		}
		done.Should().BeTrue();
	}

	[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "For test only")]
	internal class TestException : Exception
	{
	}

	[Fact]
	public void InstantAsyncExceptionsFireEventHandler()
	{
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
		static async Task taskFunc() => throw new TestException();
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously

		var scheduler = new Scheduler();

		var threw = false;
		scheduler.UnhandledException += (sender, e) =>
		{
			threw.Should().BeFalse();
			threw = true;
			e.Rethrow = e.RethrowAsync = false;
			e.Exception.Should().BeOfType<TestException>();
		};

		scheduler.SpawnTask(taskFunc);

		threw.Should().BeTrue();
	}

	[Fact]
	public void InstantSyncExceptionsFireEventHandler()
	{
		static Task taskFunc() => throw new TestException();

		var scheduler = new Scheduler();

		var threw = false;
		scheduler.UnhandledException += (sender, e) =>
		{
			threw.Should().BeFalse();
			threw = true;
			e.Rethrow = e.RethrowAsync = false;
			e.Exception.Should().BeOfType<TestException>();
		};

		scheduler.SpawnTask(taskFunc);

		threw.Should().BeTrue();
	}

	[Fact]
	public void DeferredAsyncExceptionsFireEventHandler()
	{
		var scheduler = new Scheduler();

		async Task taskFunc()
		{
			await scheduler.Yield();
			throw new TestException();
		}

		var threw = false;
		scheduler.UnhandledException += (sender, e) =>
		{
			threw.Should().BeFalse();
			threw = true;
			e.Rethrow = e.RethrowAsync = false;
			e.Exception.Should().BeOfType<TestException>();
		};

		scheduler.SpawnTask(taskFunc);

		threw.Should().BeFalse();

		scheduler.Resume();

		threw.Should().BeTrue();
	}

	[Fact]
	public void UnflowedExceptionsThrowOnResume()
	{
		var scheduler = new Scheduler();


		var threw = false;
		scheduler.UnhandledException += (_, _) => threw = true;

		scheduler.Yield().GetAwaiter().UnsafeOnCompleted(() => throw new TestException());

		threw.Should().BeFalse();

		Assert.Throws<TestException>(() => scheduler.Resume());

		threw.Should().BeFalse();
	}

	[Fact]
	public void MarshalDoesntYieldWhenSameThread()
	{
		var scheduler = new Scheduler();

		int x = 0;
		async Task taskFunc()
		{
			x = 10;
			await scheduler.Yield();
			x = 20;
			await scheduler.Marshal();
			x = 30;
		}
		;

		scheduler.SpawnTask(taskFunc);
		x.Should().Be(10);
		scheduler.Resume();
		x.Should().Be(30);
	}


	[Fact]
	public void YieldDoesntPostToSyncContext()
	{
		int factThreadId = Thread.CurrentThread.ManagedThreadId;

		var scheduler = new Scheduler();
		using var resetEvent = new AutoResetEvent(false);
		using var resetEventReply = new AutoResetEvent(false);
		var tcs = new TaskCompletionSource<int>();
		int thread1Id = 0;

		var thread1 = new Thread(() =>
		{
			thread1Id = Thread.CurrentThread.ManagedThreadId;
			resetEvent.WaitOne();
			scheduler.Resume();
		});
		thread1.Start();

		async Task test()
		{
			Thread.CurrentThread.ManagedThreadId.Should().Be(factThreadId);

			await scheduler.Yield();

			Thread.CurrentThread.ManagedThreadId.Should().Be(thread1Id);

			resetEventReply.Set();
		}

		var task = scheduler.SpawnTask(test);

		resetEvent.Set();
		resetEventReply.WaitOne(TimeSpan.FromSeconds(1));
	}

	[Fact]
	public void CanMarshalToThread()
	{
		int factThreadId = Thread.CurrentThread.ManagedThreadId;

		var scheduler = new Scheduler();
		using var resetEvent = new AutoResetEvent(false);
		var tcs = new TaskCompletionSource<int>();
		int thread1Id = 0;

		var thread1 = new Thread(() =>
		{
			thread1Id = Thread.CurrentThread.ManagedThreadId;
			resetEvent.WaitOne();
			tcs.TrySetResult(10);
		});
		thread1.Start();

		async Task test()
		{
			Thread.CurrentThread.ManagedThreadId.Should().Be(factThreadId);

			var result = await tcs.Task
				.ConfigureAwait(false); // this is required else we will automatically marshal back to the scheduler
			result.Should().Be(10);

			Thread.CurrentThread.ManagedThreadId.Should().Be(thread1Id);

			await scheduler.Marshal();

			Thread.CurrentThread.ManagedThreadId.Should().Be(factThreadId);
		}

		var task = scheduler.SpawnTask(test);

		resetEvent.Set();

		while (!task.IsCompleted)
			scheduler.Resume();
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
		task.GetAwaiter().GetResult();
#pragma warning restore xUnit1031
	}

	[Fact]
	public void ImplicitlyMarshalsToScheduler()
	{
		int factThreadId = Thread.CurrentThread.ManagedThreadId;

		var scheduler = new Scheduler();
		using var resetEvent = new AutoResetEvent(false);
		var tcs = new TaskCompletionSource<int>();

		var thread1 = new Thread(() =>
		{
			resetEvent.WaitOne();
			tcs.TrySetResult(10);
		});
		thread1.Start();

		async Task test()
		{
			Thread.CurrentThread.ManagedThreadId.Should().Be(factThreadId);

			var result = await tcs.Task; // no configure await false
			result.Should().Be(10);

			Thread.CurrentThread.ManagedThreadId.Should().Be(factThreadId);
		}

		var task = scheduler.SpawnTask(test);

		resetEvent.Set();

		while (!task.IsCompleted)
			scheduler.Resume();
#pragma warning disable xUnit1031 // Do not use blocking task operations in test method
		task.GetAwaiter().GetResult();
#pragma warning restore xUnit1031
	}
}
