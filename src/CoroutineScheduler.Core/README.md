[![Codecov][codecov-badge]][codecov-link] [![Mutation testing score][mutation-testing-badge]][mutation-testing-link]

Core interfaces for spawning coroutines and yielding to a scheduler, no implementations contained within. 

## Usage

```csharp
namespace CoroutineScheduler;

public interface IScheduler
{
	Task SpawnTask(Func<Task> func);
	YieldTask Yield();
}
```

## Full readme

[View the full readme on GitHub][full-readme] for additional details.


[full-readme]: https://github.com/AshleighAdams/CoroutineScheduler/blob/master/README.md
[codecov-badge]: https://codecov.io/gh/AshleighAdams/CoroutineScheduler/branch/master/graph/badge.svg?token=ZE1ITHB3U3
[codecov-link]: https://codecov.io/gh/AshleighAdams/CoroutineScheduler
[mutation-testing-badge]: https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2FAshleighAdams%2FCoroutineScheduler%2Fmaster
[mutation-testing-link]: https://dashboard.stryker-mutator.io/reports/github.com/AshleighAdams/CoroutineScheduler/master
