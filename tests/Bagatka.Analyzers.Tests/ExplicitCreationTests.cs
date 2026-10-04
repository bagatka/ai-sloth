using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Bagatka.Analyzers.Tests;

public sealed class ExplicitCreationTests
{
    [Fact]
    public async Task Creation_names_its_type_and_unions_are_created_with_new()
    {
        const string sample = """
            using System.Collections.Generic;

            internal sealed record Cat(string Name);
            internal sealed record Dog(string Name);
            internal union Pet(Cat, Dog);

            internal static class Sample
            {
                public static Pet Adopt(bool likesCats)
                {
                    List<Pet> pets = new List<Pet>();
                    List<Pet> more = new(); // BAG0005
                    pets.Add(new Pet(new Cat("Tom")));
                    pets.Add(new Dog("Rex")); // BAG0006
                    Pet first = pets[0];
                    if (likesCats)
                    {
                        return new Cat("Felix"); // BAG0006
                    }

                    return first;
                }
            }
            """;

        await Analysis.VerifyAsync(sample, OutputKind.DynamicallyLinkedLibrary);
    }
}
