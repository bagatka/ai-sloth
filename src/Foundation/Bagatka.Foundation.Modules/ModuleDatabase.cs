using System;

namespace Bagatka.Foundation.Modules;

// A module context whose migrations ModuleDatabases.MigrateAsync applies.
internal sealed record ModuleDatabase(Type ContextType);
