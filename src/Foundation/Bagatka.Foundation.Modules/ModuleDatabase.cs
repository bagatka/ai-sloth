using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Foundation.Modules;

// A module context, by how its migrations are applied (ModuleDatabases.MigrateAsync).
internal sealed record ModuleDatabase(Func<IServiceProvider, CancellationToken, Task> MigrateAsync);
