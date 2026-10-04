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
        return authorization is not null && authorization.StartsWith(Scheme, StringComparison.Ordinal)
            ? authorization[Scheme.Length..]
            : throw new RpcException(new Status(StatusCode.Unauthenticated, "Calls carry a bearer token."));
    }

    public static Guid ParseId(string value)
    {
        return Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid id)
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "IDs are UUIDs."));
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
