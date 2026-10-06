using System;
using Azure;

namespace Bagatka.Azure.Sandboxes;

// What an IfExists call returns for something that doesn't exist.
internal sealed class MissingResponse<T>(Response response) : NullableResponse<T>
{
    public override bool HasValue => false;

    public override T Value => throw new InvalidOperationException("It doesn't exist; check HasValue first.");

    public override Response GetRawResponse()
    {
        return response;
    }
}
