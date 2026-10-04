using System;
using System.Globalization;
using Bagatka.Foundation;
using Grpc.Core;

namespace Bagatka.AiSloth.WebApi.Endpoints;

// How the gRPC endpoints read credentials and IDs, and report a rejected call.
internal static class GrpcCalls
{
    // The caller's secret, from the authorization metadata every call carries.
    public static string BearerToken(ServerCallContext context)
    {
        const string Scheme = "Bearer ";
        string? authorization = context.RequestHeaders.GetValue("authorization");
        if (authorization is null || !authorization.StartsWith(Scheme, StringComparison.Ordinal))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Calls carry a bearer token."));
        }

        return authorization[Scheme.Length..];
    }

    public static Guid ParseId(string value)
    {
        bool parsed = Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid id);
        if (!parsed)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "IDs are UUIDs."));
        }

        return id;
    }

    public static string FormatId(Guid value)
    {
        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    public static RpcException Rejection(Error error)
    {
        StatusCode status = error.Kind switch
        {
            ErrorKind.Validation => StatusCode.InvalidArgument,
            ErrorKind.Unauthorized => StatusCode.Unauthenticated,
            ErrorKind.Forbidden => StatusCode.PermissionDenied,
            ErrorKind.NotFound => StatusCode.NotFound,
            ErrorKind.Conflict => StatusCode.FailedPrecondition,
        };
        return new RpcException(new Status(status, error.Message));
    }
}
