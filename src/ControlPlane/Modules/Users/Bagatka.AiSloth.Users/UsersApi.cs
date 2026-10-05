using System;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Users.Data;

namespace Bagatka.AiSloth.Users;

// The contract's front door: dependencies only. Each feature is a file in Features/.
internal sealed partial class UsersApi(UsersDbContext db, TimeProvider time) : IUsersApi
{
}
