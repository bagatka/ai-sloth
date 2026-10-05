namespace Bagatka.AiSloth.Users.Model;

// What a sign-in code is for.
internal enum IssuedCodePurpose
{
    // Makes its redeemer the host's first person, while nobody has signed up.
    Setup = 1,

    // Signs its creator in on another device.
    Link = 2,
}
